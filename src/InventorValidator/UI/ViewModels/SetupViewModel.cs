using System.IO;
using System.Windows.Input;
using InventorValidator.Infrastructure;
using Microsoft.Win32;

namespace InventorValidator.UI.ViewModels;

public class SetupViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly InventorVersionDetector _versionDetector;

    private string _iamPath = string.Empty;
    private string _calculatorPath = string.Empty;
    private string _selectedMode = "Inspect";
    private string _selectedVersion = "Automatic";
    private bool _allowFormulaOverwrite;
    private bool _showInventorWindow;
    private string _preflightStatus = "Ready to configure session.";
    private bool _hasPreflightWarning;

    public string IamPath
    {
        get => _iamPath;
        set
        {
            if (SetProperty(ref _iamPath, value))
            {
                ValidateInputs();
            }
        }
    }

    public string CalculatorPath
    {
        get => _calculatorPath;
        set
        {
            if (SetProperty(ref _calculatorPath, value))
            {
                ValidateInputs();
            }
        }
    }

    public string SelectedMode
    {
        get => _selectedMode;
        set => SetProperty(ref _selectedMode, value);
    }

    public string SelectedVersion
    {
        get => _selectedVersion;
        set => SetProperty(ref _selectedVersion, value);
    }

    public bool AllowFormulaOverwrite
    {
        get => _allowFormulaOverwrite;
        set => SetProperty(ref _allowFormulaOverwrite, value);
    }

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

    public string PreflightStatus
    {
        get => _preflightStatus;
        set => SetProperty(ref _preflightStatus, value);
    }

    public bool HasPreflightWarning
    {
        get => _hasPreflightWarning;
        set => SetProperty(ref _hasPreflightWarning, value);
    }

    // Installed version info
    public string Inventor2020Status => _versionDetector.Info2020.IsInstalled
        ? $"Installed ({_versionDetector.Info2020.BuildNumber ?? "Ready"})"
        : "Not Detected";

    public bool IsInventor2020Installed => _versionDetector.Info2020.IsInstalled;

    public string Inventor2024Status => _versionDetector.Info2024.IsInstalled
        ? $"Installed ({_versionDetector.Info2024.BuildNumber ?? "Ready"})"
        : "Not Detected";

    public bool IsInventor2024Installed => _versionDetector.Info2024.IsInstalled;

    public ICommand BrowseIamCommand { get; }
    public ICommand BrowseCalculatorCommand { get; }
    public ICommand? InspectCalculatorCommand { get; set; }

    public SetupViewModel(SettingsManager settingsManager, InventorVersionDetector versionDetector)
    {
        _settingsManager = settingsManager;
        _versionDetector = versionDetector;

        _selectedMode = _settingsManager.CurrentSettings.DefaultValidationMode;
        _selectedVersion = _settingsManager.CurrentSettings.PreferredInventorVersion;
        _allowFormulaOverwrite = _settingsManager.CurrentSettings.AllowFormulaOverwrite;
        _showInventorWindow = _settingsManager.CurrentSettings.ShowInventorWindow;

        BrowseIamCommand = new RelayCommand(ExecuteBrowseIam);
        BrowseCalculatorCommand = new RelayCommand(ExecuteBrowseCalculator);
    }

    private void ExecuteBrowseIam()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Autodesk Inventor Assembly",
            Filter = "Inventor Assembly (*.iam)|*.iam|All Files (*.*)|*.*"
        };

        if (!string.IsNullOrEmpty(_settingsManager.CurrentSettings.LastIamFolder) &&
            Directory.Exists(_settingsManager.CurrentSettings.LastIamFolder))
        {
            dlg.InitialDirectory = _settingsManager.CurrentSettings.LastIamFolder;
        }

        if (dlg.ShowDialog() == true)
        {
            IamPath = dlg.FileName;
            _settingsManager.CurrentSettings.LastIamFolder = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
            _settingsManager.Save();

            // Auto-detect matching calculator in the same folder if calculator is not set
            if (string.IsNullOrEmpty(CalculatorPath))
            {
                var folder = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrEmpty(folder))
                {
                    var calc = Directory.GetFiles(folder, "Calc_*.xls*").FirstOrDefault()
                               ?? Directory.GetFiles(folder, "*.xls*").FirstOrDefault();
                    if (calc != null)
                    {
                        CalculatorPath = calc;
                    }
                }
            }
        }
    }

    private void ExecuteBrowseCalculator()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Engineering Calculator",
            Filter = "Excel Workbooks (*.xls;*.xlsx)|*.xls;*.xlsx|All Files (*.*)|*.*"
        };

        if (!string.IsNullOrEmpty(_settingsManager.CurrentSettings.LastCalculatorFolder) &&
            Directory.Exists(_settingsManager.CurrentSettings.LastCalculatorFolder))
        {
            dlg.InitialDirectory = _settingsManager.CurrentSettings.LastCalculatorFolder;
        }

        if (dlg.ShowDialog() == true)
        {
            CalculatorPath = dlg.FileName;
            _settingsManager.CurrentSettings.LastCalculatorFolder = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
            _settingsManager.Save();
        }
    }

    public bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(IamPath))
        {
            PreflightStatus = "Specify an assembly (.iam) path to begin.";
            HasPreflightWarning = false;
            return false;
        }

        if (!File.Exists(IamPath))
        {
            PreflightStatus = "Selected .iam file does not exist on disk.";
            HasPreflightWarning = true;
            return false;
        }

        if (!IamPath.EndsWith(".iam", StringComparison.OrdinalIgnoreCase))
        {
            PreflightStatus = "Selected file must be an Autodesk Inventor Assembly (.iam).";
            HasPreflightWarning = true;
            return false;
        }

        if (string.IsNullOrWhiteSpace(CalculatorPath))
        {
            PreflightStatus = "Specify an engineering calculator (.xls or .xlsx) path.";
            HasPreflightWarning = false;
            return false;
        }

        if (!File.Exists(CalculatorPath))
        {
            PreflightStatus = "Selected calculator file does not exist on disk.";
            HasPreflightWarning = true;
            return false;
        }

        PreflightStatus = "Configuration verified. Ready to create session.";
        HasPreflightWarning = false;
        return true;
    }
}
