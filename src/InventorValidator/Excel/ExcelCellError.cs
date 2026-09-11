namespace InventorValidator.Excel;

/// <summary>
/// Utility for identifying and translating Excel formula error codes and values.
/// </summary>
public static class ExcelCellError
{
    // Standard Excel CVErr values returned by COM Value2
    public const int ErrNull = -2146826288;
    public const int ErrDiv0 = -2146826281;
    public const int ErrValue = -2146826273;
    public const int ErrRef = -2146826265;
    public const int ErrName = -2146826259;
    public const int ErrNum = -2146826252;
    public const int ErrNA = -2146826245;

    /// <summary>
    /// Checks if a cell value represents an Excel calculation/formula error.
    /// </summary>
    public static bool IsError(object? value)
    {
        if (value == null) return false;

        if (value is int intVal)
        {
            return intVal is ErrNull or ErrDiv0 or ErrValue or ErrRef or ErrName or ErrNum or ErrNA;
        }

        if (value is string strVal)
        {
            var trimmed = strVal.Trim();
            return trimmed.StartsWith("#") && (
                trimmed.Equals("#REF!", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("#VALUE!", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("#N/A", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("#NAME?", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("#DIV/0!", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("#NUM!", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("#NULL!", StringComparison.OrdinalIgnoreCase)
            );
        }

        return false;
    }

    /// <summary>
    /// Formats the error as a standard Excel error string (e.g., "#REF!").
    /// Returns null if the value is not an error.
    /// </summary>
    public static string? FormatError(object? value)
    {
        if (value == null || !IsError(value)) return null;

        if (value is int intVal)
        {
            return intVal switch
            {
                ErrNull => "#NULL!",
                ErrDiv0 => "#DIV/0!",
                ErrValue => "#VALUE!",
                ErrRef => "#REF!",
                ErrName => "#NAME?",
                ErrNum => "#NUM!",
                ErrNA => "#N/A",
                _ => $"#ERROR({intVal})"
            };
        }

        if (value is string strVal)
        {
            return strVal.Trim().ToUpperInvariant();
        }

        return null;
    }
}
