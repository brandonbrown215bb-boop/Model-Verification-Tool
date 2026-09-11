using System.Globalization;

namespace InventorValidator.Excel.Parsers;

/// <summary>
/// Header-driven parser for the 'Data' worksheet in engineering calculators.
/// </summary>
public static class DataTabParser
{
    public static (List<DataInputItem> Inputs, double? ErrorCheckValue, string ErrorCheckRaw, bool IsErrorCheckPassed)
        Parse(object[,] values)
    {
        var inputs = new List<DataInputItem>();
        double? overallErrorCheck = null;
        string overallErrorCheckRaw = string.Empty;

        int rMin = values.GetLowerBound(0);
        int rMax = values.GetUpperBound(0);
        int cMin = values.GetLowerBound(1);
        int cMax = values.GetUpperBound(1);

        int headerRow = -1;
        int colParam = -1;
        int colVal = -1;
        int colDesc = -1;
        int colAllowed = -1;
        int colErr = -1;

        // Step 1: Locate header row by inspecting first 15 rows
        for (int r = rMin; r <= Math.Min(rMin + 15, rMax); r++)
        {
            for (int c = cMin; c <= cMax; c++)
            {
                var text = values[r, c]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(text)) continue;

                if (text.Contains("Input", StringComparison.OrdinalIgnoreCase) && text.Contains("parameter", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Parameter", StringComparison.OrdinalIgnoreCase))
                {
                    colParam = c;
                    headerRow = r;
                }
                else if (text.Contains("Value", StringComparison.OrdinalIgnoreCase) && colVal == -1)
                {
                    colVal = c;
                }
                else if (text.Contains("Description", StringComparison.OrdinalIgnoreCase) && colDesc == -1)
                {
                    colDesc = c;
                }
                else if ((text.Contains("Values For MOM", StringComparison.OrdinalIgnoreCase) ||
                          text.Contains("Allowed", StringComparison.OrdinalIgnoreCase)) && colAllowed == -1)
                {
                    colAllowed = c;
                }
                else if (text.Contains("Error Check", StringComparison.OrdinalIgnoreCase) && colErr == -1)
                {
                    colErr = c;
                }
            }

            if (headerRow != -1 && colParam != -1)
            {
                break;
            }
        }

        // Fallbacks if some headers were not found
        if (colParam == -1) colParam = cMin;
        if (colVal == -1) colVal = cMin + 1;
        if (colDesc == -1) colDesc = cMin + 2;
        if (colAllowed == -1) colAllowed = cMin + 3;
        if (colErr == -1) colErr = cMin + 4;

        int startRow = headerRow != -1 ? headerRow + 1 : rMin + 1;

        // Step 2: Iterate rows and extract data
        for (int r = startRow; r <= rMax; r++)
        {
            var rawParam = values[r, colParam];
            var paramName = rawParam?.ToString()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(paramName))
            {
                // Check if column B has Error_Check label
                if (colVal <= cMax)
                {
                    var altText = values[r, colVal]?.ToString()?.Trim() ?? string.Empty;
                    if (altText.Equals("Error_Check", StringComparison.OrdinalIgnoreCase))
                    {
                        ExtractErrorCheck(values, r, colVal + 1, ref overallErrorCheck, ref overallErrorCheckRaw);
                    }
                }
                continue;
            }

            // Check if this row is the workbook Error_Check summary
            if (paramName.Equals("Error_Check", StringComparison.OrdinalIgnoreCase) ||
                paramName.StartsWith("Error_Check", StringComparison.OrdinalIgnoreCase) ||
                paramName.Equals("Error Check", StringComparison.OrdinalIgnoreCase))
            {
                ExtractErrorCheck(values, r, colVal, ref overallErrorCheck, ref overallErrorCheckRaw);
                continue;
            }

            var rawVal = colVal <= cMax ? values[r, colVal] : null;
            var desc = colDesc <= cMax ? (values[r, colDesc]?.ToString()?.Trim() ?? string.Empty) : string.Empty;
            var allowed = colAllowed <= cMax ? (values[r, colAllowed]?.ToString()?.Trim() ?? string.Empty) : string.Empty;
            var errVal = colErr <= cMax ? (values[r, colErr]?.ToString()?.Trim() ?? string.Empty) : string.Empty;

            string dispVal = rawVal != null ? FormatCellValue(rawVal) : string.Empty;
            bool hasErr = ExcelCellError.IsError(rawVal) || (!string.IsNullOrWhiteSpace(errVal) && !errVal.Equals("0") && !errVal.Equals("0.0"));

            inputs.Add(new DataInputItem
            {
                RowIndex = r,
                ParameterName = paramName,
                RawValue = rawVal,
                DisplayedValue = dispVal,
                Description = desc,
                AllowedValues = allowed,
                ErrorCheckValue = errVal,
                HasError = hasErr
            });
        }

        // If overall error check wasn't found yet, scan entire sheet for "Error_Check"
        if (!overallErrorCheck.HasValue)
        {
            for (int r = rMin; r <= rMax; r++)
            {
                for (int c = cMin; c <= cMax; c++)
                {
                    var text = values[r, c]?.ToString()?.Trim() ?? string.Empty;
                    if (text.Equals("Error_Check", StringComparison.OrdinalIgnoreCase))
                    {
                        if (c + 1 <= cMax)
                        {
                            ExtractErrorCheck(values, r, c + 1, ref overallErrorCheck, ref overallErrorCheckRaw);
                            break;
                        }
                    }
                }
                if (overallErrorCheck.HasValue) break;
            }
        }

        bool passed = true;
        if (overallErrorCheck.HasValue)
        {
            passed = Math.Abs(overallErrorCheck.Value) < 0.0001;
        }
        else if (!string.IsNullOrWhiteSpace(overallErrorCheckRaw))
        {
            passed = overallErrorCheckRaw.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                     overallErrorCheckRaw.Equals("0.0", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            overallErrorCheckRaw = "N/A";
            passed = true;
        }

        return (inputs, overallErrorCheck, overallErrorCheckRaw, passed);
    }

    private static void ExtractErrorCheck(object[,] values, int r, int valCol, ref double? overallErrorCheck, ref string overallErrorCheckRaw)
    {
        if (valCol <= values.GetLength(1))
        {
            var valObj = values[r, valCol];
            if (valObj != null)
            {
                overallErrorCheckRaw = valObj.ToString()?.Trim() ?? string.Empty;
                if (valObj is double d)
                {
                    overallErrorCheck = d;
                }
                else if (valObj is int i)
                {
                    overallErrorCheck = i;
                }
                else if (double.TryParse(overallErrorCheckRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
                {
                    overallErrorCheck = parsed;
                }
            }
        }
    }

    private static string FormatCellValue(object val)
    {
        if (ExcelCellError.IsError(val))
        {
            return ExcelCellError.FormatError(val) ?? "#ERROR";
        }
        if (val is double d)
        {
            return d.ToString("0.###", CultureInfo.InvariantCulture);
        }
        return val.ToString()?.Trim() ?? string.Empty;
    }
}
