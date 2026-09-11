using System.IO;
using System.Text.Json;

namespace InventorValidator.Infrastructure;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string LastIamFolder { get; set; } = string.Empty;
    public string LastCalculatorFolder { get; set; } = string.Empty;
    public string PreferredInventorVersion { get; set; } = "Automatic";
    public string DefaultValidationMode { get; set; } = "Inspect";
    public bool AllowFormulaOverwrite { get; set; } = false;
    public double MatchTolerance { get; set; } = 0.010;
    public double WarningTolerance { get; set; } = 0.031;
    public double DiameterTolerance { get; set; } = 0.002;
    public bool ShowExtraHoles { get; set; } = false;
    public bool ShowInventorWindow { get; set; } = false;
}

public class SettingsManager
{
    private static readonly string DefaultSettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModelVerification"
    );

    private readonly string _settingsFilePath;

    public AppSettings CurrentSettings { get; private set; }

    public SettingsManager(string? customSettingsPath = null)
    {
        _settingsFilePath = customSettingsPath ?? Path.Combine(DefaultSettingsDir, "settings.json");
        CurrentSettings = Load();
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    CurrentSettings = loaded;
                    return CurrentSettings;
                }
            }
        }
        catch
        {
            // On failure, fall back to defaults
        }

        CurrentSettings = new AppSettings();
        return CurrentSettings;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(CurrentSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Suppress non-critical persistence errors
        }
    }
}
