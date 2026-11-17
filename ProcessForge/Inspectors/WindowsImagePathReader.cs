using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProcessForge.Inspectors;

/// <summary>
/// Reads the image file path of one Windows process through <c>NtQuerySystemInformation(SystemProcessIdInformation)</c>.
/// </summary>
/// <remarks>
/// The query needs no handle on the process. <see cref="System.Diagnostics.Process.MainModule"/> opens the process to
/// read its memory, which its DACL refuses to any other account than its own and SYSTEM (a service running under a
/// dedicated account), and which protected processes refuse to everyone: the path was then left empty, while the
/// name, read through this query, was known.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static partial class WindowsImagePathReader
{
    private const int SystemProcessIdInformationClass = 88;
    private const int StatusSuccess = 0;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const ushort InitialBufferBytes = 520;

    /// <summary>
    /// Returns the Win32 path of the image file of the process; <see langword="null"/> when the PID is unused or the
    /// process has no image file (Idle, System, Registry, Memory Compression).
    /// </summary>
    public static string? Read(int processId)
    {
        var ntPath = ReadNtPath(processId);
        if (string.IsNullOrEmpty(ntPath))
        {
            return null;
        }

        var path = NtDevicePathTranslator.Translate(ntPath, NtDevicePathTranslator.ReadDriveMappings());
        return path.Length == 0 ? null : path;
    }

    /// <summary>
    /// Returns the image file path of the process as the kernel stores it (<c>\Device\HarddiskVolume3\…</c>);
    /// <see langword="null"/> when the PID is unused.
    /// </summary>
    public static string? ReadNtPath(int processId)
    {
        var byteCapacity = InitialBufferBytes;

        // The second pass only happens when the NT path exceeds the first buffer; the kernel then reports the size.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(byteCapacity);
            try
            {
                var information = new SystemProcessIdInformation
                {
                    ProcessId = processId,
                    MaximumLength = byteCapacity,
                    Buffer = buffer,
                };
                var status = NtQuerySystemInformation(SystemProcessIdInformationClass, ref information, Marshal.SizeOf<SystemProcessIdInformation>(), out _);
                if (status == StatusSuccess)
                {
                    return Marshal.PtrToStringUni(buffer, information.Length / sizeof(char));
                }

                if (status != StatusInfoLengthMismatch || information.MaximumLength <= byteCapacity)
                {
                    return null;
                }

                byteCapacity = information.MaximumLength;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemProcessIdInformation
    {
        public IntPtr ProcessId;
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQuerySystemInformation(int systemInformationClass, ref SystemProcessIdInformation systemInformation, int systemInformationLength, out int returnLength);
}
