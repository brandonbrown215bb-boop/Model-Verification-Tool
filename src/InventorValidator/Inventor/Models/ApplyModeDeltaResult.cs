namespace InventorValidator.Inventor.Models;

public class ParameterDelta
{
    public string ParameterName { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public double BeforeValue { get; set; }
    public double AfterValue { get; set; }
    public double Delta => AfterValue - BeforeValue;
    public string BeforeExpression { get; set; } = string.Empty;
    public string AfterExpression { get; set; } = string.Empty;
    public string Units { get; set; } = string.Empty;
    public bool HasChanged => Math.Abs(Delta) > 1e-6 || !string.Equals(BeforeExpression, AfterExpression, StringComparison.Ordinal);
}

public class SuppressionDelta
{
    public string OccurrenceName { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public bool BeforeSuppressed { get; set; }
    public bool AfterSuppressed { get; set; }
    public bool HasStateChanged => BeforeSuppressed != AfterSuppressed;
    public string TransitionText => (BeforeSuppressed, AfterSuppressed) switch
    {
        (true, false) => "Unsuppressed (Turned ON)",
        (false, true) => "Suppressed (Turned OFF)",
        (false, false) => "Remained Active",
        (true, true) => "Remained Suppressed"
    };
}

public class ApplyModeDeltaResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExecutedRuleName { get; set; }
    public bool RuleExecutedSuccessfully { get; set; }
    public string? RuleMessage { get; set; }
    public TimeSpan Duration { get; set; }

    public List<ParameterDelta> ParameterDeltas { get; set; } = new();
    public List<SuppressionDelta> SuppressionDeltas { get; set; } = new();

    public int ChangedParametersCount => ParameterDeltas.Count(p => p.HasChanged);
    public int ChangedSuppressionsCount => SuppressionDeltas.Count(s => s.HasStateChanged);
    public int UnsuppressedCount => SuppressionDeltas.Count(s => s.BeforeSuppressed && !s.AfterSuppressed);
    public int SuppressedCount => SuppressionDeltas.Count(s => !s.BeforeSuppressed && s.AfterSuppressed);
}
