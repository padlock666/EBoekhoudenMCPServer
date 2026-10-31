using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Xunit;

namespace EBoekhouden.IntegrationTests.SecretService;

internal sealed class PrivateSecretService : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "eboekhouden-private-bus-" + Guid.NewGuid().ToString("N"));
    private Process? _bus, _service;
    private Task? _events, _errors, _busErrors;
    private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();
    internal List<string> Events { get; } = [];
    internal string Address { get; private set; } = "";
    internal string DirectoryPath => _directory;
    internal Dictionary<string, string?> Environment => new()
    {
        ["DBUS_SESSION_BUS_ADDRESS"] = Address,
        ["HOME"] = _directory, ["XDG_CONFIG_HOME"] = _directory + "/config",
        ["XDG_DATA_HOME"] = _directory + "/data", ["XDG_CACHE_HOME"] = _directory + "/cache",
        ["XDG_RUNTIME_DIR"] = _directory + "/run",
    };

    internal static async Task<PrivateSecretService> StartAsync(bool fake = true)
    {
        var instance = new PrivateSecretService();
        try { await instance.StartCoreAsync(fake); return instance; }
        catch { await instance.DisposeAsync(); throw; }
    }

    private async Task StartCoreAsync(bool fake)
    {
        foreach (var path in new[] { _directory, _directory + "/run", _directory + "/config", _directory + "/data", _directory + "/cache" })
        {
            Directory.CreateDirectory(path);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        // No activation directories, includes, desktop environment or preexisting bus.
        var config = _directory + "/bus.conf";
        await File.WriteAllTextAsync(config, "<busconfig><type>session</type><listen>unix:tmpdir=" + _directory +
            "</listen><auth>EXTERNAL</auth><policy context=\"default\"><allow send_destination=\"*\"/><allow receive_sender=\"*\"/><allow own=\"*\"/></policy></busconfig>", TestContext.Current.CancellationToken);
        _bus = Start("/usr/bin/dbus-daemon", "--nofork", "--print-address=1", "--config-file=" + config);
        _busErrors = _bus.StandardError.ReadToEndAsync();
        Address = await _bus.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5))
            ?? throw new InvalidOperationException("Private test bus did not start.");
        if (!fake) return;
        _service = Start("/usr/bin/python3", Path.Combine(AppContext.BaseDirectory, "SecretService", "fake_service.py"), Address);
        _errors = _service.StandardError.ReadToEndAsync();
        _events = Task.Run(async () =>
        {
            while (await _service.StandardOutput.ReadLineAsync() is { } line)
            {
                lock (Events) Events.Add(line);
                _messages.Writer.TryWrite(line);
            }
            _messages.Writer.TryComplete();
        });
        await WaitAsync("ready");
    }

    internal Process Start(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = _directory,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment.Clear();
        info.Environment["PATH"] = "/usr/bin:/bin";
        foreach (var pair in Environment.Where(p => p.Value is not null && !p.Key.StartsWith("EBoekhouden", StringComparison.Ordinal)))
            info.Environment[pair.Key] = pair.Value;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException("Private fixture process could not start.");
    }

    internal async Task CommandAsync(object command)
    {
        await _service!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(command));
        await _service.StandardInput.FlushAsync();
        await WaitAsync("ack");
    }
    internal async Task WaitAsync(string expected)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        while (await _messages.Reader.ReadAsync(timeout.Token) is { } line)
            if (line == expected) return;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            try { await Stop(_service); }
            finally { await Stop(_bus, graceful: false); }
            if (_events is not null) await _events;
            if (_errors is not null) Assert.Equal("", await ((Task<string>)_errors));
            if (_busErrors is not null) Assert.Equal("", await ((Task<string>)_busErrors));
            lock (Events) Assert.DoesNotContain(Events, e => e.StartsWith("FORBIDDEN:", StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    internal static async Task Stop(Process? process, bool graceful = true)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                if (!graceful) process.Kill(entireProcessTree: true);
                else
                {
                process.StandardInput.Close();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { process.Kill(entireProcessTree: true); }
                }
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { process.Dispose(); }
    }
}
