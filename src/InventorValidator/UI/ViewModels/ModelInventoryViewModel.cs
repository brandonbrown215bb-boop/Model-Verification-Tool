using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using InventorValidator.Infrastructure;
using InventorValidator.Inventor.Models;

namespace InventorValidator.UI.ViewModels;

public class ModelInventoryViewModel : ViewModelBase
{
    private AssemblyInventoryResult? _inventory;
    private ParameterComparisonResult? _comparison;

    private string _searchText = string.Empty;
    private string _selectedSubTab = "Parameters";
    private string _parameterFilter = "All";
    private bool _showDiscrepanciesOnly;

    public AssemblyInventoryResult? Inventory
    {
        get => _inventory;
        set => SetProperty(ref _inventory, value);
    }

    public ParameterComparisonResult? Comparison
    {
        get => _comparison;
        set => SetProperty(ref _comparison, value);
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

    public string SelectedSubTab
    {
        get => _selectedSubTab;
        set => SetProperty(ref _selectedSubTab, value);
    }

    public string ParameterFilter
    {
        get => _parameterFilter;
        set
        {
            if (SetProperty(ref _parameterFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool ShowDiscrepanciesOnly
    {
        get => _showDiscrepanciesOnly;
        set
        {
            if (SetProperty(ref _showDiscrepanciesOnly, value))
            {
                ApplyFilters();
            }
        }
    }

    // Observable Collections for UI binding
    public ObservableCollection<ParameterComparisonRow> ParameterRows { get; } = new();
    public ObservableCollection<OccurrenceInventoryItem> OccurrenceRows { get; } = new();
    public ObservableCollection<InventorFeatureItem> FeatureRows { get; } = new();

    public ICollectionView ParametersView { get; }
    public ICollectionView OccurrencesView { get; }
    public ICollectionView FeaturesView { get; }

    public ICommand SelectSubTabCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand? ViewInInventorCommand { get; set; }
    public bool CanViewInInventor => Inventory != null && Inventory.ProcessId > 0;

    // Summary Metric Properties
    public string AssemblyName => Inventory?.AssemblyName ?? "No assembly loaded";
    public string InventorVersionInfo => Inventory != null ? $"{Inventory.InventorVersion} (PID {Inventory.ProcessId})" : "—";
    public string ActiveRepresentation => Inventory?.ActiveRepresentation ?? "—";
    public bool HasILogicLOD => Inventory?.HasILogicRepresentation ?? false;
    public int TotalOccurrences => Inventory?.TotalOccurrencesCount ?? 0;
    public int ActiveOccurrences => Inventory?.ActiveOccurrencesCount ?? 0;
    public int SuppressedOccurrences => Inventory?.SuppressedOccurrencesCount ?? 0;
    public int TotalParameters => Inventory?.Parameters.Count ?? 0;
    public int TotalFeatures => Inventory?.Features.Count ?? 0;

    public int MatchedCount => Comparison?.MatchedCount ?? 0;
    public int DiscrepancyCount => Comparison?.DiscrepancyCount ?? 0;
    public int SafeToWriteCount => Comparison?.SafeToWriteCount ?? 0;
    public int ProtectedFormulaCount => Comparison?.ProtectedFormulaCount ?? 0;

    public ModelInventoryViewModel()
    {
        ParametersView = CollectionViewSource.GetDefaultView(ParameterRows);
        OccurrencesView = CollectionViewSource.GetDefaultView(OccurrenceRows);
        FeaturesView = CollectionViewSource.GetDefaultView(FeatureRows);

        ParametersView.Filter = FilterParameterItem;
        OccurrencesView.Filter = FilterOccurrenceItem;
        FeaturesView.Filter = FilterFeatureItem;

        SelectSubTabCommand = new RelayCommand(param =>
        {
            if (param is string tab) SelectedSubTab = tab;
        });

        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
    }

    public void Clear()
    {
        Inventory = null;
        Comparison = null;

        ParameterRows.Clear();
        OccurrenceRows.Clear();
        FeatureRows.Clear();

        OnPropertyChanged(nameof(AssemblyName));
        OnPropertyChanged(nameof(InventorVersionInfo));
        OnPropertyChanged(nameof(ActiveRepresentation));
        OnPropertyChanged(nameof(HasILogicLOD));
        OnPropertyChanged(nameof(TotalOccurrences));
        OnPropertyChanged(nameof(ActiveOccurrences));
        OnPropertyChanged(nameof(SuppressedOccurrences));
        OnPropertyChanged(nameof(TotalParameters));
        OnPropertyChanged(nameof(TotalFeatures));
        OnPropertyChanged(nameof(MatchedCount));
        OnPropertyChanged(nameof(DiscrepancyCount));
        OnPropertyChanged(nameof(SafeToWriteCount));
        OnPropertyChanged(nameof(ProtectedFormulaCount));
        OnPropertyChanged(nameof(CanViewInInventor));

        ApplyFilters();
    }

    public void LoadResults(AssemblyInventoryResult inventory, ParameterComparisonResult? comparison = null)
    {
        Inventory = inventory;
        Comparison = comparison;

        ParameterRows.Clear();
        if (comparison != null)
        {
            foreach (var row in comparison.Rows)
            {
                ParameterRows.Add(row);
            }
        }

        OccurrenceRows.Clear();
        foreach (var occ in FlattenOccurrences(inventory.Occurrences))
        {
            OccurrenceRows.Add(occ);
        }

        FeatureRows.Clear();
        foreach (var f in inventory.Features)
        {
            FeatureRows.Add(f);
        }

        OnPropertyChanged(nameof(AssemblyName));
        OnPropertyChanged(nameof(InventorVersionInfo));
        OnPropertyChanged(nameof(ActiveRepresentation));
        OnPropertyChanged(nameof(HasILogicLOD));
        OnPropertyChanged(nameof(TotalOccurrences));
        OnPropertyChanged(nameof(ActiveOccurrences));
        OnPropertyChanged(nameof(SuppressedOccurrences));
        OnPropertyChanged(nameof(TotalParameters));
        OnPropertyChanged(nameof(TotalFeatures));
        OnPropertyChanged(nameof(MatchedCount));
        OnPropertyChanged(nameof(DiscrepancyCount));
        OnPropertyChanged(nameof(SafeToWriteCount));
        OnPropertyChanged(nameof(ProtectedFormulaCount));
        OnPropertyChanged(nameof(CanViewInInventor));

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        ParametersView.Refresh();
        OccurrencesView.Refresh();
        FeaturesView.Refresh();
    }

    private bool FilterParameterItem(object obj)
    {
        if (obj is not ParameterComparisonRow row) return false;

        if (ShowDiscrepanciesOnly && row.Status != ComparisonStatus.Discrepancy)
            return false;

        if (!string.IsNullOrEmpty(SearchText))
        {
            bool matchesSearch =
                row.ExcelParameterName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (row.TargetParameterName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (row.TargetOccurrenceName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (row.Notes?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);

            if (!matchesSearch) return false;
        }

        return true;
    }

    private bool FilterOccurrenceItem(object obj)
    {
        if (obj is not OccurrenceInventoryItem occ) return false;

        if (!string.IsNullOrEmpty(SearchText))
        {
            return occ.OccurrenceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                   occ.PartNumber.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private bool FilterFeatureItem(object obj)
    {
        if (obj is not InventorFeatureItem feat) return false;

        if (!string.IsNullOrEmpty(SearchText))
        {
            return feat.FeatureName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                   feat.ContainingOccurrence.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static List<OccurrenceInventoryItem> FlattenOccurrences(IEnumerable<OccurrenceInventoryItem> items)
    {
        var list = new List<OccurrenceInventoryItem>();
        foreach (var item in items)
        {
            list.Add(item);
            list.AddRange(FlattenOccurrences(item.Children));
        }
        return list;
    }
}
