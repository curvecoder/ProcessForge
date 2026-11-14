using System.Diagnostics;
using System.Text.RegularExpressions;
using ProcessForge.Contracts;
using ProcessForge.Models;

namespace ProcessForge.Inspectors;

/// <summary>
/// Cross-platform inspector that reads process metadata and resolves the actual targets (Java, scripts, and so on).
/// </summary>
public sealed class CrossPlatformProcessInspector : IProcessInspector
{
    private static readonly Regex JarPattern = new(@"\s+(-jar\s+|(?<=\.jar\s+)|^)(?<target>[^\s""']+\.jar)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(500));
    private static readonly Regex ScriptPattern = new(@"(?<target>[^\s""']+\.(?:bat|cmd|ps1|sh|py))", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(500));

    /// <inheritdoc />
    public Task<ProcessMetadata> ResolveProcessAsync(int processId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (processId <= 0)
        {
            return Task.FromResult(new ProcessMetadata(
                ProcessId: processId,
                ProcessName: processId == 0 ? "System Idle" : "Unknown",
                ExecutablePath: string.Empty,
                CommandLine: string.Empty,
                TargetExecutable: string.Empty,
                UserName: "SYSTEM"));
        }

        try
        {
            using var proc = Process.GetProcessById(processId);
            return Task.FromResult(Describe(proc));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(Terminated(processId));
        }
    }

    /// <summary>
    /// Reads the metadata of a process object obtained beforehand.
    /// </summary>
    internal static ProcessMetadata Describe(Process proc)
    {
        var processId = proc.Id;
        string processName;
        try
        {
            processName = proc.ProcessName;
        }
        catch (InvalidOperationException)
        {
            // The process ended between GetProcessById and this read: it is reported as terminated, like a PID that
            // no longer exists, instead of stopping the caller's whole audit.
            return Terminated(processId);
        }

        var executablePath = TryGetExecutablePath(proc);
        var commandLine = TryGetCommandLine(proc);
        var targetPayload = ResolveTargetPayload(processName, commandLine);
        var owner = ProcessOwnerResolver.Resolve(processId);

        return new ProcessMetadata(
            ProcessId: processId,
            ProcessName: processName,
            ExecutablePath: executablePath,
            CommandLine: commandLine,
            TargetExecutable: targetPayload,
            UserName: owner.UserName)
        {
            UserSid = owner.UserSid,
        };
    }

    private static ProcessMetadata Terminated(int processId) => new(
        ProcessId: processId,
        ProcessName: "Terminated",
        ExecutablePath: string.Empty,
        CommandLine: string.Empty,
        TargetExecutable: string.Empty,
        UserName: string.Empty);

    private static string TryGetExecutablePath(Process proc)
    {
        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                var module = proc.MainModule;
                return module?.FileName ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Insufficient rights or process already exited
        }

        return string.Empty;
    }

    private static string TryGetCommandLine(Process proc)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsCommandLineReader.Read(proc.Id);
        }

        if (OperatingSystem.IsLinux())
        {
            var cmdlinePath = $"/proc/{proc.Id}/cmdline";
            if (File.Exists(cmdlinePath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(cmdlinePath);
                    return System.Text.Encoding.UTF8.GetString(bytes).Replace('\0', ' ').Trim();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The process may end before the read, or /proc may hide it (hidepid, restricted container): the
                    // command line stays empty and the caller's audit goes on.
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Resolves the actual application target from the script host or virtual machine name and the command line.
    /// </summary>
    public static string ResolveTargetPayload(string processName, string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return string.Empty;
        }

        var isJava = processName.Contains("java", StringComparison.OrdinalIgnoreCase);
        if (isJava)
        {
            var match = JarPattern.Match(commandLine);
            if (match.Success)
            {
                return match.Groups["target"].Value;
            }
        }

        var isScriptHost = processName.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("bash", StringComparison.OrdinalIgnoreCase) ||
                           processName.Equals("sh", StringComparison.OrdinalIgnoreCase);

        if (isScriptHost)
        {
            var match = ScriptPattern.Match(commandLine);
            if (match.Success)
            {
                return match.Groups["target"].Value;
            }
        }

        return string.Empty;
    }
}
