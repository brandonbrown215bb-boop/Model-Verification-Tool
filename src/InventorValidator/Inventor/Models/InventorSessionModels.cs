namespace InventorValidator.Inventor.Models;

public record InventorProcessInfo(
    int ProcessId,
    string VersionString,
    int MajorVersion,
    bool IsApplicationOwned,
    DateTime StartTime
);

public class OccurrenceInventoryItem
{
    public string OccurrenceName { get; set; } = string.Empty;
    public string OccurrencePath { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
    public string DocumentPath { get; set; } = string.Empty;
    public bool IsSuppressed { get; set; }
    public bool IsActive => !IsSuppressed;
    public string StateText => IsActive ? "Active" : "Suppressed";
    public int DepthLevel { get; set; }
    public double[]? TransformMatrix { get; set; }
    public List<OccurrenceInventoryItem> Children { get; set; } = new();
}

public class InventorParameterItem
{
    public string Name { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string DocumentPath { get; set; } = string.Empty;
    public string ParameterType { get; set; } = "Model"; // "Model", "User", "Reference"
    public string Expression { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Units { get; set; } = string.Empty;
    public bool IsFormulaDriven { get; set; }
    public bool IsReference { get; set; }
    public bool IsLiteral => !IsFormulaDriven && !IsReference;
    public string? Comment { get; set; }
}

public class InventorFeatureItem
{
    public string FeatureName { get; set; } = string.Empty;
    public string FeatureType { get; set; } = string.Empty; // "HoleFeature", "RectangularPatternFeature", "WorkPlane", etc.
    public string ContainingOccurrence { get; set; } = string.Empty;
    public string DocumentPath { get; set; } = string.Empty;
    public int ElementCount { get; set; } = 1;
    public double? HoleDiameter { get; set; }
    public bool IsSuppressed { get; set; }
}

public class InventorRepresentationItem
{
    public string Name { get; set; } = string.Empty;
    public string RepresentationType { get; set; } = "LOD"; // "LOD", "ModelState", "DesignView", "Positional"
    public bool IsActive { get; set; }
}

public class AssemblyInventoryResult
{
    public string TopAssemblyPath { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;
    public string InventorVersion { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ActiveRepresentation { get; set; } = string.Empty;
    public bool HasILogicRepresentation { get; set; }
    public int TotalDocumentsCount { get; set; }
    public int TotalOccurrencesCount { get; set; }
    public int ActiveOccurrencesCount { get; set; }
    public int SuppressedOccurrencesCount { get; set; }

    public List<OccurrenceInventoryItem> Occurrences { get; set; } = new();
    public List<InventorParameterItem> Parameters { get; set; } = new();
    public List<InventorFeatureItem> Features { get; set; } = new();
    public List<InventorRepresentationItem> Representations { get; set; } = new();
    public TimeSpan ExtractionDuration { get; set; }
}
