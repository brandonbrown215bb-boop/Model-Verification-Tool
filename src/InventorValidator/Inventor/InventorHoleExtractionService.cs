using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using InventorValidator.Geometry;
using InventorValidator.Geometry.Models;
using InventorValidator.Infrastructure;

namespace InventorValidator.Inventor;

/// <summary>
/// Result of hole extraction from Inventor CAD assembly.
/// </summary>
public class InventorHoleExtractionResult
{
    public List<ActualHole> Holes { get; set; } = new();
    public string SegmentStartReferenceName { get; set; } = "Assembly Z=0";
    public double SegmentStartZOffset { get; set; }
    public TimeSpan Duration { get; set; }
    public int RawFaceCount { get; set; }
    public int MergedHoleCount => Holes.Count;
}

/// <summary>
/// Extracts physical hole geometry and Segment Start reference planes from an active Inventor AssemblyDocument.
/// </summary>
public class InventorHoleExtractionService
{
    private const double CmToInches = 1.0 / 2.54;
    public const int CylinderSurfaceType = 5891; // SurfaceTypeEnum.kCylinderSurface (kPlaneSurface is 5890)

    /// <summary>
    /// Extracts all physical holes from unsuppressed components in the assembly.
    /// </summary>
    public InventorHoleExtractionResult ExtractHoles(
        dynamic asmDoc,
        ComReleaseScope scope,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new InventorHoleExtractionResult();

        progress?.Report("Detecting Segment Start reference datum...");
        dynamic compDef = scope.Track<object>(asmDoc.ComponentDefinition);

        // 1. Detect Segment Start Reference Plane
        DetectSegmentStartPlane(compDef, result, scope);

        cancellationToken.ThrowIfCancellationRequested();

        // 2. Traverse unsuppressed occurrences and query cylindrical B-Rep faces
        progress?.Report("Scanning assembly components for hole geometry...");
        var rawHoles = new List<ActualHole>();
        int nextRawId = 1;

        dynamic occurrences = scope.Track<object>(compDef.Occurrences);
        int occCount = occurrences.Count;

        for (int i = 1; i <= occCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic occ = scope.Track<object>(occurrences.Item[i]);

            bool isSuppressed = false;
            try { isSuppressed = (bool)occ.Suppressed; } catch { }
            if (isSuppressed) continue;

            ExtractOccHolesRecursive(occ, rawHoles, ref nextRawId, scope, cancellationToken);
        }

        result.RawFaceCount = rawHoles.Count;
        progress?.Report($"Deduplicating {rawHoles.Count} candidate hole faces...");

        // 3. Coaxial through-hole deduplication
        result.Holes = CoaxialHoleDeduplicator.Deduplicate(rawHoles);
        result.Duration = sw.Elapsed;

        DiagnosticsLogger.Instance.Success(
            $"Hole extraction complete: {result.RawFaceCount} raw faces deduplicated into {result.MergedHoleCount} physical holes " +
            $"(Segment Start: '{result.SegmentStartReferenceName}' @ Z={result.SegmentStartZOffset:F4}\") in {sw.Elapsed.TotalSeconds:F2}s");

        return result;
    }

    private void DetectSegmentStartPlane(dynamic compDef, InventorHoleExtractionResult result, ComReleaseScope scope)
    {
        try
        {
            dynamic workPlanes = scope.Track<object>(compDef.WorkPlanes);
            int wpCount = workPlanes.Count;

            for (int i = 1; i <= wpCount; i++)
            {
                dynamic wp = scope.Track<object>(workPlanes.Item[i]);
                string name = (string)wp.Name;

                if (name.IndexOf("Seg Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Segment Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Bhd Loc", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    dynamic plane = scope.Track<object>(wp.Plane);
                    dynamic rootPt = scope.Track<object>(plane.RootPoint);
                    double zCm = (double)rootPt.Z;
                    result.SegmentStartZOffset = Math.Round(zCm * CmToInches, 4);
                    result.SegmentStartReferenceName = name;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not read WorkPlanes for Segment Start: {ex.Message}");
        }

        result.SegmentStartReferenceName = "Assembly Z=0";
        result.SegmentStartZOffset = 0.0;
    }

    private void ExtractOccHolesRecursive(
        dynamic occ,
        List<ActualHole> rawHoles,
        ref int nextRawId,
        ComReleaseScope scope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string occName = (string)occ.Name;
        string partNum = ExtractPartNumber(occName);

        // Check if occurrence is a subassembly
        bool hasSubOccs = false;
        try
        {
            dynamic subOccs = scope.Track<object>(occ.SubOccurrences);
            int count = subOccs.Count;
            if (count > 0)
            {
                hasSubOccs = true;
                for (int s = 1; s <= count; s++)
                {
                    dynamic subOcc = scope.Track<object>(subOccs.Item[s]);
                    bool isSuppressed = false;
                    try { isSuppressed = (bool)subOcc.Suppressed; } catch { }
                    if (!isSuppressed)
                    {
                        ExtractOccHolesRecursive(subOcc, rawHoles, ref nextRawId, scope, cancellationToken);
                    }
                }
            }
        }
        catch { }

        if (hasSubOccs) return; // Leaf occurrences only

        // Query SurfaceBodies on occurrence (already in assembly coordinate context)
        try
        {
            dynamic bodies = scope.Track<object>(occ.SurfaceBodies);
            int bodyCount = bodies.Count;

            for (int b = 1; b <= bodyCount; b++)
            {
                dynamic body = scope.Track<object>(bodies.Item[b]);
                dynamic faces = scope.Track<object>(body.Faces);
                int faceCount = faces.Count;

                for (int f = 1; f <= faceCount; f++)
                {
                    dynamic face = scope.Track<object>(faces.Item[f]);
                    int st = 0;
                    try { st = Convert.ToInt32(face.SurfaceType); } catch { }

                    bool isCylinder = (st == CylinderSurfaceType);
                    if (!isCylinder)
                    {
                        try
                        {
                            string? typeStr = face.SurfaceType?.ToString();
                            if (typeStr != null && typeStr.IndexOf("Cylinder", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                isCylinder = true;
                            }
                        }
                        catch { }
                    }

                    if (isCylinder)
                    {
                        try
                        {
                            dynamic cyl = scope.Track<object>(face.Geometry);
                            double radiusCm = (double)cyl.Radius;
                            double diaIn = Math.Round((radiusCm * 2.0) * CmToInches, 4);

                            // Bolt holes are typically between 0.05" and 2.0"
                            if (diaIn >= 0.050 && diaIn <= 2.000)
                            {
                                dynamic basePt = scope.Track<object>(cyl.BasePoint);
                                double xIn = ((double)basePt.X) * CmToInches;
                                double yIn = ((double)basePt.Y) * CmToInches;
                                double zIn = ((double)basePt.Z) * CmToInches;

                                dynamic axisVec = scope.Track<object>(cyl.AxisVector);
                                double ax = (double)axisVec.X;
                                double ay = (double)axisVec.Y;
                                double az = (double)axisVec.Z;

                                rawHoles.Add(new ActualHole
                                {
                                    Id = nextRawId++,
                                    Position = new Point3D(Math.Round(xIn, 4), Math.Round(yIn, 4), Math.Round(zIn, 4)),
                                    Axis = new Vector3D(ax, ay, az),
                                    Diameter = diaIn,
                                    ParentOccurrenceName = occName,
                                    ParentPartNumber = partNum,
                                    FeatureName = "CylinderFace"
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            DiagnosticsLogger.Instance.Info($"Failed extracting cylinder geometry on '{occName}': {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not extract bodies from occurrence '{occName}': {ex.Message}");
        }
    }

    private static string ExtractPartNumber(string occName)
    {
        if (string.IsNullOrEmpty(occName)) return string.Empty;
        int colonIdx = occName.LastIndexOf(':');
        return colonIdx > 0 ? occName.Substring(0, colonIdx) : occName;
    }
}
