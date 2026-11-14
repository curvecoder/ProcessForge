using AwesomeAssertions;
using ProcessForge.Filtering;
using ProcessForge.Inspectors;
using ProcessForge.Models;
using Xunit;

namespace ProcessForge.Tests;

public sealed class ProcessForgeTests
{
    [Theory]
    [InlineData("java", "java -jar /opt/app/myservice.jar --port 8080", "/opt/app/myservice.jar")]
    [InlineData("cmd", "cmd.exe /c \"C:\\scripts\\run_worker.bat\" arg1", "C:\\scripts\\run_worker.bat")]
    [InlineData("powershell", "powershell.exe -ExecutionPolicy Bypass -File C:\\tools\\audit.ps1", "C:\\tools\\audit.ps1")]
    [InlineData("bash", "/bin/bash /usr/local/bin/backup.sh", "/usr/local/bin/backup.sh")]
    public void ResolveTargetPayload_WhenHostProcessHasTarget_ExtractsTarget(string procName, string cmdLine, string expected)
    {
        // Act
        var target = CrossPlatformProcessInspector.ResolveTargetPayload(procName, cmdLine);

        // Assert
        target.Should().Be(expected);
    }

    [Fact]
    public void ShouldExclude_WhenPidMatches_ReturnsTrue()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions { ExcludedProcessIds = [4] });
        var proc = new ProcessMetadata(4, "System", "", "", "", "SYSTEM");

        // Act & Assert
        filter.ShouldExclude(proc).Should().BeTrue();
    }

    [Fact]
    public void ExcludeExactName_WhenCalled_DynamicallyExcludesProcess()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var proc = new ProcessMetadata(1234, "curl", "/usr/bin/curl", "", "", "user");

        filter.ShouldExclude(proc).Should().BeFalse();

        // Act
        filter.ExcludeExactName("curl");

        // Assert
        filter.ShouldExclude(proc).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExcludeExactName_WhenNameIsNullOrWhiteSpace_ThrowsArgumentException(string? invalidName)
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions());

        // Act
        var act = () => filter.ExcludeExactName(invalidName!);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ShouldExclude_WhenTargetExecutableMatchesConfiguredRule_ReturnsTrue()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions
        {
            ExcludedExactNames = ["language_server.exe"]
        });
        var proc = new ProcessMetadata(1234, "language_server", "C:\\bin\\language_server.exe", "", "language_server.exe", "user");

        // Act & Assert
        filter.ShouldExclude(proc).Should().BeTrue();
    }

    [Fact]
    public void ExcludeExactName_WhenTargetExecutableMatchesDynamicRule_ReturnsTrue()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var proc = new ProcessMetadata(1234, "node", "C:\\bin\\node.exe", "", "C:\\scripts\\worker.js", "user");

        filter.ShouldExclude(proc).Should().BeFalse();

        // Act
        filter.ExcludeExactName("worker.js");

        // Assert
        filter.ShouldExclude(proc).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenCancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var inspector = new CrossPlatformProcessInspector();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => inspector.ResolveProcessAsync(1, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenProcessIdIsZero_ReturnsSystemIdle()
    {
        // Arrange
        var inspector = new CrossPlatformProcessInspector();

        // Act
        var result = await inspector.ResolveProcessAsync(0);

        // Assert
        result.ProcessId.Should().Be(0);
        result.ProcessName.Should().Be("System Idle");
        result.UserName.Should().Be("SYSTEM");
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenProcessIdIsNegative_ReturnsUnknown()
    {
        // Arrange
        var inspector = new CrossPlatformProcessInspector();

        // Act
        var result = await inspector.ResolveProcessAsync(-10);

        // Assert
        result.ProcessId.Should().Be(-10);
        result.ProcessName.Should().Be("Unknown");
        result.UserName.Should().Be("SYSTEM");
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenProcessIsCurrentProcess_ReturnsValidMetadata()
    {
        // Arrange
        var inspector = new CrossPlatformProcessInspector();
        var currentPid = Environment.ProcessId;

        // Act
        var result = await inspector.ResolveProcessAsync(currentPid);

        // Assert
        result.ProcessId.Should().Be(currentPid);
        result.ProcessName.Should().NotBeNullOrWhiteSpace();
        result.ExecutablePath.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenProcessIsCurrentProcess_ReturnsCommandLineContainingExecutableName()
    {
        // Arrange
        var inspector = new CrossPlatformProcessInspector();
        var executableName = Path.GetFileNameWithoutExtension(Environment.ProcessPath);

        // Act
        var result = await inspector.ResolveProcessAsync(Environment.ProcessId);

        // Assert
        executableName.Should().NotBeNullOrEmpty();
        result.CommandLine.Should().ContainEquivalentOf(executableName);
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenShellRunsScript_ReturnsScriptAsTargetExecutable()
    {
        // Arrange
        var inspector = new CrossPlatformProcessInspector();
        var scriptPath = Path.Combine(Path.GetTempPath(), $"processforge_{Guid.NewGuid():N}" + (OperatingSystem.IsWindows() ? ".bat" : ".sh"));
        var startInfo = OperatingSystem.IsWindows()
            ? new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
            : new System.Diagnostics.ProcessStartInfo("/bin/sh", scriptPath);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        try
        {
            await File.WriteAllTextAsync(scriptPath, OperatingSystem.IsWindows() ? "@ping -n 30 127.0.0.1 > nul" : "sleep 30");
            using var shell = System.Diagnostics.Process.Start(startInfo);
            shell.Should().NotBeNull();

            try
            {
                // Act
                var result = await inspector.ResolveProcessAsync(shell!.Id);

                // Assert: the file name only, because the script pattern stops at a space and the temporary
                // directory of some accounts contains one (C:\Users\First Last\...).
                result.TargetExecutable.Should().EndWith(Path.GetFileName(scriptPath));
            }
            finally
            {
                shell!.Kill(entireProcessTree: true);
                await shell.WaitForExitAsync();
            }
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }

    [Fact]
    public async Task ResolveProcessAsync_WhenProcessDoesNotExist_ReturnsTerminated()
    {
        // Arrange
        var inspector = new CrossPlatformProcessInspector();
        const int nonExistentPid = 9999999;

        // Act
        var result = await inspector.ResolveProcessAsync(nonExistentPid);

        // Assert
        result.ProcessId.Should().Be(nonExistentPid);
        result.ProcessName.Should().Be("Terminated");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveTargetPayload_WhenCommandLineIsNullOrWhiteSpace_ReturnsEmpty(string? cmdLine)
    {
        // Act
        var target = CrossPlatformProcessInspector.ResolveTargetPayload("java", cmdLine!);

        // Assert
        target.Should().BeEmpty();
    }

    [Fact]
    public void ResolveTargetPayload_WhenJavaHasNoJarTarget_ReturnsEmpty()
    {
        // Act
        var target = CrossPlatformProcessInspector.ResolveTargetPayload("java", "java -version");

        // Assert
        target.Should().BeEmpty();
    }

    [Fact]
    public void ResolveTargetPayload_WhenHostIsPwshWithScript_ExtractsTarget()
    {
        // Act
        var target = CrossPlatformProcessInspector.ResolveTargetPayload("pwsh", "pwsh -File /scripts/run.ps1");

        // Assert
        target.Should().Be("/scripts/run.ps1");
    }

    [Fact]
    public void ResolveTargetPayload_WhenHostIsNotScriptHost_ReturnsEmpty()
    {
        // Act
        var target = CrossPlatformProcessInspector.ResolveTargetPayload("notepad", "notepad.exe C:\\temp\\notes.txt");

        // Assert
        target.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WhenOptionsIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ProcessExclusionFilter(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ShouldExclude_WhenProcessIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions());

        // Act
        var act = () => filter.ShouldExclude(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("NT AUTHORITY\\SYSTEM", true)]
    [InlineData("root", true)]
    [InlineData("SYSTEM", true)]
    [InlineData("alice", false)]
    [InlineData(null, false)]
    public void ShouldExclude_WhenExcludeSystemAccountsConfigured_ReturnsExpectedResult(string? userName, bool expected)
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions { ExcludeSystemAccounts = true });
        var proc = new ProcessMetadata(100, "custom_service", "", "", "", userName ?? string.Empty);

        // Act & Assert
        filter.ShouldExclude(proc).Should().Be(expected);
    }

    [Theory]
    [InlineData("AUTORITE NT\\Système", "S-1-5-18")]
    [InlineData("AUTORITE NT\\SERVICE LOCAL", "S-1-5-19")]
    [InlineData("AUTORITE NT\\SERVICE RÉSEAU", "S-1-5-20")]
    [InlineData("AUTORITE NT\\Système", "s-1-5-18")]
    public void ShouldExclude_WhenLocalizedSystemAccountHasWellKnownSid_ReturnsTrue(string userName, string userSid)
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions { ExcludeSystemAccounts = true });
        var proc = new ProcessMetadata(100, "custom_service", "", "", "", userName) { UserSid = userSid };

        // Act & Assert
        filter.ShouldExclude(proc).Should().BeTrue();
    }

    [Fact]
    public void ShouldExclude_WhenUserSidIsRegularAccount_ReturnsFalse()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions { ExcludeSystemAccounts = true });
        var proc = new ProcessMetadata(100, "custom_service", "", "", "", "HOMELAB\\alice") { UserSid = "S-1-5-21-1004336348-1177238915-682003330-1001" };

        // Act & Assert
        filter.ShouldExclude(proc).Should().BeFalse();
    }

    [Fact]
    public void ShouldExclude_WhenNameMatchesPrefix_ReturnsTrue()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions
        {
            ExcludedNamePrefixes = ["test_"]
        });
        var proc = new ProcessMetadata(100, "test_worker", "", "", "", "user");

        // Act & Assert
        filter.ShouldExclude(proc).Should().Be(true);
    }

    [Fact]
    public void ShouldExclude_WhenNameMatchesSuffix_ReturnsTrue()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions
        {
            ExcludedNameSuffixes = ["_agent"]
        });
        var proc = new ProcessMetadata(100, "worker_agent", "", "", "", "user");

        // Act & Assert
        filter.ShouldExclude(proc).Should().Be(true);
    }

    [Fact]
    public void ShouldExclude_WhenNameMatchesContains_ReturnsTrue()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions
        {
            ExcludedNameContains = ["daemon"]
        });
        var proc = new ProcessMetadata(100, "my_daemon_process", "", "", "", "user");

        // Act & Assert
        filter.ShouldExclude(proc).Should().Be(true);
    }

    [Fact]
    public void ShouldExclude_WhenExecutablePathMatchesConfiguredExactName_ReturnsTrue()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions
        {
            ExcludedExactNames = ["agent.exe"]
        });
        var proc = new ProcessMetadata(100, "host", "C:\\tools\\agent.exe", "", "", "user");

        // Act & Assert
        filter.ShouldExclude(proc).Should().Be(true);
    }

    [Fact]
    public void ShouldExclude_WhenNoCandidatesExist_ReturnsFalse()
    {
        // Arrange
        var filter = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var proc = new ProcessMetadata(100, "", "", "", "", "user");

        // Act & Assert
        filter.ShouldExclude(proc).Should().Be(false);
    }
}
