using Xunit;

namespace ProcessForge.Tests;

/// <summary>
/// Fact that runs on Windows only and is reported as skipped elsewhere, rather than passing without asserting.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only.";
        }
    }
}
