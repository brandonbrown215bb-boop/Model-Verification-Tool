using System.Collections.ObjectModel;
using InventorValidator.Excel;
using InventorValidator.Infrastructure;

namespace InventorValidator.UI.ViewModels;

public class CalculatorViewModel : ViewModelBase
{
    private CalculatorSessionResult? _result;
    private string _statusMessage = "No calculator loaded yet. Start validation or click 'Inspect Calculator'.";
    private string _searchText = string.Empty;
    private string _selectedCategory = "All";
    private string _selectedSubTab = "Sheet1 Parameters";

    private ObservableCollection<DataInputItem> _dataInputs = new();
    private ObservableCollection<Sheet1ParameterItem> _filteredSheet1Parameters = new();
    private ObservableCollection<ChannelLocationItem> _channelLocations = new();

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public CalculatorSessionResult? Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    public bool HasData => _result != null;

    public string WorkbookPath => _result?.WorkbookPath ?? "—";
    public string RecalculationDuration => _result != null ? $"{_result.RecalculationDuration.TotalSeconds:F2} s" : "—";
    public string CalculationState => _result?.CalculationState ?? "—";
    public string ErrorCheckRaw => _result?.WorkbookErrorCheckRaw ?? "—";
    public bool IsErrorCheckPassed => _result?.IsErrorCheckPassed ?? false;

    public int TotalDataInputs => _result?.TotalDataInputs ?? 0;
    public int TotalSheet1Parameters => _result?.TotalSheet1Parameters ?? 0;
    public int TotalChannels => _result?.TotalChannels ?? 0;
    public int ValidChannelsCount => _result?.ValidChannelsCount ?? 0;
    public int SkippedChannelsCount => _result?.SkippedChannelsCount ?? 0;
    public int TotalSegmentsCount => _result?.TotalSegmentsCount ?? 0;
    public int TotalHolesCount => _result?.TotalHolesCount ?? 0;

    public int DimensionCount => _result?.DimensionCount ?? 0;
    public int SuppressionCount => _result?.SuppressionCount ?? 0;
    public int UnitlessCount => _result?.UnitlessCount ?? 0;
    public int OtherCount => _result?.OtherCount ?? 0;

    public string SelectedSubTab
    {
        get => _selectedSubTab;
        set => SetProperty(ref _selectedSubTab, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplySheet1Filter();
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplySheet1Filter();
            }
        }
    }

    public ObservableCollection<string> Categories { get; } = new()
    {
        "All",
        "Dimensions",
        "Suppression",
        "Counts",
        "Other"
    };

    public ObservableCollection<DataInputItem> DataInputs
    {
        get => _dataInputs;
        set => SetProperty(ref _dataInputs, value);
    }

    public ObservableCollection<Sheet1ParameterItem> FilteredSheet1Parameters
    {
        get => _filteredSheet1Parameters;
        set => SetProperty(ref _filteredSheet1Parameters, value);
    }

    public ObservableCollection<ChannelLocationItem> ChannelLocations
    {
        get => _channelLocations;
        set => SetProperty(ref _channelLocations, value);
    }

    private ObservableCollection<UnifiedChannel> _unifiedChannels = new();
    public ObservableCollection<UnifiedChannel> UnifiedChannels
    {
        get => _unifiedChannels;
        set => SetProperty(ref _unifiedChannels, value);
    }

    private UnifiedChannel? _selectedUnifiedChannel;
    public UnifiedChannel? SelectedUnifiedChannel
    {
        get => _selectedUnifiedChannel;
        set
        {
            if (SetProperty(ref _selectedUnifiedChannel, value))
            {
                UpdateSelectedChannelSegments();
            }
        }
    }

    private ObservableCollection<ChannelLocationItem> _selectedChannelSegments = new();
    public ObservableCollection<ChannelLocationItem> SelectedChannelSegments
    {
        get => _selectedChannelSegments;
        set => SetProperty(ref _selectedChannelSegments, value);
    }

    private bool _showAllRowsView;
    public bool ShowAllRowsView
    {
        get => _showAllRowsView;
        set => SetProperty(ref _showAllRowsView, value);
    }

    public void LoadResult(CalculatorSessionResult result)
    {
        Result = result;
        StatusMessage = $"Recalculated in {result.RecalculationDuration.TotalSeconds:F2}s. Error_Check = {result.WorkbookErrorCheckRaw}";

        DataInputs = new ObservableCollection<DataInputItem>(result.DataInputs);
        ChannelLocations = new ObservableCollection<ChannelLocationItem>(result.ChannelLocations);
        UnifiedChannels = new ObservableCollection<UnifiedChannel>(result.UnifiedChannels);
        SelectedUnifiedChannel = UnifiedChannels.FirstOrDefault();

        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(WorkbookPath));
        OnPropertyChanged(nameof(RecalculationDuration));
        OnPropertyChanged(nameof(CalculationState));
        OnPropertyChanged(nameof(ErrorCheckRaw));
        OnPropertyChanged(nameof(IsErrorCheckPassed));
        OnPropertyChanged(nameof(TotalDataInputs));
        OnPropertyChanged(nameof(TotalSheet1Parameters));
        OnPropertyChanged(nameof(TotalChannels));
        OnPropertyChanged(nameof(ValidChannelsCount));
        OnPropertyChanged(nameof(SkippedChannelsCount));
        OnPropertyChanged(nameof(TotalSegmentsCount));
        OnPropertyChanged(nameof(TotalHolesCount));
        OnPropertyChanged(nameof(DimensionCount));
        OnPropertyChanged(nameof(SuppressionCount));
        OnPropertyChanged(nameof(UnitlessCount));
        OnPropertyChanged(nameof(OtherCount));

        ApplySheet1Filter();
    }

    public void Clear()
    {
        Result = null;
        StatusMessage = "No calculator loaded.";
        DataInputs.Clear();
        FilteredSheet1Parameters.Clear();
        ChannelLocations.Clear();
        UnifiedChannels.Clear();
        SelectedChannelSegments.Clear();
        SelectedUnifiedChannel = null;

        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(WorkbookPath));
        OnPropertyChanged(nameof(RecalculationDuration));
        OnPropertyChanged(nameof(CalculationState));
        OnPropertyChanged(nameof(ErrorCheckRaw));
        OnPropertyChanged(nameof(IsErrorCheckPassed));
        OnPropertyChanged(nameof(TotalDataInputs));
        OnPropertyChanged(nameof(TotalSheet1Parameters));
        OnPropertyChanged(nameof(TotalChannels));
        OnPropertyChanged(nameof(ValidChannelsCount));
        OnPropertyChanged(nameof(SkippedChannelsCount));
        OnPropertyChanged(nameof(TotalSegmentsCount));
        OnPropertyChanged(nameof(TotalHolesCount));
        OnPropertyChanged(nameof(DimensionCount));
        OnPropertyChanged(nameof(SuppressionCount));
        OnPropertyChanged(nameof(UnitlessCount));
        OnPropertyChanged(nameof(OtherCount));
    }

    private void ApplySheet1Filter()
    {
        if (_result == null)
        {
            FilteredSheet1Parameters.Clear();
            return;
        }

        var query = _result.Sheet1Parameters.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(p =>
                p.ParameterName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.NormalizedName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Comment.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.DisplayedValue.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = SelectedCategory switch
        {
            "Dimensions" => query.Where(p => p.Category == ParameterCategory.Dimension),
            "Suppression" => query.Where(p => p.Category == ParameterCategory.SuppressionControl),
            "Counts" => query.Where(p => p.Category == ParameterCategory.UnitlessCount),
            "Other" => query.Where(p => p.Category is not (ParameterCategory.Dimension or ParameterCategory.SuppressionControl or ParameterCategory.UnitlessCount)),
            _ => query
        };

        FilteredSheet1Parameters = new ObservableCollection<Sheet1ParameterItem>(query);
    }

    private void UpdateSelectedChannelSegments()
    {
        if (SelectedUnifiedChannel != null)
        {
            SelectedChannelSegments = new ObservableCollection<ChannelLocationItem>(SelectedUnifiedChannel.Segments);
        }
        else
        {
            SelectedChannelSegments.Clear();
        }
    }
}
