namespace InventorValidator.Inventor.Models;

public enum MatchClassification
{
    UniqueExactMatch,
    UniqueNormalizedMatch,
    AmbiguousMatch,
    NoMatch,
    ReadOnlyMatch,
    ExpressionDrivenMatch,
    SuppressionMatch,
    InformationalOnly
}

public enum ComparisonStatus
{
    Match,
    Discrepancy,
    MissingInModel,
    NotApplicable
}

public enum SafeWriteClassification
{
    SafeToWrite,
    ReadOnlyFormula,
    ReadOnlyReference,
    AmbiguousTarget,
    NormalizedRequiresApproval,
    IncompatibleUnits,
    Informational,
    NotApplicable
}

public class ParameterComparisonRow
{
    public string ExcelParameterName { get; set; } = string.Empty;
    public string ExcelValueRaw { get; set; } = string.Empty;
    public double? ExcelNumericValue { get; set; }
    public string ExcelUnit { get; set; } = string.Empty;
    public string ExcelCategory { get; set; } = string.Empty;

    public string? TargetDocument { get; set; }
    public string? TargetParameterName { get; set; }
    public string? TargetOccurrenceName { get; set; }
    public string? ModelExpression { get; set; }
    public double? ModelNumericValue { get; set; }
    public string? ModelUnits { get; set; }
    public bool? ModelSuppressionState { get; set; } // true = Active (1), false = Suppressed (0)

    public MatchClassification MatchType { get; set; } = MatchClassification.NoMatch;
    public SafeWriteClassification WritePolicy { get; set; } = SafeWriteClassification.NotApplicable;
    public ComparisonStatus Status { get; set; } = ComparisonStatus.NotApplicable;
    public string Notes { get; set; } = string.Empty;
    public double? Delta { get; set; }
}

public class ParameterComparisonResult
{
    public List<ParameterComparisonRow> Rows { get; set; } = new();
    public int TotalRows => Rows.Count;
    public int MatchedCount => Rows.Count(r => r.Status == ComparisonStatus.Match);
    public int DiscrepancyCount => Rows.Count(r => r.Status == ComparisonStatus.Discrepancy);
    public int MissingInModelCount => Rows.Count(r => r.Status == ComparisonStatus.MissingInModel);
    public int SafeToWriteCount => Rows.Count(r => r.WritePolicy == SafeWriteClassification.SafeToWrite);
    public int ProtectedFormulaCount => Rows.Count(r => r.WritePolicy == SafeWriteClassification.ReadOnlyFormula);
}
