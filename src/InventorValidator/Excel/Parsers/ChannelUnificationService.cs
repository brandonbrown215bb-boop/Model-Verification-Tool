namespace InventorValidator.Excel.Parsers;

/// <summary>
/// Service that unifies channel rows across unit sides (Floor, North/Right, South/Left, Roof).
/// Rows sharing the same Z-location / Z-offset are expressed as a single unified channel,
/// preserving their constituent array segments for detailed CAD hole matching.
/// </summary>
public static class ChannelUnificationService
{
    public static List<UnifiedChannel> Unify(IReadOnlyList<ChannelLocationItem> rawItems)
    {
        var result = new List<UnifiedChannel>();
        if (rawItems == null || rawItems.Count == 0)
        {
            return result;
        }

        // Only consider valid rows with a positive Z-location (Z > 0.0001)
        var validRows = rawItems
            .Where(r => r.Status == ChannelStatus.Valid && r.ZLocation.HasValue && r.ZLocation.Value > 0.0001)
            .ToList();

        if (validRows.Count == 0)
        {
            return result;
        }

        // Group by rounded Z location (tolerance: 0.001 in)
        var zGroups = validRows
            .GroupBy(r => Math.Round(r.ZLocation!.Value, 3))
            .OrderBy(g => g.Key);

        foreach (var group in zGroups)
        {
            double zVal = group.Key;
            var segments = group.ToList();

            // Identify which unit sides are represented
            var sides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var seg in segments)
            {
                string grp = seg.ChannelGroup;
                if (grp.Contains("Floor", StringComparison.OrdinalIgnoreCase))
                    sides.Add("Floor");
                else if (grp.Contains("Roof", StringComparison.OrdinalIgnoreCase))
                    sides.Add("Roof");
                else if (grp.Contains("South", StringComparison.OrdinalIgnoreCase) || grp.Contains("Left", StringComparison.OrdinalIgnoreCase))
                    sides.Add("South Wall (Left)");
                else if (grp.Contains("North", StringComparison.OrdinalIgnoreCase) || grp.Contains("Right", StringComparison.OrdinalIgnoreCase))
                    sides.Add("North Wall (Right)");
                else
                    sides.Add(grp);
            }

            // Order sides consistently: Floor, Roof, South Wall, North Wall
            var orderedSides = new List<string>();
            if (sides.Contains("Floor")) orderedSides.Add("Floor");
            if (sides.Contains("Roof")) orderedSides.Add("Roof");
            if (sides.Contains("South Wall (Left)")) orderedSides.Add("South Wall (Left)");
            if (sides.Contains("North Wall (Right)")) orderedSides.Add("North Wall (Right)");
            foreach (var s in sides)
            {
                if (!orderedSides.Contains(s)) orderedSides.Add(s);
            }

            string sidesSummary = string.Join(", ", orderedSides);

            // Collect distinct referenced parts
            var distinctParts = segments
                .Select(s => s.ReferencedPart?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string partsSummary = distinctParts.Count > 0 ? string.Join(", ", distinctParts) : "None";

            // Check if any skipped rows at this Z had formula errors
            var errorRowsAtZ = rawItems
                .Where(r => r.ZLocation.HasValue && Math.Abs(r.ZLocation.Value - zVal) < 0.001 &&
                            r.Status == ChannelStatus.SkippedInvalidRow &&
                            r.Notes != null && r.Notes.Contains("Formula error"))
                .ToList();

            string channelName = orderedSides.Count >= 2
                ? $"Perimeter Channel @ Z = {zVal:N3}\""
                : $"{segments[0].ChannelName} (Z = {zVal:N3}\")";

            string note = orderedSides.Count >= 2
                ? $"Unified across {orderedSides.Count} sides ({sidesSummary}) with {segments.Count} array segments"
                : $"Single-side array with {segments.Count} segment(s)";

            if (errorRowsAtZ.Count > 0)
            {
                note += $" ({errorRowsAtZ.Count} row(s) at Z={zVal:N3} had formula errors and were skipped)";
            }

            result.Add(new UnifiedChannel
            {
                ChannelName = channelName,
                ZLocation = zVal,
                SidesSummary = sidesSummary,
                SideCount = orderedSides.Count,
                ReferencedPartsSummary = partsSummary,
                Status = ChannelStatus.Valid,
                Segments = segments,
                Notes = note
            });
        }

        return result;
    }
}
