using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using InventorValidator.Geometry.Models;
using InventorValidator.Infrastructure;
using Microsoft.Win32;

namespace InventorValidator.UI.ViewModels;

public class HoleMatchResultItemViewModel : ViewModelBase
{
    private readonly HoleMatchResult _model;

    public HoleMatchResult Model => _model;

    public HoleMatchStatus Status => _model.Status;

    public string StatusBadge => _model.Status switch
    {
        HoleMatchStatus.Match => "MATCH",
        HoleMatchStatus.Warning => "WARNING",
        HoleMatchStatus.Mislocated => "MISLOCATED",
        HoleMatchStatus.MissingExpected => "MISSING",
        HoleMatchStatus.ExtraActual => "EXTRA",
        HoleMatchStatus.SkippedInvalidRow => "SKIPPED",
        _ => "UNKNOWN"
    };

    public string StatusColor => _model.Status switch
    {
        HoleMatchStatus.Match => "#2EA043",           // Green
        HoleMatchStatus.Warning => "#D29922",         // Amber
        HoleMatchStatus.Mislocated => "#F85149",      // Red
        HoleMatchStatus.MissingExpected => "#CF222E", // Dark Red
        HoleMatchStatus.ExtraActual => "#A371F7",     // Purple
        HoleMatchStatus.SkippedInvalidRow => "#8B949E", // Gray
        _ => "#8B949E"
    };

    public string ChannelGroup => _model.ChannelGroup;
    public string ChannelName => _model.ChannelName;
    public string HoleIndexDisplay => _model.HoleIndex >= 0 ? _model.HoleIndex.ToString() : "—";
    public string ReferencedPart => _model.ReferencedPart;
    public string OwningPart => _model.OwningPartNumber;
    public string OwningOccurrence => _model.OwningOccurrence;

    public string ExpectedCoordDisplay => _model.Expected != null
        ? $"({_model.Expected.ExpectedPosition.X:F3}, {_model.Expected.ExpectedPosition.Y:F3}, {_model.Expected.ExpectedPosition.Z:F3})"
        : "—";

    public string ActualCoordDisplay => _model.Actual != null
        ? $"({_model.Actual.Position.X:F3}, {_model.Actual.Position.Y:F3}, {_model.Actual.Position.Z:F3})"
        : "—";

    public string DeltaAxisDisplay => _model.Actual != null ? $"{_model.DeltaAxis:+0.0000;-0.0000;0.0000}\"" : "—";
    public string DeltaZDisplay => _model.Actual != null ? $"{_model.DeltaZ:+0.0000;-0.0000;0.0000}\"" : "—";
    public string AuthoritativeErrorDisplay => _model.Actual != null ? $"{_model.AuthoritativeError:F4}\"" : "—";
    public string TransverseDeltaDisplay => _model.Actual != null ? $"{_model.TransverseDelta:+0.0000;-0.0000;0.0000}\"" : "—";
    public string Total3DDistanceDisplay => _model.Actual != null ? $"{_model.Total3DDistance:F4}\"" : "—";

    public string ExpectedDiameterDisplay => _model.Expected?.ExpectedDiameter.HasValue == true
        ? $"{_model.Expected.ExpectedDiameter.Value:F3}\""
        : "—";

    public string ActualDiameterDisplay => _model.Actual != null
        ? $"{_model.Actual.Diameter:F3}\""
        : "—";

    public string Notes => _model.Notes;

    public HoleMatchResultItemViewModel(HoleMatchResult model)
    {
        _model = model;
    }
}

public class ChannelResultsViewModel : ViewModelBase
{
    private GeometryValidationResult? _validationResult;
    private string _statusMessage = "Channel location geometry results will appear here after CAD hole extraction and matching.";
    private bool _hasResults;
    private string _segmentStartReference = "Assembly Z=0";
    private string _segmentStartZOffset = "0.0000\"";

    private string _selectedGroupFilter = "All Groups";
    private string _selectedStatusFilter = "All Statuses";
    private string _selectedChannelFilter = "All Channels";
    private string _searchText = string.Empty;
    private bool _showExtraHoles = true;

    private PatternDiagnosticResult? _selectedDiagnostic;
    private bool _hasSelectedDiagnostic;

    private HoleMatchResultItemViewModel? _selectedItem;
    private bool _showOverlaysInInventor = true;
    private bool _autoZoomInInventor = true;
    private bool _isLoadingResults;

    public ObservableCollection<HoleMatchResultItemViewModel> AllResults { get; } = new();
    public ObservableCollection<HoleMatchResultItemViewModel> FilteredResults { get; } = new();
    public ObservableCollection<PatternDiagnosticResult> ChannelDiagnostics { get; } = new();
    public ObservableCollection<string> AvailableChannelNames { get; } = new();

    public Func<Inventor.InventorAutomationSession?>? InventorSessionProvider { get; set; }
    public ICommand? ViewInInventorCommand { get; set; }

    public IReadOnlyList<string> GroupFilterOptions { get; } = new[]
    {
        "All Groups",
        "Floor Channels",
        "Roof Channels",
        "South Wall Channels",
        "North Wall Channels"
    };

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[]
    {
        "All Statuses",
        "Matches Only",
        "Warnings Only",
        "Failures/Discrepancies",
        "Missing Only",
        "Extra Holes"
    };

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasResults
    {
        get => _hasResults;
        set => SetProperty(ref _hasResults, value);
    }

    public string SegmentStartReference
    {
        get => _segmentStartReference;
        set => SetProperty(ref _segmentStartReference, value);
    }

    public string SegmentStartZOffset
    {
        get => _segmentStartZOffset;
        set => SetProperty(ref _segmentStartZOffset, value);
    }

    public int TotalExpectedCount => _validationResult?.TotalHolesExpected ?? 0;
    public int MatchedCount => _validationResult?.MatchedCount ?? 0;
    public int WarningCount => _validationResult?.WarningCount ?? 0;
    public int FailureCount => _validationResult?.FailureCount ?? 0;
    public int MissingCount => _validationResult?.MissingCount ?? 0;
    public int ExtraCount => _validationResult?.ExtraCount ?? 0;

    public HoleMatchResultItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnSelectedItemChanged(value);
            }
        }
    }

    public bool ShowOverlaysInInventor
    {
        get => _showOverlaysInInventor;
        set
        {
            if (SetProperty(ref _showOverlaysInInventor, value))
            {
                if (value && _validationResult != null)
                {
                    ExecuteRenderOverlays();
                }
                else if (!value)
                {
                    ExecuteClearOverlays();
                }
            }
        }
    }

    public bool AutoZoomInInventor
    {
        get => _autoZoomInInventor;
        set => SetProperty(ref _autoZoomInInventor, value);
    }

    public string SelectedGroupFilter
    {
        get => _selectedGroupFilter;
        set
        {
            if (SetProperty(ref _selectedGroupFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public string SelectedChannelFilter
    {
        get => _selectedChannelFilter;
        set
        {
            if (SetProperty(ref _selectedChannelFilter, value))
            {
                UpdateSelectedDiagnostic();
                ApplyFilters();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
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
                ApplyFilters();
            }
        }
    }

    public PatternDiagnosticResult? SelectedDiagnostic
    {
        get => _selectedDiagnostic;
        set => SetProperty(ref _selectedDiagnostic, value);
    }

    public bool HasSelectedDiagnostic
    {
        get => _hasSelectedDiagnostic;
        set => SetProperty(ref _hasSelectedDiagnostic, value);
    }

    public ICommand ClearFiltersCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand RenderOverlaysCommand { get; }
    public ICommand ClearOverlaysCommand { get; }
    public ICommand SetStatusFilterCommand { get; }
    public ICommand SetGroupFilterCommand { get; }

    public ChannelResultsViewModel()
    {
        ClearFiltersCommand = new RelayCommand(() =>
        {
            _selectedGroupFilter = "All Groups";
            _selectedStatusFilter = "All Statuses";
            _selectedChannelFilter = "All Channels";
            _searchText = string.Empty;
            _showExtraHoles = true;
            OnPropertyChanged(nameof(SelectedGroupFilter));
            OnPropertyChanged(nameof(SelectedStatusFilter));
            OnPropertyChanged(nameof(SelectedChannelFilter));
            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(ShowExtraHoles));
            UpdateSelectedDiagnostic();
            ApplyFilters();
        });

        ExportCsvCommand = new RelayCommand(ExecuteExportCsv, () => HasResults && FilteredResults.Count > 0);
        RenderOverlaysCommand = new RelayCommand(ExecuteRenderOverlays, () => HasResults);
        ClearOverlaysCommand = new RelayCommand(ExecuteClearOverlays);

        SetStatusFilterCommand = new RelayCommand(param =>
        {
            if (param is string s)
            {
                SelectedStatusFilter = s;
            }
        });

        SetGroupFilterCommand = new RelayCommand(param =>
        {
            if (param is string g)
            {
                SelectedGroupFilter = g;
            }
        });
    }

    private void OnSelectedItemChanged(HoleMatchResultItemViewModel? item)
    {
        if (item?.Model == null) return;

        if (_autoZoomInInventor)
        {
            try
            {
                var session = InventorSessionProvider?.Invoke();
                if (session != null && session.IsActive)
                {
                    _ = session.ZoomAndHighlightHoleAsync(item.Model);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not trigger Inventor camera zoom: {ex.Message}");
            }
        }
    }

    public void ExecuteRenderOverlays()
    {
        if (_validationResult == null) return;

        try
        {
            var session = InventorSessionProvider?.Invoke();
            if (session != null && session.IsActive)
            {
                DiagnosticsLogger.Instance.Info("Rendering 3D visual markers in Autodesk Inventor...");
                var holesToRender = FilteredResults.Select(r => r.Model).ToList();
                _ = session.RenderOverlaysAsync(_validationResult, holesToRender, _showExtraHoles);
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not render 3D overlays in Inventor: {ex.Message}");
        }
    }

    public void ExecuteClearOverlays()
    {
        try
        {
            var session = InventorSessionProvider?.Invoke();
            if (session != null && session.IsActive)
            {
                DiagnosticsLogger.Instance.Info("Clearing 3D visual markers in Autodesk Inventor...");
                _ = session.ClearOverlaysAsync();
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not clear 3D overlays in Inventor: {ex.Message}");
        }
    }

    public void Reset(string? statusMessage = null)
    {
        _validationResult = null;
        SegmentStartReference = "Assembly Z=0";
        SegmentStartZOffset = "0.0000\"";

        AllResults.Clear();
        FilteredResults.Clear();
        ChannelDiagnostics.Clear();
        AvailableChannelNames.Clear();
        SelectedDiagnostic = null;
        HasSelectedDiagnostic = false;
        SelectedItem = null;

        _selectedGroupFilter = "All Groups";
        _selectedStatusFilter = "All Statuses";
        _selectedChannelFilter = "All Channels";
        _searchText = string.Empty;
        _showExtraHoles = true;

        HasResults = false;
        StatusMessage = statusMessage ?? "Channel location geometry results will appear here after CAD hole extraction and matching.";

        OnPropertyChanged(nameof(TotalExpectedCount));
        OnPropertyChanged(nameof(MatchedCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(FailureCount));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(ExtraCount));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(SegmentStartReference));
        OnPropertyChanged(nameof(SegmentStartZOffset));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(SelectedGroupFilter));
        OnPropertyChanged(nameof(SelectedStatusFilter));
        OnPropertyChanged(nameof(SelectedChannelFilter));
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(ShowExtraHoles));
        OnPropertyChanged(nameof(SelectedItem));

        ExecuteClearOverlays();
    }

    public void LoadResults(GeometryValidationResult result)
    {
        _validationResult = result;
        SegmentStartReference = result.SegmentStartReferenceName;
        SegmentStartZOffset = $"{result.SegmentStartZOffset:F4}\"";

        AllResults.Clear();
        ChannelDiagnostics.Clear();
        AvailableChannelNames.Clear();

        AvailableChannelNames.Add("All Channels");
        var channelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in result.Results)
        {
            var vm = new HoleMatchResultItemViewModel(r);
            AllResults.Add(vm);

            if (!string.IsNullOrEmpty(r.ChannelName) && r.ChannelName != "—")
            {
                channelNames.Add(r.ChannelName);
            }
        }

        foreach (var name in channelNames.OrderBy(n => n))
        {
            AvailableChannelNames.Add(name);
        }

        foreach (var diag in result.ChannelDiagnostics)
        {
            ChannelDiagnostics.Add(diag);
        }

        HasResults = true;
        StatusMessage = $"Geometry verification complete: {result.MatchedCount} matched, {result.WarningCount} warnings, {result.FailureCount} failures, {result.MissingCount} missing, {result.ExtraCount} extra.";

        OnPropertyChanged(nameof(TotalExpectedCount));
        OnPropertyChanged(nameof(MatchedCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(FailureCount));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(ExtraCount));

        _isLoadingResults = true;
        try
        {
            SelectedChannelFilter = "All Channels";
            SelectedStatusFilter = "All Statuses";
            SelectedGroupFilter = "All Groups";

            UpdateSelectedDiagnostic();
            ApplyFilters();

            SelectedItem = FilteredResults.FirstOrDefault() ?? AllResults.FirstOrDefault();
        }
        finally
        {
            _isLoadingResults = false;
        }

        if (ShowOverlaysInInventor)
        {
            ExecuteRenderOverlays();
        }
    }

    private void UpdateSelectedDiagnostic()
    {
        if (_selectedChannelFilter == "All Channels" || string.IsNullOrEmpty(_selectedChannelFilter))
        {
            SelectedDiagnostic = ChannelDiagnostics.FirstOrDefault(d => d.IsSystematicShift) ??
                                 ChannelDiagnostics.FirstOrDefault(d => d.MissingCount > 0) ??
                                 ChannelDiagnostics.FirstOrDefault();
            HasSelectedDiagnostic = SelectedDiagnostic != null;
        }
        else
        {
            SelectedDiagnostic = ChannelDiagnostics.FirstOrDefault(d => string.Equals(d.ChannelName, _selectedChannelFilter, StringComparison.OrdinalIgnoreCase));
            HasSelectedDiagnostic = SelectedDiagnostic != null;
        }
    }

    private void ApplyFilters()
    {
        FilteredResults.Clear();

        var query = AllResults.AsEnumerable();

        // 1. Group filter
        if (_selectedGroupFilter != "All Groups")
        {
            query = query.Where(r => r.ChannelGroup.IndexOf(_selectedGroupFilter.Replace(" Channels", ""), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // 2. Status filter
        if (_selectedStatusFilter == "Matches Only")
        {
            query = query.Where(r => r.Status == HoleMatchStatus.Match);
        }
        else if (_selectedStatusFilter == "Warnings Only")
        {
            query = query.Where(r => r.Status == HoleMatchStatus.Warning);
        }
        else if (_selectedStatusFilter == "Failures/Discrepancies")
        {
            query = query.Where(r => r.Status == HoleMatchStatus.Mislocated);
        }
        else if (_selectedStatusFilter == "Missing Only")
        {
            query = query.Where(r => r.Status == HoleMatchStatus.MissingExpected);
        }
        else if (_selectedStatusFilter == "Extra Holes")
        {
            query = query.Where(r => r.Status == HoleMatchStatus.ExtraActual);
        }

        // 3. Channel filter
        if (_selectedChannelFilter != "All Channels")
        {
            query = query.Where(r => string.Equals(r.ChannelName, _selectedChannelFilter, StringComparison.OrdinalIgnoreCase));
        }

        // 4. Extra holes visibility
        if (!_showExtraHoles)
        {
            query = query.Where(r => r.Status != HoleMatchStatus.ExtraActual);
        }

        // 5. Search text
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            string s = _searchText.Trim();
            query = query.Where(r =>
                r.ChannelName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                r.ReferencedPart.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                r.OwningPart.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                r.Notes.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query)
        {
            FilteredResults.Add(item);
        }

        if (SelectedItem != null && !FilteredResults.Contains(SelectedItem))
        {
            SelectedItem = FilteredResults.FirstOrDefault();
        }

        if (!_isLoadingResults && _hasResults && _showOverlaysInInventor)
        {
            ExecuteRenderOverlays();
        }
    }

    private void ExecuteExportCsv()
    {
        try
        {
            var sfd = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                FileName = $"GeometryValidation_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Export Geometry Results CSV"
            };

            if (sfd.ShowDialog() == true)
            {
                using var writer = new StreamWriter(sfd.FileName);
                writer.WriteLine("Status,Group,Channel,Index,ReferencedPart,OwningPart,OwningOccurrence,ExpectedCoord,ActualCoord,DeltaAxis,DeltaZ,AuthoritativeError,TransverseDelta,Total3DDistance,ExpectedDiameter,ActualDiameter,Notes");

                foreach (var item in FilteredResults)
                {
                    writer.WriteLine(
                        $"\"{item.StatusBadge}\"," +
                        $"\"{item.ChannelGroup}\"," +
                        $"\"{item.ChannelName}\"," +
                        $"\"{item.HoleIndexDisplay}\"," +
                        $"\"{item.ReferencedPart}\"," +
                        $"\"{item.OwningPart}\"," +
                        $"\"{item.OwningOccurrence}\"," +
                        $"\"{item.ExpectedCoordDisplay}\"," +
                        $"\"{item.ActualCoordDisplay}\"," +
                        $"\"{item.DeltaAxisDisplay}\"," +
                        $"\"{item.DeltaZDisplay}\"," +
                        $"\"{item.AuthoritativeErrorDisplay}\"," +
                        $"\"{item.TransverseDeltaDisplay}\"," +
                        $"\"{item.Total3DDistanceDisplay}\"," +
                        $"\"{item.ExpectedDiameterDisplay}\"," +
                        $"\"{item.ActualDiameterDisplay}\"," +
                        $"\"{item.Notes.Replace("\"", "\"\"")}\"");
                }

                DiagnosticsLogger.Instance.Success($"Exported {FilteredResults.Count} geometry results to {sfd.FileName}");
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Error($"Error exporting results CSV: {ex.Message}", ex);
        }
    }
}
