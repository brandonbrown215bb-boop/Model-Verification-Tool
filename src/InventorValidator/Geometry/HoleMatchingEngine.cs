using System;
using System.Collections.Generic;
using System.Linq;
using InventorValidator.Geometry.Models;

namespace InventorValidator.Geometry;

/// <summary>
/// Configuration parameters for geometry comparison.
/// </summary>
public class GeometryComparisonOptions
{
    public double MatchTolerance { get; set; } = 0.010;
    public double WarningTolerance { get; set; } = 0.031;
    public double SearchRadius { get; set; } = 2.0;
    public double TransverseTolerance { get; set; } = 1.0;
    public double DiameterTolerance { get; set; } = 0.005;
}

/// <summary>
/// Matches expected holes from Channel Loc against physical CAD holes using global optimal assignment.
/// </summary>
public class HoleMatchingEngine
{
    private readonly GeometryComparisonOptions _options;

    public HoleMatchingEngine(GeometryComparisonOptions? options = null)
    {
        _options = options ?? new GeometryComparisonOptions();
    }

    /// <summary>
    /// Executes geometry validation across all expected and actual holes.
    /// </summary>
    public GeometryValidationResult Validate(
        IReadOnlyList<ExpectedHole> expectedHoles,
        IReadOnlyList<ActualHole> actualHoles,
        string segmentStartRefName = "Assembly Z=0",
        double segmentStartZOffset = 0.0)
    {
        var result = new GeometryValidationResult
        {
            SegmentStartReferenceName = segmentStartRefName,
            SegmentStartZOffset = segmentStartZOffset,
            TotalHolesExpected = expectedHoles.Count
        };

        var usedActualHoleIds = new HashSet<int>();
        var channelGroups = expectedHoles
            .GroupBy(h => (h.ChannelGroup, h.ChannelName))
            .OrderBy(g => g.Key.ChannelGroup)
            .ThenBy(g => g.Key.ChannelName);

        foreach (var group in channelGroups)
        {
            var expList = group.OrderBy(h => h.HoleIndex).ToList();
            var first = expList[0];
            bool isFloorOrRoof = first.AuthoritativeAxes == AuthoritativeAxisPair.XZ;

            // 1. Spatial bounding box filter for candidate actual holes
            double minAuth = expList.Min(h => h.AuthoritativeCoordinate) - _options.SearchRadius;
            double maxAuth = expList.Max(h => h.AuthoritativeCoordinate) + _options.SearchRadius;
            double expZ = first.ExpectedZ;
            double minZ = expZ - _options.SearchRadius;
            double maxZ = expZ + _options.SearchRadius;

            // Transverse proximity: candidates must be in the general vicinity of the channel group (within 12 inches)
            double expTrans = isFloorOrRoof ? first.ExpectedPosition.Y : first.ExpectedPosition.X;
            double maxTransverseVicinity = 12.0;

            var candidates = actualHoles.Where(a =>
            {
                double authCoord = isFloorOrRoof ? a.Position.X : a.Position.Y;
                double transCoord = isFloorOrRoof ? a.Position.Y : a.Position.X;
                bool nearTransverse = expTrans == 0.0 && !first.ChannelGroup.ToUpperInvariant().Contains("SOUTH") && !first.ChannelGroup.ToUpperInvariant().Contains("ROOF")
                    ? transCoord <= maxTransverseVicinity // North wall or floor: X or Y should be near 0
                    : Math.Abs(transCoord - expTrans) <= maxTransverseVicinity;

                return authCoord >= minAuth && authCoord <= maxAuth &&
                       a.Position.Z >= minZ && a.Position.Z <= maxZ &&
                       nearTransverse;
            }).ToList();

            // 2. Resolve/infer expected hole diameter
            double? inferredDiameter = ResolveDominantDiameter(expList, candidates);
            foreach (var eh in expList)
            {
                if (!eh.ExpectedDiameter.HasValue && inferredDiameter.HasValue)
                {
                    eh.ExpectedDiameter = inferredDiameter;
                    eh.IsDiameterInferred = true;
                }
            }

            // 3. Build Cost Matrix: N (expected) x M (candidates)
            int n = expList.Count;
            int m = candidates.Count;
            var channelResults = new List<HoleMatchResult>();

            if (m == 0)
            {
                // No candidates found in this zone -> all holes missing
                foreach (var eh in expList)
                {
                    var matchRes = new HoleMatchResult
                    {
                        Expected = eh,
                        Actual = null,
                        Status = HoleMatchStatus.MissingExpected,
                        Notes = "No candidate holes found in target coordinate volume."
                    };
                    channelResults.Add(matchRes);
                }
            }
            else
            {
                double[,] costMatrix = new double[n, m];
                for (int i = 0; i < n; i++)
                {
                    var eh = expList[i];
                    for (int j = 0; j < m; j++)
                    {
                        var ah = candidates[j];
                        costMatrix[i, j] = ComputeAssignmentCost(eh, ah, isFloorOrRoof, inferredDiameter);
                    }
                }

                // 4. Solve optimal assignment using Hungarian algorithm
                int[] assignments = HungarianMatcher.FindAssignments(costMatrix);
                var assignedActualsInChannel = new HashSet<int>();

                for (int i = 0; i < n; i++)
                {
                    var eh = expList[i];
                    int assignedIdx = assignments[i];

                    if (assignedIdx >= 0 && assignedIdx < m)
                    {
                        var ah = candidates[assignedIdx];
                        double cost = costMatrix[i, assignedIdx];

                        // Accept assignment only if within search radius
                        if (cost < _options.SearchRadius * 2.0)
                        {
                            usedActualHoleIds.Add(ah.Id);
                            assignedActualsInChannel.Add(ah.Id);

                            var matchRes = CreateMatchResult(eh, ah, isFloorOrRoof);
                            channelResults.Add(matchRes);
                            continue;
                        }
                    }

                    // Not assigned or cost exceeds search radius
                    channelResults.Add(new HoleMatchResult
                    {
                        Expected = eh,
                        Actual = null,
                        Status = HoleMatchStatus.MissingExpected,
                        Notes = "No matching hole found within search tolerance."
                    });
                }
            }

            // 5. Pattern-level diagnostics for this channel
            var diag = PatternDiagnosticAnalyzer.AnalyzeChannel(
                first.ChannelGroup,
                first.ChannelName,
                first.ReferencedPart,
                expList.Count,
                first.ArraySpacing,
                first.ArrayOffset,
                channelResults);

            result.ChannelDiagnostics.Add(diag);
            result.Results.AddRange(channelResults);
        }

        // 6. Identify extra CAD holes (holes in assembly that weren't assigned to any channel)
        var unusedHoles = actualHoles.Where(a => !usedActualHoleIds.Contains(a.Id)).ToList();
        result.UnmatchedActualHoles = unusedHoles;

        foreach (var extra in unusedHoles)
        {
            result.Results.Add(new HoleMatchResult
            {
                Expected = null,
                Actual = extra,
                Status = HoleMatchStatus.ExtraActual,
                Notes = $"Hole #{extra.Id} (D={extra.Diameter:F3}\") on {extra.ParentPartNumber} not associated with any expected channel."
            });
        }

        // 7. Calculate summary counts
        result.MatchedCount = result.Results.Count(r => r.Status == HoleMatchStatus.Match);
        result.WarningCount = result.Results.Count(r => r.Status == HoleMatchStatus.Warning);
        result.FailureCount = result.Results.Count(r => r.Status == HoleMatchStatus.Mislocated);
        result.MissingCount = result.Results.Count(r => r.Status == HoleMatchStatus.MissingExpected);
        result.ExtraCount = result.Results.Count(r => r.Status == HoleMatchStatus.ExtraActual);
        result.SkippedCount = result.Results.Count(r => r.Status == HoleMatchStatus.SkippedInvalidRow);

        return result;
    }

    private double ComputeAssignmentCost(ExpectedHole eh, ActualHole ah, bool isFloorOrRoof, double? targetDia)
    {
        double actualAuth = isFloorOrRoof ? ah.Position.X : ah.Position.Y;
        double dAxis = actualAuth - eh.AuthoritativeCoordinate;
        double dZ = ah.Position.Z - eh.ExpectedZ;
        double authError = Math.Sqrt(dAxis * dAxis + dZ * dZ);

        if (authError > _options.SearchRadius)
        {
            return double.PositiveInfinity;
        }

        // Transverse distance check: candidate must lie along the channel's mounting plane
        double actualTrans = isFloorOrRoof ? ah.Position.Y : ah.Position.X;
        double expTrans = isFloorOrRoof ? eh.ExpectedPosition.Y : eh.ExpectedPosition.X;
        double dTrans = Math.Abs(actualTrans - expTrans);

        if (dTrans > _options.TransverseTolerance)
        {
            return double.PositiveInfinity;
        }

        double cost = authError + (0.01 * dTrans);

        // Diameter mismatch penalty
        if (targetDia.HasValue && Math.Abs(ah.Diameter - targetDia.Value) > _options.DiameterTolerance)
        {
            cost += 0.25; // mild penalty to prioritize correct diameter if available
        }

        return cost;
    }

    private HoleMatchResult CreateMatchResult(ExpectedHole eh, ActualHole ah, bool isFloorOrRoof)
    {
        double actualAuth = isFloorOrRoof ? ah.Position.X : ah.Position.Y;
        double dAxis = actualAuth - eh.AuthoritativeCoordinate;
        double dZ = ah.Position.Z - eh.ExpectedZ;
        double authError = Math.Sqrt(dAxis * dAxis + dZ * dZ);

        double actualTrans = isFloorOrRoof ? ah.Position.Y : ah.Position.X;
        double expTrans = isFloorOrRoof ? eh.ExpectedPosition.Y : eh.ExpectedPosition.X;
        double dTrans = actualTrans - expTrans;

        double total3D = eh.ExpectedPosition.DistanceTo(ah.Position);
        double? diaDelta = eh.ExpectedDiameter.HasValue ? Math.Round(ah.Diameter - eh.ExpectedDiameter.Value, 4) : null;

        HoleMatchStatus status;
        if (authError <= _options.MatchTolerance)
        {
            status = HoleMatchStatus.Match;
        }
        else if (authError <= _options.WarningTolerance)
        {
            status = HoleMatchStatus.Warning;
        }
        else
        {
            status = HoleMatchStatus.Mislocated;
        }

        return new HoleMatchResult
        {
            Expected = eh,
            Actual = ah,
            Status = status,
            DeltaAxis = Math.Round(dAxis, 4),
            DeltaZ = Math.Round(dZ, 4),
            AuthoritativeError = Math.Round(authError, 4),
            TransverseDelta = Math.Round(dTrans, 4),
            Total3DDistance = Math.Round(total3D, 4),
            DiameterDelta = diaDelta,
            Confidence = "Confirmed",
            Notes = status == HoleMatchStatus.Match
                ? "Position verified within tolerance."
                : $"Authoritative deviation: {authError:F4}\" (Axis: {dAxis:F4}\", Z: {dZ:F4}\")"
        };
    }

    private double? ResolveDominantDiameter(List<ExpectedHole> expected, List<ActualHole> candidates)
    {
        if (expected.Any(h => h.ExpectedDiameter.HasValue))
        {
            return expected.First(h => h.ExpectedDiameter.HasValue).ExpectedDiameter;
        }

        if (candidates.Count == 0) return null;

        // Group candidate diameters by clustering within tolerance (e.g. 0.005 in)
        var clusters = new List<List<ActualHole>>();
        foreach (var c in candidates)
        {
            var match = clusters.FirstOrDefault(cl => Math.Abs(cl[0].Diameter - c.Diameter) <= _options.DiameterTolerance);
            if (match != null)
            {
                match.Add(c);
            }
            else
            {
                clusters.Add(new List<ActualHole> { c });
            }
        }

        // Pick largest cluster whose diameter is standard (e.g. > 0.05 in, avoiding tiny artifact faces)
        var validClusters = clusters.Where(cl => cl[0].Diameter >= 0.100).ToList();
        if (validClusters.Count > 0)
        {
            var dominant = validClusters.OrderByDescending(cl => cl.Count).First();
            return Math.Round(dominant.Average(h => h.Diameter), 4);
        }

        return clusters.OrderByDescending(cl => cl.Count).FirstOrDefault()?[0].Diameter;
    }
}
