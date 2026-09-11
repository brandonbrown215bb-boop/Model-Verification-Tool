using System.IO;
using InventorValidator.Infrastructure;
using Xunit;

namespace InventorValidator.Tests;

public class SettingsManagerTests
{
    [Fact]
    public void DefaultSettings_ShouldHaveExpectedValues()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var mgr = new SettingsManager(tempFile);
            var settings = mgr.CurrentSettings;

            Assert.Equal("Dark", settings.Theme);
            Assert.Equal("Automatic", settings.PreferredInventorVersion);
            Assert.Equal("Inspect", settings.DefaultValidationMode);
            Assert.False(settings.AllowFormulaOverwrite);
            Assert.Equal(0.010, settings.MatchTolerance);
            Assert.Equal(0.031, settings.WarningTolerance);
            Assert.Equal(0.002, settings.DiameterTolerance);
            Assert.False(settings.ShowExtraHoles);
            Assert.False(settings.ShowInventorWindow);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void SaveAndLoad_ShouldPersistModifications()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            var mgr = new SettingsManager(tempFile);
            mgr.CurrentSettings.Theme = "Light";
            mgr.CurrentSettings.AllowFormulaOverwrite = true;
            mgr.CurrentSettings.MatchTolerance = 0.015;
            mgr.CurrentSettings.LastIamFolder = @"C:\Vault\Sample";
            mgr.CurrentSettings.ShowInventorWindow = true;
            mgr.Save();

            var mgr2 = new SettingsManager(tempFile);
            Assert.Equal("Light", mgr2.CurrentSettings.Theme);
            Assert.True(mgr2.CurrentSettings.AllowFormulaOverwrite);
            Assert.Equal(0.015, mgr2.CurrentSettings.MatchTolerance);
            Assert.Equal(@"C:\Vault\Sample", mgr2.CurrentSettings.LastIamFolder);
            Assert.True(mgr2.CurrentSettings.ShowInventorWindow);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
