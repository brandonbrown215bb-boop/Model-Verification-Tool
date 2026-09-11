using InventorValidator.Infrastructure;
using InventorValidator.Inventor;
using Xunit;

namespace InventorValidator.Tests;

public class InventorProcessLauncherTests
{
    [Fact]
    public void VersionDetector_InitializesWithoutCrashing()
    {
        var detector = new InventorVersionDetector();

        Assert.NotNull(detector.Info2020);
        Assert.NotNull(detector.Info2024);
        Assert.Equal(2020, detector.Info2020.MajorVersion);
        Assert.Equal(2024, detector.Info2024.MajorVersion);
        Assert.False(string.IsNullOrEmpty(detector.DefaultProgIdTarget));
    }

    [Fact]
    public void ResolveTargetVersion_RespectsUserPreferences()
    {
        var detector = new InventorVersionDetector();
        var launcher = new InventorProcessLauncher(detector);

        if (detector.Info2020.IsInstalled)
        {
            int ver = launcher.ResolveTargetVersion("Inventor 2020");
            Assert.Equal(2020, ver);
        }

        if (detector.Info2024.IsInstalled)
        {
            int ver = launcher.ResolveTargetVersion("Inventor 2024");
            Assert.Equal(2024, ver);
        }

        if (detector.Info2020.IsInstalled)
        {
            // Automatic policy defaults to 2020 if installed
            int autoVer = launcher.ResolveTargetVersion("Automatic");
            Assert.Equal(2020, autoVer);
        }
        else if (detector.Info2024.IsInstalled)
        {
            int autoVer = launcher.ResolveTargetVersion("Automatic");
            Assert.Equal(2024, autoVer);
        }
    }
}
