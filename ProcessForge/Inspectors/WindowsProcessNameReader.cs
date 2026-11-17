using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace ProcessForge.Inspectors;

/// <summary>
/// Reads the short name of one Windows process through <c>NtQuerySystemInformation(SystemProcessIdInformation)</c>.
/// </summary>
/// <remarks>
/// <see cref="System.Diagnostics.Process.ProcessName"/> takes a snapshot of every process and thread of the system to
/// find a single entry: about 2 ms per call on a desktop with 350 processes, which made it most of the inspection
/// cost. <see cref="WindowsImagePathReader"/> targets one PID, needs no handle on the process, and so also names
/// processes the caller cannot open.
/// </remarks>
internal static partial class WindowsProcessNameReader
{
    private const int StillActive = 259;

    // Windows has given PID 4 to the System process since Windows XP. It owns the kernel-mode sockets (SMB, NetBIOS,
    // HTTP.sys), so a socket scan meets it on every pass, and without an image file it would otherwise fall back to
    // the full process snapshot.
    private const int SystemProcessId = 4;
    private const string SystemProcessName = "System";

    /// <summary>
    /// Returns the name of the running process, without directory nor <c>.exe</c> extension, as
    /// <see cref="System.Diagnostics.Process.ProcessName"/> reports it; <see langword="null"/> when the PID is unused,
    /// the process has exited, or it has no image file (Idle, Secure System).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string? Read(int processId)
    {
        if (processId == SystemProcessId)
        {
            return SystemProcessName;
        }

        var imagePath = WindowsImagePathReader.ReadNtPath(processId);
        if (string.IsNullOrEmpty(imagePath) || HasExited(processId))
        {
            return null;
        }

        var name = imagePath[(imagePath.LastIndexOf('\\') + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^".exe".Length] : name;
    }

    // An exited process stays known by its PID while any handle on it remains open (the caller's own Process object
    // included), and the query above still names it. The process snapshot used by Process.ProcessName leaves it out,
    // so the inspector reports it as terminated; this check keeps that behaviour. A process the caller cannot open is
    // taken as running: the query above found it, and nothing more can be learnt about it.
    private static bool HasExited(int processId)
    {
        using var process = Win32ProcessOpener.OpenForLimitedQuery(processId);
        return !process.IsInvalid && GetExitCodeProcess(process, out var exitCode) && exitCode != StillActive;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeProcessHandle processHandle, out int exitCode);
}
