using System.IO;
using System.Windows.Input;
using InventorValidator.Excel;
using InventorValidator.Infrastructure;
using InventorValidator.Session;

namespace InventorValidator.UI.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly SettingsManager _settingsManager;
    private readonly InventorVersionDetector _versionDetector;
    private readonly WorkspaceManager _workspaceManager;
    private readonly OrphanSessionCleaner _cleaner;

    private ViewModelBase _currentView;
    private string _selectedNav = "Setup";
    private bool _isBusy;
    private string _stageStatus = "Ready";
    private double _progressValue;
    private bool _isProgressIndeterminate;
    private CancellationTokenSource? _cts;
    private SessionManifest? _activeManifest;
    private Inventor.InventorAutomationSession? _activeInventorSession;

    public SetupViewModel SetupVM { get; }
    public CalculatorViewModel CalculatorVM { get; }
    public ModelInventoryViewModel ModelInventoryVM { get; }
    public ParameterPreviewViewModel ParameterPreviewVM { get; }
    public ChannelResultsViewModel ChannelResultsVM { get; }
    public DiagnosticsViewModel DiagnosticsVM { get; }
    public SettingsViewModel SettingsVM { get; }

    public ViewModelBase CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public string SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (SetProperty(ref _selectedNav, value))
            {
                SwitchNav(value);
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StageStatus
    {
        get => _stageStatus;
        set => SetProperty(ref _stageStatus, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public SessionManifest? ActiveManifest
    {
        get => _activeManifest;
        set => SetProperty(ref _activeManifest, value);
    }

    public ICommand SelectNavCommand { get; }
    public ICommand StartValidationCommand { get; }
    public ICommand CancelValidationCommand { get; }
    public ICommand ShowInInventorCommand { get; }

    public MainViewModel()
    {
        _settingsManager = new SettingsManager();
        _versionDetector = new InventorVersionDetector();
        _workspaceManager = new WorkspaceManager();
        _cleaner = new OrphanSessionCleaner();

        SetupVM = new SetupViewModel(_settingsManager, _versionDetector);
        CalculatorVM = new CalculatorViewModel();
        ModelInventoryVM = new ModelInventoryViewModel();
        ParameterPreviewVM = new ParameterPreviewViewModel();
        ChannelResultsVM = new ChannelResultsViewModel();
        DiagnosticsVM = new DiagnosticsViewModel();
        SettingsVM = new SettingsViewModel(_settingsManager, _cleaner);

        _currentView = SetupVM;

        SelectNavCommand = new RelayCommand(param =>
        {
            if (param is string navName)
            {
                SelectedNav = navName;
            }
        });

        ShowInInventorCommand = new RelayCommand(async () =>
        {
            if (_activeInventorSession != null && _activeInventorSession.IsActive)
            {
                DiagnosticsLogger.Instance.Info("Displaying dedicated Autodesk Inventor window on user request...");
                await _activeInventorSession.SetVisibleAsync(true);
            }
        });
        ModelInventoryVM.ViewInInventorCommand = ShowInInventorCommand;

        SetupVM.InspectCalculatorCommand = new RelayCommand(async () => await ExecuteInspectCalculatorAsync(), () => !IsBusy);

        StartValidationCommand = new RelayCommand(async () => await ExecuteStartValidationAsync(), () => !IsBusy);
        CancelValidationCommand = new RelayCommand(ExecuteCancelValidation, () => IsBusy);

        DiagnosticsLogger.Instance.Info("Application initialized in Phase 3 mode.");
    }

    private void SwitchNav(string navName)
    {
        CurrentView = navName switch
        {
            "Setup" => SetupVM,
            "Calculator" => CalculatorVM,
            "Model Inventory" => ModelInventoryVM,
            "Parameter Preview" => ParameterPreviewVM,
            "Channel Results" => ChannelResultsVM,
            "Diagnostics" => DiagnosticsVM,
            "Settings" => SettingsVM,
            _ => SetupVM
        };
    }

    private async Task ExecuteInspectCalculatorAsync()
    {
        if (string.IsNullOrWhiteSpace(SetupVM.CalculatorPath) || !File.Exists(SetupVM.CalculatorPath))
        {
            StageStatus = "Select a valid calculator (.xls or .xlsx) file first.";
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = true;
        StageStatus = "Preparing isolated calculator inspection copy...";
        _cts = new CancellationTokenSource();

        string tempDir = Path.Combine(Path.GetTempPath(), "InventorValidator", $"Inspect_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, Path.GetFileName(SetupVM.CalculatorPath));
            File.Copy(SetupVM.CalculatorPath, tempFile, overwrite: true);

            var fi = new FileInfo(tempFile);
            if (fi.IsReadOnly)
            {
                fi.IsReadOnly = false;
            }

            var progress = new Progress<string>(msg => StageStatus = msg);
            var excelSession = new ExcelAutomationSession();

            var result = await excelSession.RecalculateAndParseAsync(tempFile, progress: progress, cancellationToken: _cts.Token);
            CalculatorVM.LoadResult(result);
            SelectedNav = "Calculator";

            if (!result.IsErrorCheckPassed)
            {
                StageStatus = $"Calculator inspected with Error_Check warning ({result.WorkbookErrorCheckRaw}).";
                DiagnosticsLogger.Instance.Warn($"Calculator Error_Check warning: {result.WorkbookErrorCheckRaw}");
            }
            else
            {
                StageStatus = $"Calculator inspection complete ({result.TotalSheet1Parameters} parameters, {result.TotalChannels} channels).";
                DiagnosticsLogger.Instance.Success($"Standalone calculator inspection succeeded for: {Path.GetFileName(SetupVM.CalculatorPath)}");
            }
        }
        catch (OperationCanceledException)
        {
            StageStatus = "Calculator inspection cancelled.";
            DiagnosticsLogger.Instance.Warn("Calculator inspection cancelled by user.");
        }
        catch (Exception ex)
        {
            StageStatus = $"Calculator inspection failed: {ex.Message}";
            DiagnosticsLogger.Instance.Error($"Calculator inspection failure: {ex.Message}", ex);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp dir cleanup
            }

            IsBusy = false;
            IsProgressIndeterminate = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task ExecuteStartValidationAsync()
    {
        if (!SetupVM.ValidateInputs())
        {
            SelectedNav = "Setup";
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = true;
        StageStatus = "Initializing session workspace...";
        _cts = new CancellationTokenSource();

        try
        {
            DiagnosticsLogger.Instance.Info($"Starting session: IAM='{SetupVM.IamPath}', Calc='{SetupVM.CalculatorPath}', Mode='{SetupVM.SelectedMode}', Version='{SetupVM.SelectedVersion}', AllowFormulaOverwrite={SetupVM.AllowFormulaOverwrite}");

            var progress = new Progress<string>(msg =>
            {
                StageStatus = msg;
            });

            // Close any existing Inventor automation session from previous runs
            if (_activeInventorSession != null)
            {
                StageStatus = "Closing previous Inventor session...";
                _activeInventorSession.CloseSession();
                _activeInventorSession = null;
            }

            // Step 3: Create disposable workspace
            ActiveManifest = await _workspaceManager.CreateWorkspaceAsync(
                SetupVM.IamPath,
                SetupVM.CalculatorPath,
                progress,
                _cts.Token
            );

            // Step 4 & 5: Recalculate and parse Excel calculator in disposable workspace
            var excelSession = new ExcelAutomationSession();
            var calcResult = await excelSession.RecalculateAndParseAsync(
                ActiveManifest.CopiedCalculatorPath,
                progress: progress,
                cancellationToken: _cts.Token
            );

            CalculatorVM.LoadResult(calcResult);

            if (!calcResult.IsErrorCheckPassed)
            {
                if (SetupVM.SelectedMode.Equals("Apply", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Apply mode blocked: Workbook Error_Check indicates an invalid calculation ({calcResult.WorkbookErrorCheckRaw}). Only 'Inspect Current Model' mode is permitted.");
                }
                else
                {
                    DiagnosticsLogger.Instance.Warn($"Workbook Error_Check = {calcResult.WorkbookErrorCheckRaw}. Proceeding in Inspect mode.");
                }
            }

            // Step 6 & 7: Dedicated Inventor launch & Assembly inventory (Phase 3)
            var inventorSession = new Inventor.InventorAutomationSession();
            _activeInventorSession = inventorSession;

            var invResult = await inventorSession.StartAndInventoryAsync(
                ActiveManifest.CopiedIamPath,
                requestedVersion: SetupVM.SelectedVersion,
                isVisible: SetupVM.ShowInventorWindow,
                progress: progress,
                cancellationToken: _cts.Token
            );

            // Step 8: Parameter matching & Inspect mode discrepancy analysis
            var matchingService = new Inventor.ParameterMatchingService();
            var comparisonResult = matchingService.CompareParameters(calcResult.Sheet1Parameters, invResult);

            ModelInventoryVM.LoadResults(invResult, comparisonResult);
            SelectedNav = "Model Inventory";

            if (SetupVM.SelectedMode.Equals("Apply", StringComparison.OrdinalIgnoreCase))
            {
                StageStatus = $"Phase 3 complete: Model inventory ready ({invResult.TotalOccurrencesCount} components). Parameter application & iLogic rebuild scheduled for Phase 4.";
                DiagnosticsLogger.Instance.Success($"Phase 3 complete (Apply Preview): {invResult.TotalOccurrencesCount} components inventoried. Safe to write: {comparisonResult.SafeToWriteCount}, Protected formulas: {comparisonResult.ProtectedFormulaCount}.");
            }
            else
            {
                StageStatus = $"Phase 3 complete (Inspect Mode): Model inventory verified ({invResult.TotalOccurrencesCount} components, {comparisonResult.MatchedCount} matched, {comparisonResult.DiscrepancyCount} discrepancies).";
                DiagnosticsLogger.Instance.Success($"Phase 3 complete (Inspect Mode): Model inventory verified with {comparisonResult.DiscrepancyCount} discrepancies out of {comparisonResult.TotalRows} parameters.");
            }
        }
        catch (OperationCanceledException)
        {
            StageStatus = "Session cancelled by user.";
            DiagnosticsLogger.Instance.Warn("Session cancelled.");
            if (_activeInventorSession != null)
            {
                _activeInventorSession.CloseSession();
                _activeInventorSession = null;
            }
            ActiveManifest = null;
        }
        catch (Exception ex)
        {
            StageStatus = $"Session error: {ex.Message}";
            DiagnosticsLogger.Instance.Error($"Session failure: {ex.Message}", ex);
            if (_activeInventorSession != null)
            {
                _activeInventorSession.CloseSession();
                _activeInventorSession = null;
            }
            ActiveManifest = null;
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ExecuteCancelValidation()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            StageStatus = "Cancelling session...";
            _cts.Cancel();
        }

        if (_activeInventorSession != null)
        {
            _activeInventorSession.CloseSession();
            _activeInventorSession = null;
        }
    }

    public void OnWindowClosing()
    {
        if (_activeInventorSession != null)
        {
            _activeInventorSession.CloseSession();
            _activeInventorSession = null;
        }

        if (ActiveManifest != null)
        {
            _workspaceManager.CleanupWorkspace(ActiveManifest);
            ActiveManifest = null;
        }
    }
}
