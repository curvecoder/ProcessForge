using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProcessForge.Inspectors;

/// <summary>
/// Drive letter of a Windows volume and the NT device it designates, as <c>QueryDosDevice</c> reports them.
/// </summary>
internal readonly record struct DosDeviceMapping(string DriveName, string DevicePath);

/// <summary>
/// Turns an NT device path (<c>\Device\HarddiskVolume3\Windows\System32\svchost.exe</c>) into the Win32 path a user
/// and a firewall rule expect (<c>C:\Windows\System32\svchost.exe</c>).
/// </summary>
internal static partial class NtDevicePathTranslator
{
    private const string DevicePrefix = @"\Device\";
    private static readonly string[] NetworkRedirectorPrefixes = [@"\Device\Mup\", @"\Device\LanmanRedirector\"];

    // A volume mounted in a folder, or not mounted at all, has no drive letter. The GLOBALROOT form still names the
    // file in a way Win32 functions accept, which an empty path or the bare NT path would not.
    private const string GlobalRootPrefix = @"\\?\GLOBALROOT";

    private const int DriveLetterCount = 26;
    private const int DeviceTargetCapacity = 1024;

    /// <summary>
    /// Returns the Win32 form of <paramref name="ntPath"/>; an empty string when the text is not an NT device path
    /// (the pseudo-processes Registry and Memory Compression report a bare name).
    /// </summary>
    public static string Translate(string ntPath, IReadOnlyList<DosDeviceMapping> mappings)
    {
        if (!ntPath.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        foreach (var mapping in mappings)
        {
            if (IsOnDevice(ntPath, mapping.DevicePath))
            {
                return mapping.DriveName + ntPath[mapping.DevicePath.Length..];
            }
        }

        foreach (var redirectorPrefix in NetworkRedirectorPrefixes)
        {
            if (ntPath.StartsWith(redirectorPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + SkipLogonSessionSegments(ntPath[redirectorPrefix.Length..]);
            }
        }

        return GlobalRootPrefix + ntPath;
    }

    // A file reached through a mapped drive keeps, before the server name, the segments that tie the drive letter to
    // the logon session that mapped it (;LanmanRedirector\;Z:000000000001a2b3\). That letter is unknown to the other
    // sessions, the caller's included, so the UNC path from the server name on is the only portable form.
    private static string SkipLogonSessionSegments(string redirectedPath)
    {
        var remainder = redirectedPath;
        while (remainder.StartsWith(';') && remainder.IndexOf('\\') is var separator and >= 0)
        {
            remainder = remainder[(separator + 1)..];
        }

        return remainder;
    }

    /// <summary>
    /// Reads the NT device behind each drive letter of the system.
    /// </summary>
    /// <remarks>
    /// The mappings are read on every call: a drive letter can be assigned or removed while a long audit runs, and
    /// the inspection of a process is cached by the caller, so the 26 queries at most are paid once per new process.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<DosDeviceMapping> ReadDriveMappings()
    {
        var drives = GetLogicalDrives();
        var mappings = new List<DosDeviceMapping>();
        var target = new char[DeviceTargetCapacity];
        for (var index = 0; index < DriveLetterCount; index++)
        {
            if ((drives & (1u << index)) == 0)
            {
                continue;
            }

            var driveName = $"{(char)('A' + index)}:";
            var length = QueryDosDevice(driveName, target, DeviceTargetCapacity);
            if (length == 0)
            {
                continue;
            }

            // The answer is a list of NUL-terminated strings; the first one is the device currently in use.
            var answer = target.AsSpan(0, (int)length);
            var end = answer.IndexOf('\0');
            mappings.Add(new DosDeviceMapping(driveName, new string(end >= 0 ? answer[..end] : answer)));
        }

        return mappings;
    }

    // HarddiskVolume3 must not claim a path on HarddiskVolume31: the device name has to end where a separator starts.
    private static bool IsOnDevice(string ntPath, string devicePath) =>
        devicePath.Length > 0
        && ntPath.Length > devicePath.Length
        && ntPath[devicePath.Length] == '\\'
        && ntPath.StartsWith(devicePath, StringComparison.OrdinalIgnoreCase);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetLogicalDrives();

    [LibraryImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint QueryDosDevice(string deviceName, [Out] char[] targetPath, uint maxLength);
}
