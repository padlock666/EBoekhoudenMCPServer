using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace EBoekhouden.IntegrationTests;

// Own the process so raw stdout can be checked independently of the SDK's permissive line parser.
internal sealed class StdioProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Pipe _protocol = new(new PipeOptions(pauseWriterThreshold: long.MaxValue));
    private readonly MemoryStream _stdout = new();
    private readonly MemoryStream _stderr = new();
    private readonly Task _outPump;
    private readonly Task _errPump;
    private readonly RecordingInput _input;
    private readonly string _directory;
    private readonly CancellationTokenSource _deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    internal CancellationToken Token => _deadline.Token;
    internal McpClient Client { get; private set; } = null!;
    internal string Sent => _input.Text;
    internal string Stdout => Encoding.UTF8.GetString(_stdout.ToArray());
    internal string Stderr => Encoding.UTF8.GetString(_stderr.ToArray());
    internal int Id => _process.Id;
    internal int ExitCode => _process.ExitCode;

    private StdioProcess(Uri origin, IReadOnlyDictionary<string, string?>? environment)
    {
        _deadline.CancelAfter(TimeSpan.FromSeconds(30));
        _directory = Path.Combine(Path.GetTempPath(), "eboekhouden-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = _directory, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "StdioHost", "EBoekhouden.Host.Stdio.dll"));
        // Do not inherit user credentials, secrets providers, proxies, or logging configuration.
        start.Environment.Clear();
        foreach (var key in new[] { "PATH", "SystemRoot", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64", "TMP", "TEMP", "TMPDIR" })
            if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
        start.Environment["DOTNET_ENVIRONMENT"] = "IntegrationTest";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["EBoekhoudenTest__UpstreamOrigin"] = origin.AbsoluteUri;
        start.Environment["EBoekhouden__AccessToken"] = FakeUpstream.Credential;
        start.Environment["EBoekhouden__Source"] = "Tests";
        start.Environment["Logging__LogLevel__Default"] = "Trace";
        start.Environment["Logging__LogLevel__ModelContextProtocol"] = "Trace";
        start.Environment["Logging__Console__LogToStandardErrorThreshold"] = "None";
        if (environment is not null)
            foreach (var (key, value) in environment)
                if (value is null) start.Environment.Remove(key); else start.Environment[key] = value;
        _process = new Process { StartInfo = start };
        try
        {
            if (!_process.Start()) throw new InvalidOperationException("Unable to launch stdio host.");
        }
        catch { _process.Dispose(); _deadline.Dispose(); Directory.Delete(_directory, true); throw; }
        _input = new RecordingInput(_process.StandardInput.BaseStream);
        _outPump = PumpAsync(_process.StandardOutput.BaseStream, _stdout, true);
        _errPump = PumpAsync(_process.StandardError.BaseStream, _stderr, false);
    }

    internal static StdioProcess Start(Uri origin, IReadOnlyDictionary<string, string?>? environment = null) => new(origin, environment);

    internal async Task ConnectAsync(string protocolVersion = "2025-11-25")
    {
        Client = await McpClient.CreateAsync(new StreamClientTransport(_input, _protocol.Reader.AsStream()), new McpClientOptions
        {
            ProtocolVersion = protocolVersion, InitializationTimeout = TimeSpan.FromSeconds(10),
            ClientInfo = new Implementation { Name = "synthetic-integration-client", Version = "1" },
        }, cancellationToken: Token);
    }

    internal ValueTask<CallToolResult> CallAsync(string name, string arguments = "{}") =>
        Client.CallToolAsync(name, JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments)!, cancellationToken: Token);

    private async Task PumpAsync(Stream source, MemoryStream capture, bool protocol)
    {
        var buffer = new byte[8192];
        var forward = protocol;
        try
        {
            while (await source.ReadAsync(buffer) is var read && read != 0)
            {
                if (capture.Length + read > 8 * 1024 * 1024) throw new InvalidDataException("Child output exceeded the synthetic test budget.");
                capture.Write(buffer, 0, read);
                if (forward) forward = !(await _protocol.Writer.WriteAsync(buffer.AsMemory(0, read))).IsCompleted;
            }
        }
        finally { if (protocol) await _protocol.Writer.CompleteAsync(); }
    }

    internal async Task WaitForExitAsync()
    {
        await _process.WaitForExitAsync(Token).WaitAsync(TimeSpan.FromSeconds(5), Token);
        await Task.WhenAll(_outPump, _errPump).WaitAsync(Token);
    }

    internal async Task CloseInputAndWaitAsync()
    {
        _process.StandardInput.Close();
        await WaitForExitAsync();
        Assert.Equal(0, ExitCode);
    }

    internal void AssertCleanOutput()
    {
        foreach (var line in Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var json = JsonDocument.Parse(line);
            Assert.Equal("2.0", json.RootElement.GetProperty("jsonrpc").GetString());
            Assert.True(json.RootElement.TryGetProperty("result", out _) || json.RootElement.TryGetProperty("error", out _) || json.RootElement.TryGetProperty("method", out _));
        }
        Assert.False(string.IsNullOrWhiteSpace(Stderr)); // Diagnostics were captured separately, not discarded.
        AssertSafe(Stderr);
    }

    internal static void AssertSafe(string text)
    {
        foreach (var marker in new[] { FakeUpstream.Credential, FakeUpstream.SessionPrefix, FakeUpstream.PrivateData })
            Assert.False(text.Contains(marker, StringComparison.Ordinal), "Sensitive synthetic marker escaped into logs or an error.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            try { _process.StandardInput.Close(); } catch (IOException) { }
            if (Client is not null) await Client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            try
            {
                if (!_process.HasExited)
                {
                    using var graceful = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try { await _process.WaitForExitAsync(graceful.Token); }
                    catch (OperationCanceledException) { _process.Kill(entireProcessTree: true); }
                }
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Task.WhenAll(_outPump, _errPump).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                await _protocol.Reader.CompleteAsync();
                _process.Dispose(); _input.Dispose(); _stdout.Dispose(); _stderr.Dispose(); _deadline.Dispose();
                Directory.Delete(_directory, true);
            }
        }
    }

    private sealed class RecordingInput(Stream inner) : Stream
    {
        private readonly MemoryStream _recorded = new();
        internal string Text { get { lock (_recorded) return Encoding.UTF8.GetString(_recorded.ToArray()); } }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override void Write(byte[] buffer, int offset, int count)
        { lock (_recorded) _recorded.Write(buffer, offset, count); inner.Write(buffer, offset, count); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lock (_recorded) _recorded.Write(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        protected override void Dispose(bool disposing)
        { if (disposing) { inner.Dispose(); _recorded.Dispose(); } base.Dispose(disposing); }
    }
}
