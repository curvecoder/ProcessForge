namespace ProcessForge.Inspectors;

/// <summary>
/// Reads the start time of a process, which tells a reused PID apart from the process that held it before.
/// </summary>
internal interface IProcessStartTimeReader
{
    /// <summary>
    /// Reads an opaque start time value for the process (a Windows FILETIME or Linux clock ticks since boot).
    /// </summary>
    /// <param name="processId">Process identifier (PID).</param>
    /// <returns>The start time, or <see langword="null"/> when the process has exited or cannot be read.</returns>
    long? Read(int processId);
}
