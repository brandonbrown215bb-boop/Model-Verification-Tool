using System;
using System.Collections.Generic;

namespace InventorValidator.Geometry.Models;

/// <summary>
/// Lightweight 3D point in inches.
/// </summary>
public readonly record struct Point3D(double X, double Y, double Z)
{
    public static readonly Point3D Zero = new(0, 0, 0);

    public double DistanceTo(Point3D other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        double dz = Z - other.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public double Distance2D(double otherA, double otherB, bool isFloorOrRoof)
    {
        // For Floor/Roof, A=X, B=Z. For Walls, A=Y, B=Z.
        double primary = isFloorOrRoof ? X : Y;
        double da = primary - otherA;
        double dz = Z - otherB;
        return Math.Sqrt(da * da + dz * dz);
    }

    public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";
}

/// <summary>
/// Lightweight 3D unit vector for cylinder/hole axes.
/// </summary>
public readonly record struct Vector3D(double X, double Y, double Z)
{
    public static readonly Vector3D UnitX = new(1, 0, 0);
    public static readonly Vector3D UnitY = new(0, 1, 0);
    public static readonly Vector3D UnitZ = new(0, 0, 1);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vector3D Normalize()
    {
        double len = Length;
        return len > 1e-9 ? new Vector3D(X / len, Y / len, Z / len) : new Vector3D(0, 0, 0);
    }

    public double Dot(Vector3D other) => X * other.X + Y * other.Y + Z * other.Z;

    public bool IsCollinear(Vector3D other, double angularToleranceDegrees = 1.0)
    {
        Vector3D n1 = Normalize();
        Vector3D n2 = other.Normalize();
        double dot = Math.Abs(n1.Dot(n2));
        double threshold = Math.Cos(angularToleranceDegrees * Math.PI / 180.0);
        return dot >= threshold;
    }

    public override string ToString() => $"[{X:F4}, {Y:F4}, {Z:F4}]";
}

/// <summary>
/// Specifies which coordinate axes are authoritatively dictated by the engineering workbook.
/// </summary>
public enum AuthoritativeAxisPair
{
    /// <summary>Floor and Roof channels: X (array position along width/length) and Z (depth from Segment Start).</summary>
    XZ,
    /// <summary>South and North Wall channels: Y (array position along height) and Z (depth from Segment Start).</summary>
    YZ
}

/// <summary>
/// Status of an individual hole match.
/// </summary>
public enum HoleMatchStatus
{
    /// <summary>Error &lt;= MatchTolerance (default 0.010 in).</summary>
    Match,
    /// <summary>MatchTolerance &lt; Error &lt;= WarningTolerance (default 0.031 in / 1/32 in).</summary>
    Warning,
    /// <summary>Error &gt; WarningTolerance (&gt; 0.031 in) or misaligned coordinate.</summary>
    Mislocated,
    /// <summary>Expected hole defined in Excel was not found in the 3D model.</summary>
    MissingExpected,
    /// <summary>Physical hole in 3D CAD that did not pair with any expected Excel hole (informational).</summary>
    ExtraActual,
    /// <summary>Excel row was skipped due to template noise (#REF!, Qty &lt;= 0, etc.).</summary>
    SkippedInvalidRow
}

/// <summary>
/// Expected hole derived from Channel Loc worksheet parameters.
/// </summary>
public class ExpectedHole
{
    public string ChannelGroup { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public int SourceRowIndex { get; set; }
    public int HoleIndex { get; set; }
    public string ReferencedPart { get; set; } = string.Empty;
    public Point3D ExpectedPosition { get; set; }
    public AuthoritativeAxisPair AuthoritativeAxes { get; set; }
    public double? ExpectedDiameter { get; set; }
    public bool IsDiameterInferred { get; set; }
    public string ArrayAxis { get; set; } = "X";
    public double ArrayOffset { get; set; }
    public double ArraySpacing { get; set; }
    public int ArrayQuantity { get; set; }

    public double AuthoritativeCoordinate => AuthoritativeAxes == AuthoritativeAxisPair.XZ ? ExpectedPosition.X : ExpectedPosition.Y;
    public double ExpectedZ => ExpectedPosition.Z;

    public override string ToString() => $"{ChannelName}[{HoleIndex}] @ {ExpectedPosition} (Part: {ReferencedPart})";
}

/// <summary>
/// Physical hole extracted from the 3D CAD assembly.
/// </summary>
public class ActualHole
{
    public int Id { get; set; }
    public Point3D Position { get; set; }
    public Vector3D Axis { get; set; }
    public double Diameter { get; set; }
    public string ParentOccurrenceName { get; set; } = string.Empty;
    public string ParentPartNumber { get; set; } = string.Empty;
    public string FeatureName { get; set; } = string.Empty;
    public int CoaxialFaceCount { get; set; } = 1;
    public List<Point3D> RawFaceCenters { get; set; } = new();

    public override string ToString() => $"Hole #{Id}: D={Diameter:F3}\" @ {Position} on {ParentPartNumber}";
}

/// <summary>
/// Detailed result of pairing an ExpectedHole with an ActualHole.
/// </summary>
public class HoleMatchResult
{
    public ExpectedHole? Expected { get; set; }
    public ActualHole? Actual { get; set; }
    public HoleMatchStatus Status { get; set; } = HoleMatchStatus.MissingExpected;

    /// <summary>Delta along authoritative array axis: DeltaX for Floor/Roof, DeltaY for Walls.</summary>
    public double DeltaAxis { get; set; }

    /// <summary>Delta in Z elevation from Segment Start.</summary>
    public double DeltaZ { get; set; }

    /// <summary>Distance over authoritative worksheet dimensions: sqrt(DeltaAxis^2 + DeltaZ^2).</summary>
    public double AuthoritativeError { get; set; }

    /// <summary>Diagnostic delta on the unconstrained transverse axis (DeltaY for Floor/Roof, DeltaX for Walls).</summary>
    public double TransverseDelta { get; set; }

    /// <summary>Full 3D Euclidean distance sqrt(dx^2 + dy^2 + dz^2).</summary>
    public double Total3DDistance { get; set; }

    /// <summary>Difference between actual diameter and expected diameter (if known).</summary>
    public double? DiameterDelta { get; set; }

    public string Confidence { get; set; } = "Confirmed";
    public string Notes { get; set; } = string.Empty;

    public string ChannelGroup => Expected?.ChannelGroup ?? (Actual != null ? "Extra CAD" : "Unknown");
    public string ChannelName => Expected?.ChannelName ?? "—";
    public int HoleIndex => Expected?.HoleIndex ?? -1;
    public string ReferencedPart => Expected?.ReferencedPart ?? Actual?.ParentPartNumber ?? "—";
    public string OwningPartNumber => Actual?.ParentPartNumber ?? "—";
    public string OwningOccurrence => Actual?.ParentOccurrenceName ?? "—";
}

/// <summary>
/// Statistical summary for an entire channel array pattern.
/// </summary>
public class PatternDiagnosticResult
{
    public string ChannelGroup { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string ReferencedPart { get; set; } = string.Empty;
    public int ExpectedCount { get; set; }
    public int MatchedCount { get; set; }
    public int MissingCount { get; set; }
    public double ExpectedSpacing { get; set; }
    public double ActualAverageSpacing { get; set; }
    public double SpacingStdDev { get; set; }
    public double MaxSpacingDeviation { get; set; }
    public double FirstHoleOffset { get; set; }

    /// <summary>Average signed error along authoritative axis.</summary>
    public double MeanSignedAxisDelta { get; set; }
    public double AxisDeltaStdDev { get; set; }

    /// <summary>Average signed error in Z.</summary>
    public double MeanSignedZDelta { get; set; }
    public double ZDeltaStdDev { get; set; }

    /// <summary>True when spacing is consistent but the entire pattern has a uniform shift.</summary>
    public bool IsSystematicShift { get; set; }

    public string DiagnosticMessage { get; set; } = string.Empty;
}

/// <summary>
/// Aggregated geometry validation results across all channel groups.
/// </summary>
public class GeometryValidationResult
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string SegmentStartReferenceName { get; set; } = "Assembly Z=0";
    public double SegmentStartZOffset { get; set; }

    public int TotalHolesExpected { get; set; }
    public int MatchedCount { get; set; }
    public int WarningCount { get; set; }
    public int FailureCount { get; set; }
    public int MissingCount { get; set; }
    public int ExtraCount { get; set; }
    public int SkippedCount { get; set; }

    public List<PatternDiagnosticResult> ChannelDiagnostics { get; set; } = new();
    public List<HoleMatchResult> Results { get; set; } = new();
    public List<ActualHole> UnmatchedActualHoles { get; set; } = new();
}
