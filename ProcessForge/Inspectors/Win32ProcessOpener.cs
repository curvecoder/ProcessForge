using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ProcessForge.Inspectors;

/// <summary>
/// Opens Windows processes with the narrowest access right that still lets the inspectors read their token and
/// command line.
/// </summary>
internal static partial class Win32ProcessOpener
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>
    /// Opens the process with <c>PROCESS_QUERY_LIMITED_INFORMATION</c>; the handle is invalid when the process has
    /// exited, is protected, or the caller lacks the right.
    /// </summary>
    public static SafeProcessHandle OpenForLimitedQuery(int processId) =>
        OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);
}
