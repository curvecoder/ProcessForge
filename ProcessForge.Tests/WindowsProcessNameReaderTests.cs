using System.Diagnostics;
using AwesomeAssertions;
using ProcessForge.Inspectors;
using Xunit;

namespace ProcessForge.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsProcessNameReaderTests
{
    [WindowsFact]
    public void Read_WhenProcessIsRunning_ReturnsSameNameAsProcessClass()
    {
        // Arrange
        using var current = Process.GetCurrentProcess();

        // Act
        var name = WindowsProcessNameReader.Read(current.Id);

        // Assert
        name.Should().Be(current.ProcessName);
    }

    [WindowsFact]
    public async Task Read_WhenProcessExitedButHandleIsStillOpen_ReturnsNull()
    {
        // Arrange: the Process object keeps a handle, so the exited process stays known to the kernel by its PID.
        var startInfo = new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true };
        using var exited = Process.Start(startInfo);
        exited.Should().NotBeNull();
        await exited!.WaitForExitAsync();

        // Act
        var name = WindowsProcessNameReader.Read(exited.Id);

        // Assert
        name.Should().BeNull();
    }

    [WindowsFact]
    public void Read_WhenProcessIdIsUnused_ReturnsNull()
    {
        // Act: Windows process identifiers are multiples of four, so an odd identifier is never assigned.
        var name = WindowsProcessNameReader.Read(int.MaxValue);

        // Assert
        name.Should().BeNull();
    }

    [WindowsFact]
    public void Read_WhenProcessIsSystem_ReturnsSameNameAsProcessClass()
    {
        // Act: the System process (PID 4) runs kernel code and has no image file name.
        var name = WindowsProcessNameReader.Read(4);

        // Assert
        name.Should().Be("System");
    }

    [WindowsFact]
    public void Read_WhenProcessIsIdle_ReturnsNull()
    {
        // Act: the Idle process (PID 0) has no image file name; the inspector names it without this reader.
        var name = WindowsProcessNameReader.Read(0);

        // Assert
        name.Should().BeNull();
    }
}
