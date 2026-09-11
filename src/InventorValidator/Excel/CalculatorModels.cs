namespace InventorValidator.Excel;

public enum ParameterCategory
{
    Dimension,
    SuppressionControl,
    UnitlessCount,
    FeatureControl,
    Property,
    Informational,
    Unknown
}

public enum ChannelStatus
{
    Valid,
    SkippedInvalidRow
}

public class DataInputItem
{
    public int RowIndex { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public string DisplayedValue { get; set; } = string.Empty;
    public object? RawValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public string AllowedValues { get; set; } = string.Empty;
    public string ErrorCheckValue { get; set; } = string.Empty;
    public bool HasError { get; set; }
}

public class Sheet1ParameterItem
{
    public int RowIndex { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public object? RawValue { get; set; }
    public string DisplayedValue { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string? FormulaError { get; set; }
    public ParameterCategory Category { get; set; } = ParameterCategory.Unknown;
    public bool IsSafeToWrite { get; set; }
}

public class ChannelLocationItem
{
    public int RowIndex { get; set; }
    public string ChannelGroup { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string Axis { get; set; } = string.Empty;
    public double? Offset { get; set; }
    public double? Spacing { get; set; }
    public int? Quantity { get; set; }
    public double? ZLocation { get; set; }
    public string ReferencedPart { get; set; } = string.Empty;
    public ChannelStatus Status { get; set; } = ChannelStatus.Valid;
    public string? Notes { get; set; }

    /// <summary>
    /// Generates the expected 1D positions along the primary array axis:
    /// Position[i] = Offset + i * Spacing for i = 0 .. Quantity - 1.
    /// </summary>
    public IReadOnlyList<double> GenerateExpectedPositions()
    {
        if (Status != ChannelStatus.Valid || !Offset.HasValue || !Spacing.HasValue || !Quantity.HasValue || Quantity.Value <= 0)
        {
            return Array.Empty<double>();
        }

        var positions = new double[Quantity.Value];
        for (int i = 0; i < Quantity.Value; i++)
        {
            positions[i] = Offset.Value + (i * Spacing.Value);
        }
        return positions;
    }
}

public class UnifiedChannel
{
    public string ChannelName { get; set; } = string.Empty;
    public double ZLocation { get; set; }
    public string SidesSummary { get; set; } = string.Empty;
    public int SideCount { get; set; }
    public int SegmentCount => Segments.Count;
    public int TotalHoles => Segments.Sum(s => s.Quantity ?? 0);
    public string ReferencedPartsSummary { get; set; } = string.Empty;
    public ChannelStatus Status { get; set; } = ChannelStatus.Valid;
    public List<ChannelLocationItem> Segments { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
}

public class CalculatorSessionResult
{
    public string WorkbookPath { get; set; } = string.Empty;
    public TimeSpan RecalculationDuration { get; set; }
    public string CalculationState { get; set; } = "xlDone";
    public double? WorkbookErrorCheckValue { get; set; }
    public string WorkbookErrorCheckRaw { get; set; } = string.Empty;
    public bool IsErrorCheckPassed { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public List<DataInputItem> DataInputs { get; set; } = new();
    public List<Sheet1ParameterItem> Sheet1Parameters { get; set; } = new();
    public List<ChannelLocationItem> ChannelLocations { get; set; } = new();
    public List<UnifiedChannel> UnifiedChannels { get; set; } = new();

    // Summary counters
    public int TotalDataInputs => DataInputs.Count;
    public int TotalSheet1Parameters => Sheet1Parameters.Count;
    public int TotalChannels => UnifiedChannels.Count > 0 ? UnifiedChannels.Count : ChannelLocations.Count;
    public int ValidChannelsCount => UnifiedChannels.Count > 0 
        ? UnifiedChannels.Count(c => c.Status == ChannelStatus.Valid)
        : ChannelLocations.Count(c => c.Status == ChannelStatus.Valid);
    public int SkippedChannelsCount => ChannelLocations.Count(c => c.Status == ChannelStatus.SkippedInvalidRow);
    public int TotalSegmentsCount => ChannelLocations.Count(c => c.Status == ChannelStatus.Valid);
    public int TotalHolesCount => UnifiedChannels.Sum(c => c.TotalHoles);

    public int DimensionCount => Sheet1Parameters.Count(p => p.Category == ParameterCategory.Dimension);
    public int SuppressionCount => Sheet1Parameters.Count(p => p.Category == ParameterCategory.SuppressionControl);
    public int UnitlessCount => Sheet1Parameters.Count(p => p.Category == ParameterCategory.UnitlessCount);
    public int FeatureControlCount => Sheet1Parameters.Count(p => p.Category == ParameterCategory.FeatureControl);
    public int OtherCount => Sheet1Parameters.Count(p => p.Category is ParameterCategory.Property or ParameterCategory.Informational or ParameterCategory.Unknown);
}
