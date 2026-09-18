using System.Globalization;
using System.Text.RegularExpressions;

namespace InventorValidator.Excel.Parsers;

/// <summary>
/// Header-driven parser for the 'Sheet1' worksheet containing calculator output parameters.
/// </summary>
public static partial class Sheet1TabParser
{
    [GeneratedRegex(@"^Part_(\d+_\d+_\d+.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SuppressionPartRegex();

    [GeneratedRegex(@"^Feature_", RegexOptions.IgnoreCase)]
    private static partial Regex FeatureRegex();

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_\-\.]*$", RegexOptions.Compiled)]
    private static partial Regex ParameterNamePatternRegex();

    public static List<Sheet1ParameterItem> Parse(object[,] values)
    {
        return ParseDetailed(values).Parameters;
    }

    public static (List<Sheet1ParameterItem> Parameters, List<HoleScheduleItem> HoleSchedules, Sheet1Archetype Archetype) ParseDetailed(object[,] values)
    {
        var parameters = new List<Sheet1ParameterItem>();
        var holeSchedules = new List<HoleScheduleItem>();

        int rMin = values.GetLowerBound(0);
        int rMax = values.GetUpperBound(0);
        int cMin = values.GetLowerBound(1);
        int cMax = values.GetUpperBound(1);

        // Step 1: Detect Archetype by scanning rows rMin to min(rMin + 15, rMax)
        var holeScheduleColBlocks = new List<(int HeaderRow, int ColHole, int ColXDim, int ColYDim, int ColDesc, string ScheduleName)>();
        int paramHeaderRow = -1;
        int colParam = -1;
        int colVal = -1;
        int colUnit = -1;
        int colComment = -1;

        for (int r = rMin; r <= Math.Min(rMin + 15, rMax); r++)
        {
            for (int c = cMin; c <= cMax; c++)
            {
                var text = values[r, c]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(text)) continue;

                // Check for Hole Schedule header signature
                if (text.Equals("HOLE", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("HOLE NO", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("HOLE #", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("HOLE NUMBER", StringComparison.OrdinalIgnoreCase))
                {
                    int colX = FindAdjacentColumn(values, r, c + 1, Math.Min(c + 4, cMax), "XDIM", "DIM", "VALUE", "X-DIM", "X DIM");
                    int colY = FindAdjacentColumn(values, r, c + 1, Math.Min(c + 4, cMax), "YDIM", "Y-DIM", "Y DIM");
                    int colDesc = FindAdjacentColumn(values, r, c + 1, Math.Min(c + 5, cMax), "DESCRIPTION", "DESC", "COMMENT");

                    string scheduleName = $"Hole Schedule {holeScheduleColBlocks.Count + 1}";
                    holeScheduleColBlocks.Add((r, c, colX, colY, colDesc, scheduleName));
                }

                // Check for Parameter Table header signature
                if (paramHeaderRow == -1 && (
                    text.Equals("Parameter", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Parameter Name", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Parameters", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("NAME", StringComparison.OrdinalIgnoreCase)))
                {
                    // Check for Value in adjacent columns
                    int valCol = FindAdjacentColumn(values, r, c + 1, Math.Min(c + 3, cMax), "Value", "VALUE", "Values", "Val");
                    if (valCol != -1)
                    {
                        paramHeaderRow = r;
                        colParam = c;
                        colVal = valCol;
                        colUnit = FindAdjacentColumn(values, r, c + 1, Math.Min(c + 4, cMax), "UM", "Unit", "Units");
                        colComment = FindAdjacentColumn(values, r, c + 1, Math.Min(c + 5, cMax), "Comments", "Comment", "Description", "DESC");
                    }
                }
            }

            if (paramHeaderRow != -1 && holeScheduleColBlocks.Count > 0)
            {
                break;
            }
        }

        Sheet1Archetype archetype;

        if (holeScheduleColBlocks.Count > 0 && paramHeaderRow != -1)
        {
            archetype = Sheet1Archetype.MultiTableFanSkid;
        }
        else if (paramHeaderRow != -1)
        {
            archetype = Sheet1Archetype.Standard4Column;
        }
        else
        {
            // Check for Headerless sheet
            archetype = DetectHeaderlessOrFallback(values, rMin, rMax, cMin, cMax, out paramHeaderRow, out colParam, out colVal, out colUnit, out colComment);
        }

        // Parse Hole Schedules if Archetype 2
        if (archetype == Sheet1Archetype.MultiTableFanSkid && holeScheduleColBlocks.Count > 0)
        {
            foreach (var block in holeScheduleColBlocks)
            {
                for (int r = block.HeaderRow + 1; r <= rMax; r++)
                {
                    var rawHole = values[r, block.ColHole];
                    var holeId = rawHole?.ToString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(holeId)) continue;

                    // Stop if we hit a subheader or unrelated table title
                    if (holeId.StartsWith("ITEM", StringComparison.OrdinalIgnoreCase) ||
                        holeId.StartsWith("HOLE", StringComparison.OrdinalIgnoreCase) ||
                        holeId.StartsWith("NAME", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    double? xDim = block.ColXDim != -1 ? ParseDouble(values[r, block.ColXDim]) : null;
                    double? yDim = block.ColYDim != -1 ? ParseDouble(values[r, block.ColYDim]) : null;
                    string desc = block.ColDesc != -1 ? (values[r, block.ColDesc]?.ToString()?.Trim() ?? string.Empty) : string.Empty;

                    holeSchedules.Add(new HoleScheduleItem
                    {
                        RowIndex = r,
                        ScheduleName = block.ScheduleName,
                        HoleIdentifier = holeId,
                        XDim = xDim,
                        YDim = yDim,
                        Description = desc
                    });
                }
            }
        }

        // Parse Parameters
        int startRow = paramHeaderRow != -1 ? paramHeaderRow + 1 : rMin;
        if (colParam == -1)
        {
            colParam = cMin;
            colVal = cMin + 1;
            colUnit = cMin + 2 <= cMax ? cMin + 2 : -1;
            colComment = cMin + 3 <= cMax ? cMin + 3 : -1;
        }

        for (int r = startRow; r <= rMax; r++)
        {
            var rawParam = values[r, colParam];
            var paramName = rawParam?.ToString()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(paramName))
            {
                continue;
            }

            // Skip accidental repeated headers or BOM labels
            if (paramName.Equals("Parameter", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("Parameter Name", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("NAME", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("ITEM", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("HOLE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rawVal = colVal <= cMax ? values[r, colVal] : null;
            var unit = colUnit != -1 && colUnit <= cMax ? (values[r, colUnit]?.ToString()?.Trim() ?? string.Empty) : string.Empty;
            var comment = colComment != -1 && colComment <= cMax ? (values[r, colComment]?.ToString()?.Trim() ?? string.Empty) : string.Empty;

            string? formulaErr = ExcelCellError.FormatError(rawVal);
            string dispVal = formulaErr ?? FormatCellValue(rawVal);

            var category = ClassifyParameter(paramName, unit, rawVal);
            var normalizedName = NormalizeParameterName(paramName);

            bool isSafe = (category is ParameterCategory.Dimension or ParameterCategory.UnitlessCount or ParameterCategory.SuppressionControl)
                          && formulaErr == null;

            parameters.Add(new Sheet1ParameterItem
            {
                RowIndex = r,
                ParameterName = paramName,
                NormalizedName = normalizedName,
                RawValue = rawVal,
                DisplayedValue = dispVal,
                Unit = unit,
                Comment = comment,
                FormulaError = formulaErr,
                Category = category,
                IsSafeToWrite = isSafe
            });
        }

        return (parameters, holeSchedules, archetype);
    }

    private static Sheet1Archetype DetectHeaderlessOrFallback(
        object[,] values,
        int rMin, int rMax, int cMin, int cMax,
        out int paramHeaderRow, out int colParam, out int colVal, out int colUnit, out int colComment)
    {
        // Scan first 5 rows and columns for a row that looks like: [Identifier, Number/String, ...]
        for (int r = rMin; r <= Math.Min(rMin + 5, rMax); r++)
        {
            for (int c = cMin; c < cMax; c++)
            {
                var cell1 = values[r, c]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(cell1) && ParameterNamePatternRegex().IsMatch(cell1))
                {
                    var cell2 = values[r, c + 1];
                    if (cell2 != null && (cell2 is double or int or float || double.TryParse(cell2.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _)))
                    {
                        // Found valid parameter data row on row r at column c
                        paramHeaderRow = r - 1; // so startRow = paramHeaderRow + 1 = r
                        colParam = c;
                        colVal = c + 1;
                        colUnit = c + 2 <= cMax ? c + 2 : -1;
                        colComment = c + 3 <= cMax ? c + 3 : -1;
                        return Sheet1Archetype.Headerless;
                    }
                }
            }
        }

        // Fallback default
        paramHeaderRow = rMin;
        colParam = cMin;
        colVal = cMin + 1;
        colUnit = cMin + 2 <= cMax ? cMin + 2 : -1;
        colComment = cMin + 3 <= cMax ? cMin + 3 : -1;
        return Sheet1Archetype.Standard4Column;
    }

    private static int FindAdjacentColumn(object[,] values, int r, int startCol, int endCol, params string[] keywords)
    {
        for (int c = startCol; c <= endCol; c++)
        {
            var text = values[r, c]?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text)) continue;

            foreach (var kw in keywords)
            {
                if (text.Equals(kw, StringComparison.OrdinalIgnoreCase) || text.Contains(kw, StringComparison.OrdinalIgnoreCase))
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
        if (double.TryParse(val.ToString()?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
        {
            return parsed;
        }
        return null;
    }

    public static string NormalizeParameterName(string paramName)
    {
        if (string.IsNullOrWhiteSpace(paramName)) return string.Empty;

        // Trim and replace hyphens/spaces with underscores
        var cleaned = paramName.Trim()
            .Replace('-', '_')
            .Replace(' ', '_');

        return cleaned;
    }

    public static ParameterCategory ClassifyParameter(string paramName, string unit, object? rawValue)
    {
        var trimmed = paramName.Trim();

        // 1. Suppression controls (Part_<PartNum>_<Suffix> or containing "Suppression")
        if (SuppressionPartRegex().IsMatch(trimmed) ||
            trimmed.StartsWith("Part_", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Suppression", StringComparison.OrdinalIgnoreCase))
        {
            return ParameterCategory.SuppressionControl;
        }

        // 2. Feature controls
        if (FeatureRegex().IsMatch(trimmed))
        {
            return ParameterCategory.FeatureControl;
        }

        // 3. Length dimensions
        var unitLower = unit.ToLowerInvariant();
        if (unitLower is "in" or "inch" or "inches" or "mm" or "ft" or "cm" or "\"")
        {
            return ParameterCategory.Dimension;
        }

        // 4. Unitless counts / flags
        if (unitLower is "ul" or "count" or "ea" or "qty")
        {
            if (trimmed.Contains("qty", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("count", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("high", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("wide", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("deep", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("rows", StringComparison.OrdinalIgnoreCase))
            {
                return ParameterCategory.UnitlessCount;
            }

            // UL might also be an unflagged suppression or general multiplier
            return ParameterCategory.UnitlessCount;
        }

        // 5. Property / Material
        if (trimmed.Contains("Material", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Style", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Finish", StringComparison.OrdinalIgnoreCase))
        {
            return ParameterCategory.Property;
        }

        // 6. Inspect by name semantics if unit is empty
        if (string.IsNullOrEmpty(unit))
        {
            if (trimmed.EndsWith("Thk", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("Width", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("Height", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("Length", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("Loc", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("Clearance", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("Spacing", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("IH", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("IW", StringComparison.OrdinalIgnoreCase))
            {
                return ParameterCategory.Dimension;
            }

            if (rawValue is double or int or float ||
                (rawValue != null && double.TryParse(rawValue.ToString()?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out _)))
            {
                return ParameterCategory.Dimension;
            }
        }

        return ParameterCategory.Informational;
    }

    private static string FormatCellValue(object? val)
    {
        if (val == null) return string.Empty;

        if (val is double d)
        {
            return d.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return val.ToString()?.Trim() ?? string.Empty;
    }
}
