using System.Globalization;

namespace InventorValidator.Excel.Parsers;

/// <summary>
/// Header-driven parser for the 'Channel Loc' worksheet defining 3D linear mounting hole arrays.
/// Supports 4 groups: Floor Channels, Roof Channels, South Wall Channels, North Wall Channels.
/// </summary>
public static class ChannelLocTabParser
{
    public static List<ChannelLocationItem> Parse(object[,] values)
    {
        var items = new List<ChannelLocationItem>();

        int rMin = values.GetLowerBound(0);
        int rMax = values.GetUpperBound(0);
        int cMin = values.GetLowerBound(1);
        int cMax = values.GetUpperBound(1);

        string currentGroup = string.Empty;
        string currentAxis = "X";
        int colName = -1;
        int colZ = -1;
        int colOffset = -1;
        int colQty = -1;
        int colSpacing = -1;
        int colPart = -1;

        for (int r = rMin; r <= rMax; r++)
        {
            // Check if this row is a group header row
            string? detectedGroup = DetectGroupHeader(values, r, cMin, cMax);
            if (!string.IsNullOrEmpty(detectedGroup))
            {
                currentGroup = detectedGroup;
                currentAxis = (currentGroup.Contains("Floor", StringComparison.OrdinalIgnoreCase) ||
                               currentGroup.Contains("Roof", StringComparison.OrdinalIgnoreCase)) ? "X" : "Y";

                // Locate column positions in this header row
                colName = FindColumnIndex(values, r, cMin, cMax, "Channel Name", "Channel");
                colZ = FindColumnIndex(values, r, cMin, cMax, "Z_LOCATION", "Z Location", "Z");
                colOffset = FindColumnIndex(values, r, cMin, cMax, "OFFSET", "ARRAY_OFFSET");
                colQty = FindColumnIndex(values, r, cMin, cMax, "QTY", "ARRAY_QTY");
                colSpacing = FindColumnIndex(values, r, cMin, cMax, "SPACING", "ARRAY_SPACING");

                // Default fallbacks based on standardized layout
                if (colName == -1) colName = cMin + 1;
                if (colZ == -1) colZ = cMin + 2;
                if (colOffset == -1) colOffset = cMin + 3;
                if (colQty == -1) colQty = cMin + 4;
                if (colSpacing == -1) colSpacing = cMin + 5;

                colPart = FindColumnIndex(values, r, cMin, cMax, "Part", "Part Number", "Part #", "Component");
                if (colPart == -1)
                {
                    colPart = colName + 6;
                }

                continue;
            }

            if (string.IsNullOrEmpty(currentGroup) || colName == -1)
            {
                continue;
            }

            // Check channel name cell
            var rawName = colName <= cMax ? values[r, colName] : null;
            var nameStr = rawName?.ToString()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(nameStr) || nameStr.Equals("Channel Name", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // If this is an informational row or subheader, skip
            if (nameStr.StartsWith("DIMENSION", StringComparison.OrdinalIgnoreCase) ||
                nameStr.StartsWith("FROM", StringComparison.OrdinalIgnoreCase) ||
                nameStr.StartsWith("X_ARRAY", StringComparison.OrdinalIgnoreCase) ||
                nameStr.StartsWith("Y_ARRAY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Read raw cells
            var rawZ = colZ <= cMax ? values[r, colZ] : null;
            var rawOffset = colOffset <= cMax ? values[r, colOffset] : null;
            var rawQty = colQty <= cMax ? values[r, colQty] : null;
            var rawSpacing = colSpacing <= cMax ? values[r, colSpacing] : null;
            var rawPart = colPart <= cMax ? values[r, colPart] : null;

            // Check for formula errors (#REF!, #VALUE!, etc.)
            string? errZ = ExcelCellError.FormatError(rawZ);
            string? errOffset = ExcelCellError.FormatError(rawOffset);
            string? errQty = ExcelCellError.FormatError(rawQty);
            string? errSpacing = ExcelCellError.FormatError(rawSpacing);

            string? formulaError = errZ ?? errOffset ?? errQty ?? errSpacing;

            double? parsedZ = ParseDouble(rawZ);
            double? parsedOffset = ParseDouble(rawOffset);
            int? parsedQty = ParseInt(rawQty);
            double? parsedSpacing = ParseDouble(rawSpacing);
            string partNumber = rawPart?.ToString()?.Trim() ?? string.Empty;

            // If part number was empty at expected column, scan adjacent columns for part number pattern
            if (string.IsNullOrWhiteSpace(partNumber) && colSpacing != -1)
            {
                for (int c = colSpacing + 1; c <= cMax; c++)
                {
                    var candidate = values[r, c]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(candidate) && candidate.Contains('-') && candidate.Any(char.IsDigit))
                    {
                        partNumber = candidate;
                        break;
                    }
                }
            }

            ChannelStatus status;
            string? note = null;

            if (formulaError != null)
            {
                status = ChannelStatus.SkippedInvalidRow;
                note = $"Formula error in row ({formulaError})";
            }
            else if (!parsedZ.HasValue || parsedZ.Value <= 0.0001)
            {
                status = ChannelStatus.SkippedInvalidRow;
                note = "Zero Z-location / Z-offset (Unused or invalid)";
            }
            else if (!parsedQty.HasValue || parsedQty.Value <= 0)
            {
                status = ChannelStatus.SkippedInvalidRow;
                note = "Zero quantity (Unused)";
            }
            else if (!parsedOffset.HasValue || !parsedSpacing.HasValue)
            {
                status = ChannelStatus.SkippedInvalidRow;
                note = "Missing required numeric coordinates";
            }
            else
            {
                status = ChannelStatus.Valid;
            }

            items.Add(new ChannelLocationItem
            {
                RowIndex = r,
                ChannelGroup = currentGroup,
                ChannelName = nameStr,
                Axis = currentAxis,
                Offset = parsedOffset,
                Spacing = parsedSpacing,
                Quantity = parsedQty,
                ZLocation = parsedZ,
                ReferencedPart = partNumber,
                Status = status,
                Notes = note
            });
        }

        return items;
    }

    private static string? DetectGroupHeader(object[,] values, int r, int cMin, int cMax)
    {
        for (int c = cMin; c <= cMax; c++)
        {
            var text = values[r, c]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text)) continue;

            if (text.Contains("FLOOR CHANNELS", StringComparison.OrdinalIgnoreCase))
                return "Floor Channels";
            if (text.Contains("ROOF CHANNELS", StringComparison.OrdinalIgnoreCase))
                return "Roof Channels";
            if (text.Contains("SOUTH WALL CHANNELS", StringComparison.OrdinalIgnoreCase))
                return "South Wall Channels";
            if (text.Contains("NORTH WALL CHANNELS", StringComparison.OrdinalIgnoreCase))
                return "North Wall Channels";
        }

        return null;
    }

    private static int FindColumnIndex(object[,] values, int r, int cMin, int cMax, params string[] keywords)
    {
        for (int c = cMin; c <= cMax; c++)
        {
            var text = values[r, c]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text)) continue;

            foreach (var kw in keywords)
            {
                if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }
        }
        return -1;
    }

    private static double? ParseDouble(object? val)
    {
        if (val == null || ExcelCellError.IsError(val)) return null;

        if (val is double d) return d;
        if (val is int i) return i;
        if (val is float f) return f;

        var str = val.ToString()?.Trim();
        if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
        {
            return parsed;
        }

        return null;
    }

    private static int? ParseInt(object? val)
    {
        if (val == null || ExcelCellError.IsError(val)) return null;

        if (val is int i) return i;
        if (val is double d) return (int)Math.Round(d);

        var str = val.ToString()?.Trim();
        if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedD))
        {
            return (int)Math.Round(parsedD);
        }

        return null;
    }
}
