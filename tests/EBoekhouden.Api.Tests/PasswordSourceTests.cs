using System.Net;
using System.Security.Cryptography;
using System.Text;
using EBoekhouden.Api.Authentication;
using EBoekhouden.Api.Configuration;
using EBoekhouden.Api.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EBoekhouden.Api.Tests;

public sealed class PasswordSourceTests
{
    [Theory]
    [InlineData(" ")]
    [InlineData("\u2003")]
    [InlineData("")]
    [InlineData("a\0b")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\u007fb")]
    [InlineData("a\u0085b")]
    public void RejectsBlankOrControlValues(string value) =>
        Assert.Throws<ApiTransportException>(() => Password.FromUtf8(Encoding.UTF8.GetBytes(value)));

    [Fact]
    public void StrictUtf8ByteLimitsAndOwnedBufferDisposal()
    {
        foreach (var value in new[] { new byte[] { 0xc0, 0xaf }, new byte[] { 0xed, 0xa0, 0x80 }, new byte[] { 0xf4, 0x90, 0x80, 0x80 }, new byte[] { 0xff } })
            Assert.Throws<ApiTransportException>(() => Password.FromUtf8(value));
        var bytes = Encoding.UTF8.GetBytes(" " + new string('é', 2047) + "a"); // exactly 4096 bytes
        using var password = Password.FromUtf8(bytes);
        Assert.Equal(bytes, password.Bytes.ToArray());
        Assert.Throws<ApiTransportException>(() => Password.FromUtf8(new byte[4097]));
        var owned = Encoding.UTF8.GetBytes(" synthetic ");
        var disposable = new Password(owned);
        disposable.Dispose(); disposable.Dispose();
        Assert.All(owned, b => Assert.Equal(0, b));
        Assert.Throws<ObjectDisposedException>(() => disposable.Bytes.ToArray());
        Assert.Equal("Password", password.ToString());
    }

    [Theory]
    [InlineData("Keyring", null)]
    [InlineData("Keyring:Application", null)]
    [InlineData("Keyring:Application", "")]
    [InlineData("Keyring:Account", " ")]
    [InlineData("Keyring:Account", "a\0b")]
    [InlineData("Keyring:Unknown", "x")]
    [InlineData("AccessToken", "synthetic")]
    [InlineData("PasswordFile", "/tmp/synthetic")]
    [InlineData("PasswordSource:Kind", "libsecret")]
    [InlineData("Unknown", "x")]
    public void InvalidAndRemovedConfigurationFailsWithoutNativeAccess(string key, string? value)
    {
        var values = KeyringConfig();
        if (key == "Keyring") foreach (var field in values.Keys.Where(k => k.Contains("Keyring", StringComparison.Ordinal)).ToArray()) values.Remove(field);
        values["EBoekhouden:" + key] = value;
        var services = new ServiceCollection();
        services.AddEBoekhoudenApi(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<EBoekhoudenOptions>>().Value);
    }

    [Fact]
    public void DefaultAndCustomAttributesSelectTheOnlyProductionSourceWithoutLookup()
    {
        foreach (var values in new[] { new Dictionary<string, string?> { ["EBoekhouden:Source"] = "Tests" }, KeyringConfig() })
        {
            var services = new ServiceCollection();
            services.AddEBoekhoudenApi(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            using var provider = services.BuildServiceProvider();
            var settings = provider.GetRequiredService<IOptions<EBoekhoudenOptions>>().Value;
            Assert.Equal("EBoekhoudenMcp", settings.Keyring.Application);
            Assert.Equal("api", settings.Keyring.Account);
            Assert.IsType<KeyringPasswordSource>(provider.GetRequiredService<IPasswordSource>());
        }
    }

    [Fact]
    public async Task PrecancelledLookupDoesNotLoadNativeLibraries()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var source = new KeyringPasswordSource(new KeyringOptions());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetAsync(cancellation.Token).AsTask());
    }

    [Fact]
    public async Task DeferredSourceIsNotCalledByTlsFailureAndContentIsOneShotAndCleared()
    {
        var source = new Source();
        // Simulate a connection/identity rejection before HttpClient can serialize request content.
        using (var client = new HttpClient(new StubHandler((_, _) => throw new HttpRequestException("synthetic TLS rejection"))))
        using (var message = new HttpRequestMessage(HttpMethod.Post, "https://127.0.0.1/v1/session")
        { Content = new SessionContent(source, "Tests", TestContext.Current.CancellationToken) })
            await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(message, TestContext.Current.CancellationToken));
        Assert.Equal(0, source.Calls);
        using var content = new SessionContent(source, "Tests", TestContext.Current.CancellationToken);
        using var output = new MemoryStream();
        await content.CopyToAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal("""{"accessToken":" synthetic ","source":"Tests"}""", Encoding.UTF8.GetString(output.ToArray()));
        Assert.All(source.LastBytes!, b => Assert.Equal(0, b));
        await Assert.ThrowsAsync<ApiTransportException>(() => content.CopyToAsync(output, TestContext.Current.CancellationToken));
        Assert.Equal(1, source.Calls);
        CryptographicOperations.ZeroMemory(output.GetBuffer());
    }

    internal static Dictionary<string, string?> KeyringConfig() => new()
    {
        ["EBoekhouden:Source"] = "Tests",
        ["EBoekhouden:Keyring:Application"] = "EBoekhoudenMcp",
        ["EBoekhouden:Keyring:Account"] = "api",
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceAndSerializedOwnedBuffersAreClearedWhenWritingFailsOrCancels(bool cancel)
    {
        var source = new Source();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var content = new SessionContent(source, "Tests", cancellation.Token);
        using var stream = new FailedWrite(cancel);
        var writing = content.CopyToAsync(stream, cancellation.Token);
        await stream.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        if (cancel)
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing);
        }
        else await Assert.ThrowsAsync<HttpRequestException>(() => writing);
        Assert.All(source.LastBytes!, b => Assert.Equal(0, b));
        var observed = stream.Observed;
        Assert.True(observed.Span.IndexOfAnyExcept((byte)0) < 0);
    }

    private sealed class FailedWrite(bool cancel) : MemoryStream
    {
        internal ReadOnlyMemory<byte> Observed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Observed = buffer;
            Entered.TrySetResult();
            if (cancel) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new IOException("synthetic write failure");
        }
    }

    private sealed class Source : IPasswordSource
    {
        internal int Calls;
        internal byte[]? LastBytes;
        public ValueTask<Password> GetAsync(CancellationToken cancellationToken)
        {
            Calls++;
            LastBytes = Encoding.UTF8.GetBytes(" synthetic ");
            return ValueTask.FromResult(new Password(LastBytes));
        }
    }
}
