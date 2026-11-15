using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProcessForge.Inspectors;

/// <summary>
/// Reads process start times from the operating system: the creation FILETIME on Windows, field 22 of
/// <c>/proc/&lt;pid&gt;/stat</c> on Linux, and nothing elsewhere.
/// </summary>
internal sealed partial class ProcessStartTimeReader : IProcessStartTimeReader
{
    // Field 22 of /proc/<pid>/stat is the 20th field after the command name, which ends at the last ')'.
    private const int StartTimeIndexAfterCommandName = 19;

    /// <inheritdoc />
    public long? Read(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return ReadWindows(processId);
        }

        return OperatingSystem.IsLinux() ? ReadLinux(processId) : null;
    }

    /// <summary>
    /// Extracts the start time (field 22, clock ticks since boot) from the content of <c>/proc/&lt;pid&gt;/stat</c>.
    /// The command name (field 2) may itself contain spaces and parentheses, so the fields are counted after the last
    /// closing parenthesis.
    /// </summary>
    /// <returns>The start time in clock ticks, or <see langword="null"/> when the content is malformed.</returns>
    public static long? ParseLinuxStartTicks(string statContent)
    {
        var commandNameEnd = statContent.LastIndexOf(')');
        if (commandNameEnd < 0)
        {
            return null;
        }

        var fields = statContent[(commandNameEnd + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length <= StartTimeIndexAfterCommandName)
        {
            return null;
        }

        return long.TryParse(fields[StartTimeIndexAfterCommandName], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            ? ticks
            : null;
    }

    [SupportedOSPlatform("windows")]
    private static long? ReadWindows(int processId)
    {
        using var handle = Win32ProcessOpener.OpenForLimitedQuery(processId);
        if (handle.IsInvalid)
        {
            return null;
        }

        // A non-zero exit time means the process has exited but is still held by a handle: its PID cannot be reused
        // yet, but its metadata is no longer that of a running process, so it is not worth caching.
        if (!GetProcessTimes(handle, out var creationTime, out var exitTime, out _, out _) || exitTime != 0)
        {
            return null;
        }

        return creationTime;
    }

    private static long? ReadLinux(int processId)
    {
        try
        {
            return ParseLinuxStartTicks(File.ReadAllText($"/proc/{processId}/stat"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The process may have exited or /proc may hide it (hidepid, restricted container): without a start time
            // the caller resolves the process on every call instead of caching it.
            return null;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(
        Microsoft.Win32.SafeHandles.SafeProcessHandle processHandle,
        out long creationTime,
        out long exitTime,
        out long kernelTime,
        out long userTime);
}
