using Microsoft.Extensions.Configuration;

namespace EBoekhouden.Host.Stdio;

internal static class FileConfiguration
{
    internal static void Add(ConfigurationManager configuration, string[] args)
    {
        using var arguments = new ConfigurationManager();
        arguments.AddCommandLine(args);
        if (arguments["config"] is not { } path) return;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("The configuration file requires an absolute path.");

        using var file = new ConfigurationManager();
        file.AddJsonFile(path, optional: false, reloadOnChange: false);
        // Explicit nonsecret settings; environment and CLI retain their usual priority.
        configuration.AddInMemoryCollection(file.AsEnumerable()).AddEnvironmentVariables().AddCommandLine(args);
    }
}
