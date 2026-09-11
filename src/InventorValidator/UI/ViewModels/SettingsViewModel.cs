using System.Windows.Input;
using InventorValidator.Infrastructure;
using InventorValidator.Session;
using InventorValidator.UI.Themes;

namespace InventorValidator.UI.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly OrphanSessionCleaner _cleaner;

    private string _selectedTheme;
    private double _matchTolerance;
    private double _warningTolerance;
    private double _diameterTolerance;
    private bool _showExtraHoles;
    private string _cleanupStatus = "Temporary workspace directory: C:\\Temp\\InventorValidator";

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _settingsManager.CurrentSettings.Theme = value;
                _settingsManager.Save();
                ThemeManager.ApplyTheme(value);
            }
        }
    }

    public double MatchTolerance
    {
        get => _matchTolerance;
        set
        {
            if (SetProperty(ref _matchTolerance, value))
            {
                _settingsManager.CurrentSettings.MatchTolerance = value;
                _settingsManager.Save();
            }
        }
    }

    public double WarningTolerance
    {
        get => _warningTolerance;
        set
        {
            if (SetProperty(ref _warningTolerance, value))
            {
                _settingsManager.CurrentSettings.WarningTolerance = value;
                _settingsManager.Save();
            }
        }
    }

    public double DiameterTolerance
    {
        get => _diameterTolerance;
        set
        {
            if (SetProperty(ref _diameterTolerance, value))
            {
                _settingsManager.CurrentSettings.DiameterTolerance = value;
                _settingsManager.Save();
            }
        }
    }

    public bool ShowExtraHoles
    {
        get => _showExtraHoles;
        set
        {
            if (SetProperty(ref _showExtraHoles, value))
            {
                _settingsManager.CurrentSettings.ShowExtraHoles = value;
                _settingsManager.Save();
            }
        }
    }

    private bool _showInventorWindow;

    public bool ShowInventorWindow
    {
        get => _showInventorWindow;
        set
        {
            if (SetProperty(ref _showInventorWindow, value))
            {
                _settingsManager.CurrentSettings.ShowInventorWindow = value;
                _settingsManager.Save();
            }
        }
    }

    public string CleanupStatus
    {
        get => _cleanupStatus;
        set => SetProperty(ref _cleanupStatus, value);
    }

    public ICommand CleanTempWorkspacesCommand { get; }

    public SettingsViewModel(SettingsManager settingsManager, OrphanSessionCleaner cleaner)
    {
        _settingsManager = settingsManager;
        _cleaner = cleaner;

        _selectedTheme = _settingsManager.CurrentSettings.Theme;
        _matchTolerance = _settingsManager.CurrentSettings.MatchTolerance;
        _warningTolerance = _settingsManager.CurrentSettings.WarningTolerance;
        _diameterTolerance = _settingsManager.CurrentSettings.DiameterTolerance;
        _showExtraHoles = _settingsManager.CurrentSettings.ShowExtraHoles;
        _showInventorWindow = _settingsManager.CurrentSettings.ShowInventorWindow;

        CleanTempWorkspacesCommand = new RelayCommand(ExecuteCleanTempWorkspaces);
    }

    private void ExecuteCleanTempWorkspaces()
    {
        var result = _cleaner.PurgeStaleSessions(purgeAll: true);
        CleanupStatus = $"Cleaned {result.CleanedCount} workspaces ({result.FreedBytes / (1024 * 1024):N1} MB freed). Failed: {result.FailedCount}.";
    }
}
