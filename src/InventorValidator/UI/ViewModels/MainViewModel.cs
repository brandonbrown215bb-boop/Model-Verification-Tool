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
        ParameterPreviewVM.ViewInInventorCommand = ShowInInventorCommand;
        ParameterPreviewVM.OnGeometryValidated = geomResult =>
        {
            ChannelResultsVM.LoadResults(geomResult);
            SelectedNav = "Channel Results";
        };
        ParameterPreviewVM.OnApplyCompletedWithoutGeometry = () =>
        {
            ChannelResultsVM.Reset("No channel locations defined in calculator for this model. View applied parameters and model inventory for results.");
            StageStatus = $"Apply Mode complete: {ParameterPreviewVM.ChangedParamsCount} parameter(s) modified, {ParameterPreviewVM.ChangedOccsCount} suppression(s) updated. (No channel geometry defined in calculator).";
        };

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

        // Clean up previous workspace if exists
        if (ActiveManifest != null)
        {
            try
            {
                _workspaceManager.CleanupWorkspace(ActiveManifest);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not clean up previous workspace: {ex.Message}");
            }
            ActiveManifest = null;
        }

        // Reset all result ViewModels so stale results from previous sessions are never displayed
        CalculatorVM.Clear();
        ModelInventoryVM.Clear();
        ParameterPreviewVM.Clear();
        ChannelResultsVM.Reset("Validation session in progress...");

        IsBusy = true;
        IsProgressIndeterminate = true;
        StageStatus = "Initializing session workspace...";
        _cts = new CancellationTokenSource();

        try
        {
            DiagnosticsLogger.Instance.Info($"Starting session: IAM='{SetupVM.IamPath}', Calc='{SetupVM.CalculatorPath}', Mode='{SetupVM.SelectedMode}', Version='{SetupVM.SelectedVersion}', AllowFormulaOverwrite={SetupVM.AllowFormulaOverwrite}");

            IProgress<string> progress = new Progress<string>(msg =>
            {
                StageStatus = msg;
            });

            // Keep existing Inventor session alive if healthy for warm reuse
            if (_activeInventorSession != null && !_activeInventorSession.IsActive)
            {
                _activeInventorSession.CloseSession();
                _activeInventorSession = null;
            }

            if (_activeInventorSession == null)
            {
                _activeInventorSession = new Inventor.InventorAutomationSession();
            }
            var inventorSession = _activeInventorSession;

            // Step 3: Create disposable workspace
            ActiveManifest = await _workspaceManager.CreateWorkspaceAsync(
                SetupVM.IamPath,
                SetupVM.CalculatorPath,
                progress,
                _cts.Token
            );

            // Step 4-7: Concurrently recalculate/parse Excel and inventory Inventor assembly
            var excelSession = new ExcelAutomationSession();
            var excelTask = excelSession.RecalculateAndParseAsync(
                ActiveManifest.CopiedCalculatorPath,
                progress: progress,
                cancellationToken: _cts.Token
            );

            var inventorTask = inventorSession.StartAndInventoryAsync(
                ActiveManifest.CopiedIamPath,
                requestedVersion: SetupVM.SelectedVersion,
                isVisible: SetupVM.ShowInventorWindow,
                progress: progress,
                cancellationToken: _cts.Token
            );

            await Task.WhenAll(excelTask, inventorTask);

            var calcResult = await excelTask;
            var invResult = await inventorTask;

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

            // Step 8: Parameter matching & Inspect mode discrepancy analysis
            var matchingService = new Inventor.ParameterMatchingService();
            var comparisonResult = matchingService.CompareParameters(calcResult.Sheet1Parameters, invResult);

            ModelInventoryVM.LoadResults(invResult, comparisonResult);

            if (SetupVM.SelectedMode.Equals("Apply", StringComparison.OrdinalIgnoreCase))
            {
                ParameterPreviewVM.LoadSessionData(
                    ActiveManifest,
                    calcResult,
                    invResult,
                    comparisonResult,
                    _activeInventorSession
                );
                ChannelResultsVM.Reset(calcResult.ChannelLocations.Count > 0
                    ? "Apply Preview ready: review parameters and apply changes to run geometry verification."
                    : "No channel locations defined in calculator for this model. Geometry validation will be skipped after apply.");
                SelectedNav = "Parameter Preview";
                StageStatus = $"Apply Preview ready: {calcResult.DataInputs.Count} inputs, {comparisonResult.TotalRows} parameters, {invResult.AvailableRules.Count} rule(s). Review and click 'Apply Changes'.";
                DiagnosticsLogger.Instance.Success($"Apply Preview initialized with {invResult.AvailableRules.Count} iLogic rule(s) detected.");
            }
            else
            {
                // Step 9: Geometry validation across channel groups
                if (calcResult.ChannelLocations.Count > 0)
                {
                    progress?.Report("Extracting CAD hole geometry and validating channel locations...");
                    double roofH = calcResult.GetRoofHeight();
                    double unitW = calcResult.GetUnitWidth();

                    var geomResult = await inventorSession.ValidateGeometryAsync(
                        calcResult.ChannelLocations,
                        roofHeight: roofH,
                        unitWidth: unitW,
                        progress: progress,
                        cancellationToken: _cts.Token
                    );

                    ChannelResultsVM.LoadResults(geomResult);
                    SelectedNav = "Channel Results";
                    StageStatus = $"Inspect Mode complete: {geomResult.MatchedCount} matched, {geomResult.WarningCount} warnings, {geomResult.FailureCount} failures, {geomResult.MissingCount} missing.";
                    DiagnosticsLogger.Instance.Success($"Inspect Mode complete: Geometry verified ({geomResult.MatchedCount} matched, {geomResult.FailureCount} failures, {geomResult.MissingCount} missing).");
                }
                else
                {
                    ChannelResultsVM.Reset("No channel locations defined in calculator for this model. Model inventory and parameter comparison verified.");
                    SelectedNav = "Model Inventory";
                    StageStatus = $"Inspect Mode complete: Model inventory verified ({invResult.TotalOccurrencesCount} components, {comparisonResult.MatchedCount} matched, {comparisonResult.DiscrepancyCount} discrepancies).";
                    DiagnosticsLogger.Instance.Success($"Inspect Mode complete: Model inventory verified with {comparisonResult.DiscrepancyCount} discrepancies out of {comparisonResult.TotalRows} parameters (no channel locations in calculator).");
                }
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
