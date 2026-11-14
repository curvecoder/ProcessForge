namespace ProcessForge.Models;

/// <summary>
/// Detailed metadata of an executable process resolved by the system.
/// </summary>
/// <param name="ProcessId">Unique process identifier (PID).</param>
/// <param name="ProcessName">Short process name.</param>
/// <param name="ExecutablePath">Full absolute path of the executable binary.</param>
/// <param name="CommandLine">Full launch command line.</param>
/// <param name="TargetExecutable">Actual target detected (for example a JAR file, a .bat, .cmd, .ps1, .sh, .py or .js script, or a .dll run by dotnet).</param>
/// <param name="UserName">Account the process runs under.</param>
public sealed record ProcessMetadata(
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    string CommandLine,
    string TargetExecutable,
    string UserName)
{
    /// <summary>
    /// Security identifier of the account the process runs under (for example <c>S-1-5-18</c> for LocalSystem) on Windows.
    /// Unlike <see cref="UserName"/>, it does not depend on the display language of the system.
    /// Empty on Linux and when the access token of the process cannot be read.
    /// </summary>
    public string UserSid { get; init; } = string.Empty;
}
