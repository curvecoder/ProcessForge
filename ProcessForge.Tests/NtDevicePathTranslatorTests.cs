using AwesomeAssertions;
using ProcessForge.Inspectors;
using Xunit;

namespace ProcessForge.Tests;

public sealed class NtDevicePathTranslatorTests
{
    private static readonly DosDeviceMapping[] Mappings =
    [
        new("C:", @"\Device\HarddiskVolume3"),
        new("D:", @"\Device\HarddiskVolume5"),
    ];

    [Fact]
    public void Translate_WhenDeviceHasDriveLetter_ReturnsDrivePath()
    {
        // Act
        var path = NtDevicePathTranslator.Translate(@"\Device\HarddiskVolume5\Apps\Supervision\agent.exe", Mappings);

        // Assert
        path.Should().Be(@"D:\Apps\Supervision\agent.exe");
    }

    [Fact]
    public void Translate_WhenDeviceNameDiffersInCase_ReturnsDrivePath()
    {
        // Act
        var path = NtDevicePathTranslator.Translate(@"\device\harddiskvolume3\Windows\System32\svchost.exe", Mappings);

        // Assert
        path.Should().Be(@"C:\Windows\System32\svchost.exe");
    }

    [Fact]
    public void Translate_WhenDeviceNameOnlyStartsLikeMappedDevice_ReturnsGlobalRootPath()
    {
        // Act: HarddiskVolume31 is another volume than HarddiskVolume3.
        var path = NtDevicePathTranslator.Translate(@"\Device\HarddiskVolume31\Apps\agent.exe", Mappings);

        // Assert
        path.Should().Be(@"\\?\GLOBALROOT\Device\HarddiskVolume31\Apps\agent.exe");
    }

    [Fact]
    public void Translate_WhenFileIsOnNetworkRedirector_ReturnsUncPath()
    {
        // Act
        var path = NtDevicePathTranslator.Translate(@"\Device\Mup\nas.home.arpa\apps\agent.exe", Mappings);

        // Assert
        path.Should().Be(@"\\nas.home.arpa\apps\agent.exe");
    }

    [Theory]
    [InlineData(@"\Device\Mup\;LanmanRedirector\;Z:000000000001a2b3\nas.home.arpa\apps\agent.exe")]
    [InlineData(@"\Device\LanmanRedirector\;Z:000000000001a2b3\nas.home.arpa\apps\agent.exe")]
    public void Translate_WhenFileIsOnDriveMappedInAnotherLogonSession_ReturnsUncPath(string ntPath)
    {
        // Act: the drive letter belongs to the logon session of the process, not to the caller's.
        var path = NtDevicePathTranslator.Translate(ntPath, Mappings);

        // Assert
        path.Should().Be(@"\\nas.home.arpa\apps\agent.exe");
    }

    [Fact]
    public void Translate_WhenTextIsBareName_ReturnsEmpty()
    {
        // Act: the Registry and Memory Compression pseudo-processes report a name, not a file.
        var path = NtDevicePathTranslator.Translate("Registry", Mappings);

        // Assert
        path.Should().BeEmpty();
    }

    [Fact]
    public void Translate_WhenVolumeHasNoDriveLetter_ReturnsGlobalRootPath()
    {
        // Act
        var path = NtDevicePathTranslator.Translate(@"\Device\HarddiskVolume7\Apps\agent.exe", Mappings);

        // Assert
        path.Should().Be(@"\\?\GLOBALROOT\Device\HarddiskVolume7\Apps\agent.exe");
    }

    [Fact]
    public void Translate_WhenPathIsEmpty_ReturnsEmpty()
    {
        // Act
        var path = NtDevicePathTranslator.Translate(string.Empty, Mappings);

        // Assert
        path.Should().BeEmpty();
    }
}
