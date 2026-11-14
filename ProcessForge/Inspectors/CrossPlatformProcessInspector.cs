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

    // The command line is split into tokens and the extension is tested at the end of each one, so that foo.shared is
    // not a .sh script. A regular expression searching a script anywhere backtracks quadratically on a long argument
    // (inline code, encoded payload) and its timeout exception would stop the caller's audit.
    private static readonly char[] TokenSeparators = [' ', '\t', '\r', '\n', '"', '\''];

    private static readonly string[] ShellScriptExtensions = [".bat", ".cmd", ".ps1", ".sh", ".py"];
    private static readonly string[] PythonScriptExtensions = [".py"];
    private static readonly string[] NodeScriptExtensions = [".js", ".mjs", ".cjs"];
    private static readonly string[] DotnetAssemblyExtensions = [".dll"];

    private static readonly HashSet<string> ShellHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "pwsh", "bash", "sh",
    };

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

        var extensions = TargetExtensionsOf(processName);
        if (extensions.Length == 0)
        {
            return string.Empty;
        }

        foreach (var token in commandLine.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Array.Exists(extensions, extension => token.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            {
                return token;
            }
        }

        return string.Empty;
    }

    private static string[] TargetExtensionsOf(string processName)
    {
        if (ShellHosts.Contains(processName))
        {
            return ShellScriptExtensions;
        }

        // Linux names the interpreter after its versioned binary (python3, python3.12); Windows uses python, pythonw
        // or the py launcher.
        if (processName.Equals("py", StringComparison.OrdinalIgnoreCase) ||
            processName.StartsWith("python", StringComparison.OrdinalIgnoreCase))
        {
            return PythonScriptExtensions;
        }

        if (processName.Equals("node", StringComparison.OrdinalIgnoreCase))
        {
            return NodeScriptExtensions;
        }

        return processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? DotnetAssemblyExtensions : [];
    }
}
