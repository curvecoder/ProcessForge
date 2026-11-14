using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace ProcessForge.Inspectors;

/// <summary>
/// Reads the command line of a Windows process through <c>NtQueryInformationProcess(ProcessCommandLineInformation)</c>.
/// </summary>
/// <remarks>
/// This information class (Windows 8.1 and later) only needs <c>PROCESS_QUERY_LIMITED_INFORMATION</c> and copies the
/// string out of the kernel, so it works across 32/64-bit boundaries without reading the memory of the target process,
/// and costs far less than a WMI query per process.
/// </remarks>
internal static partial class WindowsCommandLineReader
{
    private const int ProcessCommandLineInformation = 60;
    private const int StatusSuccess = 0;

    /// <summary>
    /// Returns the command line of the process, or an empty string when the process cannot be opened
    /// (exited, protected, insufficient rights).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string Read(int processId)
    {
        using var process = Win32ProcessOpener.OpenForLimitedQuery(processId);
        if (process.IsInvalid)
        {
            return string.Empty;
        }

        // The first call only reports the size of the UNICODE_STRING header plus its characters.
        _ = NtQueryInformationProcess(process, ProcessCommandLineInformation, IntPtr.Zero, 0, out var length);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, length, out _) != StatusSuccess)
            {
                return string.Empty;
            }

            // UNICODE_STRING: USHORT Length (in bytes), USHORT MaximumLength, then the PWSTR Buffer aligned on a
            // pointer boundary, which points inside the same allocation.
            var byteLength = (ushort)Marshal.ReadInt16(buffer);
            var characters = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            return characters == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(characters, byteLength / sizeof(char));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(SafeProcessHandle processHandle, int processInformationClass, IntPtr processInformation, int processInformationLength, out int returnLength);
}
