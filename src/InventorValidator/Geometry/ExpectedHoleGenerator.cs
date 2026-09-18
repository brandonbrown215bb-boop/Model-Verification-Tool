using System;
using System.Collections.Generic;
using InventorValidator.Excel;
using InventorValidator.Geometry.Models;

namespace InventorValidator.Geometry;

/// <summary>
/// Generates expected 3D hole instances from Channel Loc engineering parameters.
/// </summary>
public static class ExpectedHoleGenerator
{
    /// <summary>
    /// Generates ExpectedHole instances for all valid rows in the provided channel location items.
    /// </summary>
    /// <param name="items">The parsed ChannelLocationItem list from Excel.</param>
    /// <param name="segmentStartZOffset">Z offset of the Segment Start reference plane relative to assembly origin (inches).</param>
    /// <param name="roofHeight">Optional unit height (IH) for Roof channels diagnostic Y (default 0.0).</param>
    /// <param name="unitWidth">Optional unit width (IW) for South Wall diagnostic X (default 0.0).</param>
    public static List<ExpectedHole> GenerateExpectedHoles(
        IEnumerable<ChannelLocationItem> items,
        double segmentStartZOffset = 0.0,
        double roofHeight = 0.0,
        double unitWidth = 0.0)
    {
        var result = new List<ExpectedHole>();

        foreach (var item in items)
        {
            if (item.Status != ChannelStatus.Valid ||
                !item.Offset.HasValue ||
                !item.Spacing.HasValue ||
                !item.Quantity.HasValue ||
                item.Quantity.Value <= 0 ||
                !item.ZLocation.HasValue)
            {
                continue;
            }

            string groupNormalized = NormalizeGroup(item.ChannelGroup);
            bool isFloorOrRoof = groupNormalized.Contains("FLOOR") || groupNormalized.Contains("ROOF");
            var authAxes = isFloorOrRoof ? AuthoritativeAxisPair.XZ : AuthoritativeAxisPair.YZ;

            double expectedZ = item.ZLocation.Value + segmentStartZOffset;
            int qty = item.Quantity.Value;
            double offset = item.Offset.Value;
            double spacing = item.Spacing.Value;

            for (int i = 0; i < qty; i++)
            {
                double primaryPos = offset + (i * spacing);

                Point3D pos;
                if (isFloorOrRoof)
                {
                    double diagnosticY = groupNormalized.Contains("ROOF") ? roofHeight : 0.0;
                    pos = new Point3D(primaryPos, diagnosticY, expectedZ);
                }
                else
                {
                    double diagnosticX = groupNormalized.Contains("SOUTH") ? unitWidth : 0.0;
                    pos = new Point3D(diagnosticX, primaryPos, expectedZ);
                }

                result.Add(new ExpectedHole
                {
                    ChannelGroup = item.ChannelGroup,
                    ChannelName = item.ChannelName,
                    SourceRowIndex = item.RowIndex,
                    HoleIndex = i,
                    ReferencedPart = item.ReferencedPart ?? string.Empty,
                    ExpectedPosition = pos,
                    AuthoritativeAxes = authAxes,
                    ArrayAxis = item.Axis ?? (isFloorOrRoof ? "X" : "Y"),
                    ArrayOffset = offset,
                    ArraySpacing = spacing,
                    ArrayQuantity = qty
                });
            }
        }

        return result;
    }

    private static string NormalizeGroup(string group)
    {
        return (group ?? string.Empty).ToUpperInvariant().Trim();
    }
}
