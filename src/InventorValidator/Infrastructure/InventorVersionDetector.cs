using System.IO;
using Microsoft.Win32;

namespace InventorValidator.Infrastructure;

public record InventorInstallationInfo(
    int MajorVersion,
    string DisplayName,
    bool IsInstalled,
    string? ExecutablePath,
    string? BuildNumber,
    string? ProgId
);

public class InventorVersionDetector
{
    public InventorInstallationInfo Info2020 { get; private set; }
    public InventorInstallationInfo Info2024 { get; private set; }
    public string DefaultProgIdTarget { get; private set; } = "Unknown";

    public InventorVersionDetector()
    {
        Info2020 = DetectVersion(2020, 24);
        Info2024 = DetectVersion(2024, 28);
        DetectDefaultProgId();
    }

    private static InventorInstallationInfo DetectVersion(int year, int internalVersion)
    {
        var exePath = $@"C:\Program Files\Autodesk\Inventor {year}\Bin\Inventor.exe";
        var isInstalled = File.Exists(exePath);
        string? buildNumber = null;
        string? progId = $"Inventor.Application.{internalVersion}";

        if (isInstalled)
        {
            try
            {
                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                buildNumber = fvi.FileVersion ?? fvi.ProductVersion;
            }
            catch
            {
                // File version retrieval error fallback
            }
        }
        else
        {
            // Try checking registry under HKLM\SOFTWARE\Autodesk\Inventor\RegistryVersion{internalVersion}.0
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Autodesk\Inventor\RegistryVersion{internalVersion}.0");
                if (key != null)
                {
                    var installPath = key.GetValue("InstallDir") as string;
                    if (!string.IsNullOrEmpty(installPath))
                    {
                        var candidateExe = Path.Combine(installPath, "Bin", "Inventor.exe");
                        if (File.Exists(candidateExe))
                        {
                            exePath = candidateExe;
                            isInstalled = true;
                        }
                    }
                }
            }
            catch
            {
                // Registry check fallback
            }
        }

        return new InventorInstallationInfo(
            year,
            $"Inventor {year}",
            isInstalled,
            isInstalled ? exePath : null,
            buildNumber,
            progId
        );
    }

    private void DetectDefaultProgId()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"Inventor.Application\CurVer");
            if (key != null)
            {
                DefaultProgIdTarget = key.GetValue(null) as string ?? "Inventor.Application";
            }
            else
            {
                using var clsidKey = Registry.ClassesRoot.OpenSubKey(@"Inventor.Application\CLSID");
                if (clsidKey != null)
                {
                    DefaultProgIdTarget = "Inventor.Application (Registered)";
                }
            }
        }
        catch
        {
            DefaultProgIdTarget = "Unavailable";
        }
    }
}
