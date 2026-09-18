using System.Collections.ObjectModel;
using System.Windows.Input;
using InventorValidator.Excel;
using InventorValidator.Geometry.Models;
using InventorValidator.Infrastructure;
using InventorValidator.Inventor;
using InventorValidator.Inventor.Models;
using InventorValidator.Session;

namespace InventorValidator.UI.ViewModels;

public class DataInputItemViewModel : ViewModelBase
{
    private string _currentValue = string.Empty;
    private bool _isModified;

    public string ParameterName { get; init; } = string.Empty;
    public string OriginalValue { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string AllowedValues { get; init; } = string.Empty;

    public string CurrentValue
    {
        get => _currentValue;
        set
        {
            if (SetProperty(ref _currentValue, value))
            {
                IsModified = !string.Equals(_currentValue?.Trim(), OriginalValue?.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public bool IsModified
    {
        get => _isModified;
        set => SetProperty(ref _isModified, value);
    }

    public void Reset()
    {
        CurrentValue = OriginalValue;
    }
}

public class ParameterPreviewViewModel : ViewModelBase
{
    public const string SkipRuleOption = "None (Skip Rule Execution)";
    public const string AllRulesOption = "All Rules (Execute All in Sequence)";

    private string _statusMessage = "Review planned parameter updates and select an iLogic rule before applying to the model copy.";
    private bool _isApplying;
    private bool _isRecalculating;
    private bool _hasAppliedResults;
    private string? _selectedRule;
    private ApplyModeDeltaResult? _deltaResult;
    private string? _errorMessage;
    private int _selectedTabIndex;

    private SessionManifest? _sessionManifest;
    private InventorAutomationSession? _inventorSession;
    private AssemblyInventoryResult? _inventoryResult;
    private ParameterComparisonResult? _comparisonResult;
    private CalculatorSessionResult? _calcResult;

    public ObservableCollection<DataInputItemViewModel> DataInputs { get; } = new();
    public ObservableCollection<string> AvailableRules { get; } = new();
    public ObservableCollection<ParameterComparisonRow> PlannedChanges { get; } = new();
    public ObservableCollection<ParameterDelta> ChangedParameters { get; } = new();
    public ObservableCollection<SuppressionDelta> ChangedSuppressions { get; } = new();
    public Action<GeometryValidationResult>? OnGeometryValidated { get; set; }
    public Action? OnApplyCompletedWithoutGeometry { get; set; }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsApplying
    {
        get => _isApplying;
        set => SetProperty(ref _isApplying, value);
    }

    public bool IsRecalculating
    {
        get => _isRecalculating;
        set => SetProperty(ref _isRecalculating, value);
    }

    public bool HasAppliedResults
    {
        get => _hasAppliedResults;
        set => SetProperty(ref _hasAppliedResults, value);
    }

    public string? SelectedRule
    {
        get => _selectedRule;
        set => SetProperty(ref _selectedRule, value);
    }

    public ApplyModeDeltaResult? DeltaResult
    {
        get => _deltaResult;
        set
        {
            if (SetProperty(ref _deltaResult, value))
            {
                OnPropertyChanged(nameof(ChangedParamsCount));
                OnPropertyChanged(nameof(ChangedOccsCount));
                OnPropertyChanged(nameof(UnsuppressedCount));
                OnPropertyChanged(nameof(SuppressedCount));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasErrors));
            }
        }
    }

    public bool HasErrors => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasModifiedInputs => DataInputs.Any(d => d.IsModified);

    public int DiscrepancyCount => _comparisonResult?.DiscrepancyCount ?? 0;
    public int SafeToWriteCount => _comparisonResult?.SafeToWriteCount ?? 0;
    public int ProtectedFormulaCount => _comparisonResult?.ProtectedFormulaCount ?? 0;
    public int ChangedParamsCount => _deltaResult?.ChangedParametersCount ?? 0;
    public int ChangedOccsCount => _deltaResult?.ChangedSuppressionsCount ?? 0;
    public int UnsuppressedCount => _deltaResult?.UnsuppressedCount ?? 0;
    public int SuppressedCount => _deltaResult?.SuppressedCount ?? 0;

    public ICommand RecalculateCommand { get; }
    public ICommand ResetAllInputsCommand { get; }
    public ICommand ApplyChangesCommand { get; }
    public ICommand? ViewInInventorCommand { get; set; }

    public ParameterPreviewViewModel()
    {
        RecalculateCommand = new RelayCommand(async () => await ExecuteRecalculateAsync(), () => !IsApplying && !IsRecalculating);
        ResetAllInputsCommand = new RelayCommand(ExecuteResetAllInputs, () => !IsApplying && !IsRecalculating);
        ApplyChangesCommand = new RelayCommand(async () => await ExecuteApplyChangesAsync(), () => !IsApplying && !IsRecalculating);
    }

    public void Clear()
    {
        _sessionManifest = null;
        _calcResult = null;
        _inventoryResult = null;
        _comparisonResult = null;
        _inventorSession = null;

        HasAppliedResults = false;
        ErrorMessage = null;
        DeltaResult = null;
        ChangedParameters.Clear();
        ChangedSuppressions.Clear();
        DataInputs.Clear();
        AvailableRules.Clear();
        PlannedChanges.Clear();

        StatusMessage = "Review planned parameter updates and select an iLogic rule before applying to the model copy.";
        SelectedRule = null;
        SelectedTabIndex = 0;

        OnPropertyChanged(nameof(DiscrepancyCount));
        OnPropertyChanged(nameof(SafeToWriteCount));
        OnPropertyChanged(nameof(ProtectedFormulaCount));
        OnPropertyChanged(nameof(HasModifiedInputs));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasAppliedResults));
        OnPropertyChanged(nameof(ChangedParamsCount));
        OnPropertyChanged(nameof(ChangedOccsCount));
        OnPropertyChanged(nameof(UnsuppressedCount));
        OnPropertyChanged(nameof(SuppressedCount));
    }

    public void LoadSessionData(
        SessionManifest manifest,
        CalculatorSessionResult calcResult,
        AssemblyInventoryResult inventoryResult,
        ParameterComparisonResult comparisonResult,
        InventorAutomationSession inventorSession)
    {
        _sessionManifest = manifest;
        _calcResult = calcResult;
        _inventoryResult = inventoryResult;
        _comparisonResult = comparisonResult;
        _inventorSession = inventorSession;

        HasAppliedResults = false;
        ErrorMessage = null;
        DeltaResult = null;
        ChangedParameters.Clear();
        ChangedSuppressions.Clear();
        SelectedTabIndex = 0;

        // 1. Populate Primary Data Inputs
        DataInputs.Clear();
        foreach (var input in calcResult.DataInputs)
        {
            var item = new DataInputItemViewModel
            {
                ParameterName = input.ParameterName,
                OriginalValue = input.DisplayedValue,
                CurrentValue = input.DisplayedValue,
                Description = input.Description,
                AllowedValues = input.AllowedValues
            };
            DataInputs.Add(item);
        }

        // 2. Populate Available iLogic Rules (Dynamic discovery - never assume hardcoded names)
        AvailableRules.Clear();

        if (inventoryResult.AvailableRules.Count > 0)
        {
            if (inventoryResult.AvailableRules.Count > 1)
            {
                AvailableRules.Add(AllRulesOption);
            }

            foreach (var rule in inventoryResult.AvailableRules)
            {
                AvailableRules.Add(rule);
            }

            AvailableRules.Add(SkipRuleOption);

            // Prioritize selecting a rule containing "Suppression" or "Part"
            var suppressionRule = inventoryResult.AvailableRules
                .FirstOrDefault(r => r.IndexOf("suppress", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     r.IndexOf("part", StringComparison.OrdinalIgnoreCase) >= 0);

            SelectedRule = suppressionRule ?? (inventoryResult.AvailableRules.Count > 1 ? AllRulesOption : inventoryResult.AvailableRules[0]);
        }
        else
        {
            AvailableRules.Add(SkipRuleOption);
            SelectedRule = SkipRuleOption;
        }

        // 3. Populate Planned Parameter Changes
        PlannedChanges.Clear();
        foreach (var row in comparisonResult.Rows)
        {
            PlannedChanges.Add(row);
        }

        OnPropertyChanged(nameof(DiscrepancyCount));
        OnPropertyChanged(nameof(SafeToWriteCount));
        OnPropertyChanged(nameof(ProtectedFormulaCount));
        OnPropertyChanged(nameof(HasModifiedInputs));

        StatusMessage = $"Ready to preview: {calcResult.DataInputs.Count} primary inputs, {PlannedChanges.Count} mapped parameters, {AvailableRules.Count - 1} iLogic rule(s) detected.";
        DiagnosticsLogger.Instance.Info($"Loaded Parameter Preview: {calcResult.DataInputs.Count} inputs, {PlannedChanges.Count} parameters, Selected Rule='{SelectedRule}'");
    }

    private void ExecuteResetAllInputs()
    {
        foreach (var item in DataInputs)
        {
            item.Reset();
        }
        OnPropertyChanged(nameof(HasModifiedInputs));
        StatusMessage = "All input overrides reset to original spreadsheet values.";
    }

    private async Task ExecuteRecalculateAsync()
    {
        if (_sessionManifest == null || string.IsNullOrEmpty(_sessionManifest.CopiedCalculatorPath))
        {
            StatusMessage = "Cannot recalculate: No active disposable workspace found.";
            return;
        }

        IsRecalculating = true;
        StatusMessage = "Applying input overrides and recalculating Excel...";
        ErrorMessage = null;

        try
        {
            var overrides = DataInputs
                .Where(d => d.IsModified)
                .ToDictionary(d => d.ParameterName, d => d.CurrentValue, StringComparer.OrdinalIgnoreCase);

            var excelSession = new ExcelAutomationSession();
            var progress = new Progress<string>(msg => StatusMessage = msg);

            var newCalcResult = await excelSession.RecalculateAndParseAsync(
                _sessionManifest.CopiedCalculatorPath,
                inputOverrides: overrides,
                progress: progress
            );

            _calcResult = newCalcResult;

            // Re-match against the existing model inventory
            if (_inventoryResult != null)
            {
                var matchingService = new ParameterMatchingService();
                _comparisonResult = matchingService.CompareParameters(newCalcResult.Sheet1Parameters, _inventoryResult);

                PlannedChanges.Clear();
                foreach (var row in _comparisonResult.Rows)
                {
                    PlannedChanges.Add(row);
                }

                OnPropertyChanged(nameof(DiscrepancyCount));
                OnPropertyChanged(nameof(SafeToWriteCount));
                OnPropertyChanged(nameof(ProtectedFormulaCount));
            }

            StatusMessage = $"Recalculation complete: {overrides.Count} override(s) applied. Review planned changes below.";
            DiagnosticsLogger.Instance.Success($"Recalculated disposable calculator with {overrides.Count} overrides.");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Recalculation error: {ex.Message}";
            StatusMessage = "Failed to recalculate workbook with input overrides.";
            DiagnosticsLogger.Instance.Error($"Error recalculating inputs: {ex.Message}", ex);
        }
        finally
        {
            IsRecalculating = false;
        }
    }

    private async Task ExecuteApplyChangesAsync()
    {
        if (_inventorSession == null || !_inventorSession.IsActive)
        {
            ErrorMessage = "Dedicated Inventor automation session is not currently active.";
            StatusMessage = "Cannot apply changes: Inventor session is not active.";
            return;
        }

        IsApplying = true;
        StatusMessage = "Applying parameters and executing iLogic rule...";
        ErrorMessage = null;

        try
        {
            // If there are un-recalculated modifications, apply and recalculate first
            if (DataInputs.Any(d => d.IsModified))
            {
                await ExecuteRecalculateAsync();
            }

            var progress = new Progress<string>(msg => StatusMessage = msg);

            string? ruleToRun = SelectedRule;
            if (string.Equals(ruleToRun, SkipRuleOption, StringComparison.OrdinalIgnoreCase))
            {
                ruleToRun = null;
            }

            var delta = await _inventorSession.ApplyAndRebuildAsync(ruleToRun, progress);
            DeltaResult = delta;

            ChangedParameters.Clear();
            foreach (var p in delta.ParameterDeltas.Where(p => p.HasChanged))
            {
                ChangedParameters.Add(p);
            }

            ChangedSuppressions.Clear();
            foreach (var s in delta.SuppressionDeltas.Where(s => s.HasStateChanged))
            {
                ChangedSuppressions.Add(s);
            }

            HasAppliedResults = true;
            SelectedTabIndex = 2;

            if (!delta.Success)
            {
                ErrorMessage = delta.ErrorMessage ?? "Assembly rebuild warning or error occurred.";
                StatusMessage = $"Apply completed with issues: {ErrorMessage}";
            }
            else if (!delta.RuleExecutedSuccessfully)
            {
                ErrorMessage = delta.RuleMessage ?? "iLogic rule execution warning.";
                StatusMessage = $"Apply completed with rule warning: {ErrorMessage}";
            }
            else
            {
                StatusMessage = $"Apply Mode complete: {delta.ChangedParametersCount} parameter(s) modified, {delta.ChangedSuppressionsCount} occurrence suppression(s) updated.";

                // Run geometry validation on updated assembly
                if (_calcResult != null && _calcResult.ChannelLocations.Count > 0)
                {
                    try
                    {
                        StatusMessage = "Validating updated geometry against channel locations...";
                        double roofH = _calcResult.GetRoofHeight();
                        double unitW = _calcResult.GetUnitWidth();

                        var geomResult = await _inventorSession.ValidateGeometryAsync(
                            _calcResult.ChannelLocations,
                            roofHeight: roofH,
                            unitWidth: unitW
                        );

                        OnGeometryValidated?.Invoke(geomResult);
                        StatusMessage = $"Apply & Geometry complete: {delta.ChangedParametersCount} param(s) modified, {geomResult.MatchedCount} hole(s) matched, {geomResult.FailureCount} failure(s).";
                    }
                    catch (Exception gEx)
                    {
                        DiagnosticsLogger.Instance.Warn($"Geometry validation after apply encountered an issue: {gEx.Message}");
                    }
                }
                else
                {
                    DiagnosticsLogger.Instance.Info("No channel locations defined in calculator for this model; geometry hole extraction skipped.");
                    OnApplyCompletedWithoutGeometry?.Invoke();
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Apply Mode error: {ex.Message}";
            StatusMessage = "Error occurred while applying changes to Inventor model.";
            DiagnosticsLogger.Instance.Error($"Apply Mode failure: {ex.Message}", ex);
        }
        finally
        {
            IsApplying = false;
        }
    }
}
