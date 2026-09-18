using System;
using System.Collections.Generic;
using System.Linq;
using InventorValidator.Geometry.Models;

namespace InventorValidator.Geometry;

/// <summary>
/// Computes pattern-level statistical diagnostics for each channel hole array.
/// Detects systematic offsets (e.g., uniform plate thickness shifts) and spacing anomalies.
/// </summary>
public static class PatternDiagnosticAnalyzer
{
    /// <summary>
    /// Analyzes a set of hole match results for a single channel.
    /// </summary>
    public static PatternDiagnosticResult AnalyzeChannel(
        string group,
        string channelName,
        string referencedPart,
        int expectedCount,
        double expectedSpacing,
        double expectedOffset,
        IReadOnlyList<HoleMatchResult> channelResults)
    {
        var matched = channelResults
            .Where(r => r.Actual != null && r.Expected != null && r.Status != HoleMatchStatus.MissingExpected)
            .OrderBy(r => r.Expected!.HoleIndex)
            .ToList();

        int matchedCount = matched.Count;
        int missingCount = expectedCount - matchedCount;

        var diag = new PatternDiagnosticResult
        {
            ChannelGroup = group,
            ChannelName = channelName,
            ReferencedPart = referencedPart,
            ExpectedCount = expectedCount,
            MatchedCount = matchedCount,
            MissingCount = Math.Max(0, missingCount),
            ExpectedSpacing = expectedSpacing,
            FirstHoleOffset = expectedOffset
        };

        if (matchedCount == 0)
        {
            diag.DiagnosticMessage = expectedCount > 0
                ? $"All {expectedCount} expected holes are missing in the 3D model."
                : "No holes expected for this row.";
            return diag;
        }

        // Calculate signed axis deltas and Z deltas
        double[] axisDeltas = matched.Select(r => r.DeltaAxis).ToArray();
        double[] zDeltas = matched.Select(r => r.DeltaZ).ToArray();

        diag.MeanSignedAxisDelta = Math.Round(axisDeltas.Average(), 4);
        diag.AxisDeltaStdDev = Math.Round(CalculateStdDev(axisDeltas), 4);

        diag.MeanSignedZDelta = Math.Round(zDeltas.Average(), 4);
        diag.ZDeltaStdDev = Math.Round(CalculateStdDev(zDeltas), 4);

        // Calculate actual spacings between consecutive matched actual holes
        if (matchedCount >= 2)
        {
            var consecutiveSpacings = new List<double>();
            for (int i = 0; i < matchedCount - 1; i++)
            {
                var h1 = matched[i].Actual!.Position;
                var h2 = matched[i + 1].Actual!.Position;
                bool isFloorOrRoof = group.ToUpperInvariant().Contains("FLOOR") || group.ToUpperInvariant().Contains("ROOF");
                double dist = isFloorOrRoof ? Math.Abs(h2.X - h1.X) : Math.Abs(h2.Y - h1.Y);
                int indexDiff = matched[i + 1].Expected!.HoleIndex - matched[i].Expected!.HoleIndex;
                if (indexDiff > 0)
                {
                    consecutiveSpacings.Add(dist / indexDiff);
                }
            }

            if (consecutiveSpacings.Count > 0)
            {
                diag.ActualAverageSpacing = Math.Round(consecutiveSpacings.Average(), 4);
                diag.SpacingStdDev = Math.Round(CalculateStdDev(consecutiveSpacings.ToArray()), 4);
                diag.MaxSpacingDeviation = Math.Round(consecutiveSpacings.Max(s => Math.Abs(s - expectedSpacing)), 4);
            }
        }
        else
        {
            diag.ActualAverageSpacing = expectedSpacing;
        }

        // Evaluate systematic shift
        // Systematic shift occurs when standard deviation of the error is low (<= 0.005 in)
        // but the mean shift is significant (> 0.010 in)
        bool axisShift = Math.Abs(diag.MeanSignedAxisDelta) > 0.010 && diag.AxisDeltaStdDev <= 0.005;
        bool zShift = Math.Abs(diag.MeanSignedZDelta) > 0.010 && diag.ZDeltaStdDev <= 0.005;

        if (axisShift || zShift)
        {
            diag.IsSystematicShift = true;
            string axisName = group.ToUpperInvariant().Contains("FLOOR") || group.ToUpperInvariant().Contains("ROOF") ? "X" : "Y";
            var parts = new List<string>();
            if (axisShift)
            {
                string sign = diag.MeanSignedAxisDelta >= 0 ? "+" : "";
                parts.Add($"{sign}{diag.MeanSignedAxisDelta:F3}\" {axisName} shift");
            }
            if (zShift)
            {
                string sign = diag.MeanSignedZDelta >= 0 ? "+" : "";
                parts.Add($"{sign}{diag.MeanSignedZDelta:F3}\" Z shift");
            }

            diag.DiagnosticMessage = $"Systematic {string.Join(" and ", parts)} across {matchedCount}/{expectedCount} holes (spacing consistent at {diag.ActualAverageSpacing:F3}\").";
        }
        else if (matchedCount == expectedCount && matched.All(r => r.Status == HoleMatchStatus.Match))
        {
            diag.DiagnosticMessage = $"All {expectedCount} holes match expected locations within ±0.010\" tolerance.";
        }
        else if (missingCount > 0)
        {
            diag.DiagnosticMessage = $"{matchedCount}/{expectedCount} holes matched; {missingCount} hole(s) missing from pattern.";
        }
        else
        {
            diag.DiagnosticMessage = $"Pattern has individual hole deviations (max spacing error: {diag.MaxSpacingDeviation:F4}\").";
        }

        return diag;
    }

    private static double CalculateStdDev(double[] values)
    {
        if (values.Length <= 1) return 0.0;
        double avg = values.Average();
        double sumSq = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumSq / values.Length);
    }
}
