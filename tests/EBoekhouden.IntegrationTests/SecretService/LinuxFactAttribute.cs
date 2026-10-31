using Xunit;
using System.Runtime.CompilerServices;

namespace EBoekhouden.IntegrationTests.SecretService;

internal sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsLinux() || IntPtr.Size != 8) Skip = "Native adapter tests require 64-bit Linux.";
    }
}

internal sealed class LinuxTheoryAttribute : TheoryAttribute
{
    public LinuxTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsLinux() || IntPtr.Size != 8) Skip = "Native adapter tests require 64-bit Linux.";
    }
}
