using System.Diagnostics;
using AwesomeAssertions;
using ProcessForge.Inspectors;
using Xunit;

namespace ProcessForge.Tests;

public sealed class ProcessStartTimeReaderTests
{
    [Fact]
    public void Read_WhenProcessIsRunning_ReturnsSameNonNullValueTwice()
    {
        // Arrange
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return;
        }

        using var current = Process.GetCurrentProcess();
        var sut = new ProcessStartTimeReader();

        // Act
        var first = sut.Read(current.Id);
        var second = sut.Read(current.Id);

        // Assert
        first.Should().NotBeNull();
        second.Should().Be(first);
    }

    [Fact]
    public void Read_WhenProcessDoesNotExist_ReturnsNull()
    {
        // Act
        var startTime = new ProcessStartTimeReader().Read(int.MaxValue);

        // Assert
        startTime.Should().BeNull();
    }

    [Fact]
    public void ParseLinuxStartTicks_WhenCommandNameContainsParentheses_ReturnsField22()
    {
        // Arrange: fields 3 to 21 follow the command name; field 22 is the start time.
        const string stat = "1234 ((my) (app)) S 1 1234 1234 0 -1 4194560 100 0 0 0 5 3 0 0 20 0 1 0 987654 12345678 300";

        // Act
        var ticks = ProcessStartTimeReader.ParseLinuxStartTicks(stat);

        // Assert
        ticks.Should().Be(987654);
    }

    [Fact]
    public void ParseLinuxStartTicks_WhenContentIsTruncated_ReturnsNull()
    {
        // Act
        var ticks = ProcessStartTimeReader.ParseLinuxStartTicks("1234 (app) S 1 1234");

        // Assert
        ticks.Should().BeNull();
    }
}
