using System.Windows;
using InventorValidator.Infrastructure;
using InventorValidator.Session;
using InventorValidator.UI.Themes;

namespace InventorValidator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global unhandled exception logging
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                DiagnosticsLogger.Instance.Error($"Unhandled Domain Exception: {ex.Message}", ex);
            }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            DiagnosticsLogger.Instance.Error($"Unhandled UI Exception: {args.Exception.Message}", args.Exception);
            args.Handled = true; // Prevent abrupt crash for non-fatal UI errors
            MessageBox.Show($"An unexpected error occurred:\n{args.Exception.Message}\n\nCheck the Diagnostics tab for full details.",
                "Inventor & Excel Model Verification", MessageBoxButton.OK, MessageBoxImage.Warning);
        };

        // Load settings and apply configured theme
        var settingsMgr = new SettingsManager();
        ThemeManager.ApplyTheme(settingsMgr.CurrentSettings.Theme);

        // Silently clean up stale temporary workspaces (> 24 hours old) in background
        Task.Run(() =>
        {
            try
            {
                var cleaner = new OrphanSessionCleaner();
                cleaner.PurgeStaleSessions(TimeSpan.FromHours(24));
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Background orphan workspace cleanup exception: {ex.Message}");
            }
        });
    }
}
