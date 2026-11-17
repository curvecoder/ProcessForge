using System.Diagnostics;
using System.Text;
using ProcessForge.Contracts;
using ProcessForge.Models;

namespace ProcessForge.Inspectors;

/// <summary>
/// Cross-platform inspector that reads process metadata and resolves the actual targets (Java, scripts, and so on).
/// </summary>
public sealed class CrossPlatformProcessInspector : IProcessInspector
{
    // The command line is split into tokens and the extension is tested at the end of each one, so that foo.shared is
    // not a .sh script. A regular expression searching a target anywhere backtracks quadratically on a long argument
    // (inline code, encoded payload) or a long run of blanks, and its timeout exception would stop the caller's audit.
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
            processName = ReadProcessName(proc);
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

    // Process.ProcessName stays the source when the direct query has no answer: it names the image-less processes
    // (Idle, Secure System) and throws InvalidOperationException for an exited one.
    private static string ReadProcessName(Process proc) =>
        OperatingSystem.IsWindows() && WindowsProcessNameReader.Read(proc.Id) is { } name ? name : proc.ProcessName;

    private static string TryGetExecutablePath(Process proc)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsImagePathReader.Read(proc.Id) ?? string.Empty;
        }

        try
        {
            if (OperatingSystem.IsLinux())
            {
                var module = proc.MainModule;
                return module?.FileName ?? string.Empty;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The /proc/<pid>/exe link of another user's process is unreadable without root, and the process may have
            // exited since it was found: the metadata is still reported, with an empty path the caller can display.
        }

        return string.Empty;
    }

    /// <summary>
    /// Turns a raw command line (NUL-separated arguments of <c>/proc/&lt;pid&gt;/cmdline</c>, or a Windows command line
    /// whose arguments contain line breaks) into a single line.
    /// </summary>
    /// <remarks>
    /// Consumers write the command line as one field of line-oriented reports (CSV): a line break kept inside an
    /// argument (<c>python -c "a\nb"</c>) would split the record. Each run of control characters or blanks other
    /// than the space becomes one space; ordinary spaces inside arguments are kept.
    /// </remarks>
    internal static string NormalizeCommandLine(string rawCommandLine)
    {
        var builder = new StringBuilder(rawCommandLine.Length);
        var previousWasSeparator = false;
        foreach (var character in rawCommandLine)
        {
            var isSeparator = character != ' ' && (char.IsControl(character) || char.IsWhiteSpace(character));
            if (!isSeparator)
            {
                builder.Append(character);
            }
            else if (!previousWasSeparator)
            {
                builder.Append(' ');
            }

            previousWasSeparator = isSeparator;
        }

        return builder.ToString().Trim();
    }

    private static string TryGetCommandLine(Process proc)
    {
        if (OperatingSystem.IsWindows())
        {
            return NormalizeCommandLine(WindowsCommandLineReader.Read(proc.Id));
        }

        if (OperatingSystem.IsLinux())
        {
            var cmdlinePath = $"/proc/{proc.Id}/cmdline";
            if (File.Exists(cmdlinePath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(cmdlinePath);
                    return NormalizeCommandLine(System.Text.Encoding.UTF8.GetString(bytes));
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

        if (processName.Contains("java", StringComparison.OrdinalIgnoreCase))
        {
            return FindJarTarget(commandLine.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries));
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

    // A JAR is the target only right after -jar or after another JAR: a JAR given to -cp or -classpath is a library,
    // not the application the process runs. A quoted path containing a space is split like any other token, so its
    // last part no longer follows -jar and the target stays empty rather than truncated.
    private static string FindJarTarget(string[] tokens)
    {
        for (var index = 1; index < tokens.Length; index++)
        {
            var previous = tokens[index - 1];
            var followsJarOption = previous.Equals("-jar", StringComparison.OrdinalIgnoreCase) ||
                                   previous.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            if (followsJarOption && tokens[index].EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                return tokens[index];
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
