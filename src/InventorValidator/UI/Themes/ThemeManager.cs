using System.Windows;
using Microsoft.Win32;

namespace InventorValidator.UI.Themes;

public static class ThemeManager
{
    public static void ApplyTheme(string themeName)
    {
        var isDark = themeName switch
        {
            "Light" => false,
            "Dark" => true,
            _ => IsWindowsInDarkMode()
        };

        var uri = isDark
            ? new Uri("pack://application:,,,/UI/Themes/DarkTheme.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/UI/Themes/LightTheme.xaml", UriKind.Absolute);

        try
        {
            var dict = new ResourceDictionary { Source = uri };
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }
        catch
        {
            // Fallback or design mode
        }
    }

    public static bool IsWindowsInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val is int intVal)
            {
                return intVal == 0;
            }
        }
        catch
        {
            // Fallback default to dark
        }
        return true;
    }
}
