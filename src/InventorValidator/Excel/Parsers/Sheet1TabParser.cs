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

    public static List<Sheet1ParameterItem> Parse(object[,] values)
    {
        var parameters = new List<Sheet1ParameterItem>();

        int rMin = values.GetLowerBound(0);
        int rMax = values.GetUpperBound(0);
        int cMin = values.GetLowerBound(1);
        int cMax = values.GetUpperBound(1);

        int headerRow = -1;
        int colParam = -1;
        int colVal = -1;
        int colUnit = -1;
        int colComment = -1;

        // Step 1: Find header row
        for (int r = rMin; r <= Math.Min(rMin + 10, rMax); r++)
        {
            for (int c = cMin; c <= cMax; c++)
            {
                var text = values[r, c]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(text)) continue;

                if (text.Equals("Parameter", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Parameter Name", StringComparison.OrdinalIgnoreCase))
                {
                    colParam = c;
                    headerRow = r;
                }
                else if (text.Equals("Value", StringComparison.OrdinalIgnoreCase) && colVal == -1)
                {
                    colVal = c;
                }
                else if ((text.Equals("UM", StringComparison.OrdinalIgnoreCase) ||
                          text.Equals("Unit", StringComparison.OrdinalIgnoreCase) ||
                          text.Equals("Units", StringComparison.OrdinalIgnoreCase)) && colUnit == -1)
                {
                    colUnit = c;
                }
                else if ((text.Equals("Comments", StringComparison.OrdinalIgnoreCase) ||
                          text.Equals("Comment", StringComparison.OrdinalIgnoreCase) ||
                          text.Equals("Description", StringComparison.OrdinalIgnoreCase)) && colComment == -1)
                {
                    colComment = c;
                }
            }

            if (headerRow != -1 && colParam != -1)
            {
                break;
            }
        }

        // Fallbacks
        if (colParam == -1) colParam = cMin;
        if (colVal == -1) colVal = cMin + 1;
        if (colUnit == -1) colUnit = cMin + 2;
        if (colComment == -1) colComment = cMin + 3;

        int startRow = headerRow != -1 ? headerRow + 1 : rMin + 1;

        // Step 2: Parse parameter rows
        for (int r = startRow; r <= rMax; r++)
        {
            var rawParam = values[r, colParam];
            var paramName = rawParam?.ToString()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(paramName))
            {
                continue;
            }

            var rawVal = colVal <= cMax ? values[r, colVal] : null;
            var unit = colUnit <= cMax ? (values[r, colUnit]?.ToString()?.Trim() ?? string.Empty) : string.Empty;
            var comment = colComment <= cMax ? (values[r, colComment]?.ToString()?.Trim() ?? string.Empty) : string.Empty;

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

        return parameters;
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

            if (rawValue is double or int or float)
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
