using System.Globalization;
using System.Text.RegularExpressions;
using InventorValidator.Excel;
using InventorValidator.Infrastructure;
using InventorValidator.Inventor.Models;

namespace InventorValidator.Inventor;

/// <summary>
/// Implements the 4-tier parameter matching precedence, safe-write policy classification,
/// and Inspect Mode discrepancy analysis between Excel Sheet1 outputs and Inventor model parameters.
/// </summary>
public class ParameterMatchingService
{
    private static readonly Regex SuppressionParamRegex = new(
        @"^Part_(?<num>\d+_\d+_\d+)(?:_(?<suffix>\d+))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GeneralSuppressionParamRegex = new(
        @"^Part_(?<num>[a-zA-Z0-9_\-]+?)(?:_(?<suffix>\d+))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Evaluates and compares all Sheet1 calculator parameters against inventoried assembly parameters and occurrences.
    /// </summary>
    public ParameterComparisonResult CompareParameters(
        IReadOnlyList<Sheet1ParameterItem> sheet1Items,
        AssemblyInventoryResult inventory)
    {
        var result = new ParameterComparisonResult();

        // Index Inventor parameters for fast matching by Name
        var paramsExact = inventory.Parameters
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var paramsIgnoreCase = inventory.Parameters
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var paramsNormalized = inventory.Parameters
            .GroupBy(p => NormalizeName(p.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Index Inventor parameters by Expression for Tier 5 expression-driven matching
        var paramsByExprExact = inventory.Parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Expression))
            .GroupBy(p => p.Expression.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var paramsByExprIgnoreCase = inventory.Parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Expression))
            .GroupBy(p => p.Expression.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var paramsByExprNormalized = inventory.Parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Expression))
            .GroupBy(p => NormalizeName(p.Expression), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Index occurrences by name and normalized part-suffix
        var occurrencesByName = FlattenOccurrences(inventory.Occurrences)
            .GroupBy(o => o.OccurrenceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var excelParam in sheet1Items)
        {
            var row = MatchAndCompareRow(
                excelParam,
                paramsExact,
                paramsIgnoreCase,
                paramsNormalized,
                paramsByExprExact,
                paramsByExprIgnoreCase,
                paramsByExprNormalized,
                occurrencesByName,
                inventory.AssemblyName);

            result.Rows.Add(row);
        }

        DiagnosticsLogger.Instance.Info(
            $"Parameter matching complete: {result.TotalRows} rows evaluated. " +
            $"Matched: {result.MatchedCount}, Discrepancies: {result.DiscrepancyCount}, " +
            $"Missing in CAD: {result.MissingInModelCount}, Safe to Write: {result.SafeToWriteCount}, " +
            $"Protected Equations: {result.ProtectedFormulaCount}");

        return result;
    }

    private ParameterComparisonRow MatchAndCompareRow(
        Sheet1ParameterItem excelParam,
        Dictionary<string, List<InventorParameterItem>> exactMap,
        Dictionary<string, List<InventorParameterItem>> ignoreCaseMap,
        Dictionary<string, List<InventorParameterItem>> normMap,
        Dictionary<string, List<InventorParameterItem>> exprExactMap,
        Dictionary<string, List<InventorParameterItem>> exprIgnoreCaseMap,
        Dictionary<string, List<InventorParameterItem>> exprNormMap,
        Dictionary<string, OccurrenceInventoryItem> occurrencesByName,
        string topDocName)
    {
        var row = new ParameterComparisonRow
        {
            ExcelParameterName = excelParam.ParameterName,
            ExcelValueRaw = excelParam.DisplayedValue,
            ExcelUnit = excelParam.Unit,
            ExcelCategory = excelParam.Category.ToString()
        };

        if (TryParseDouble(excelParam.DisplayedValue, out double dVal))
        {
            row.ExcelNumericValue = dVal;
        }

        // Tier 4 check: Suppression parameter mapping (e.g. Part_091_30102_466_1 or Part_091_30102_458)
        if (TryMatchSuppression(excelParam, occurrencesByName, out var occItem, out string mappedOccName))
        {
            row.MatchType = MatchClassification.SuppressionMatch;
            row.TargetDocument = topDocName;
            row.TargetOccurrenceName = occItem != null ? occItem.OccurrenceName : mappedOccName;
            row.WritePolicy = SafeWriteClassification.SafeToWrite;

            if (occItem != null)
            {
                row.ModelSuppressionState = occItem.IsActive;
                bool expectedActive = row.ExcelNumericValue.HasValue && Math.Abs(row.ExcelNumericValue.Value - 1.0) < 0.001;

                if (expectedActive == occItem.IsActive)
                {
                    row.Status = ComparisonStatus.Match;
                    row.Notes = $"Occurrence '{occItem.OccurrenceName}' suppression agrees ({(occItem.IsActive ? "Active" : "Suppressed")}).";
                }
                else
                {
                    row.Status = ComparisonStatus.Discrepancy;
                    row.Notes = $"Suppression discrepancy: Excel expects {(expectedActive ? "1 (Active)" : "0 (Suppressed)")}, but CAD occurrence is {(occItem.IsActive ? "Active" : "Suppressed")}.";
                }
            }
            else
            {
                row.Status = ComparisonStatus.MissingInModel;
                row.Notes = $"Referenced suppression occurrence '{mappedOccName}' not found in assembly hierarchy.";
            }

            return row;
        }

        // Tier 1: Exact Case-Sensitive Match by Parameter Name
        if (exactMap.TryGetValue(excelParam.ParameterName, out var exactMatches))
        {
            if (exactMatches.Count == 1)
            {
                return EvaluateParameterMatch(row, exactMatches[0], MatchClassification.UniqueExactMatch);
            }
            return CreateAmbiguousMatch(row, exactMatches);
        }

        // Tier 2: Exact Case-Insensitive Match by Parameter Name
        if (ignoreCaseMap.TryGetValue(excelParam.ParameterName, out var icMatches))
        {
            if (icMatches.Count == 1)
            {
                return EvaluateParameterMatch(row, icMatches[0], MatchClassification.UniqueExactMatch);
            }
            return CreateAmbiguousMatch(row, icMatches);
        }

        // Tier 3: Trimmed & Normalized Match by Parameter Name
        string normName = NormalizeName(excelParam.ParameterName);
        if (normMap.TryGetValue(normName, out var normMatches))
        {
            if (normMatches.Count == 1)
            {
                return EvaluateParameterMatch(row, normMatches[0], MatchClassification.UniqueNormalizedMatch);
            }
            return CreateAmbiguousMatch(row, normMatches);
        }

        // Tier 5: Expression-Driven Match (CAD parameter expression references Excel parameter name, e.g. d14 = IW)
        if (exprExactMap.TryGetValue(excelParam.ParameterName, out var exprExactMatches))
        {
            return EvaluateExpressionMatch(row, exprExactMatches, excelParam);
        }
        if (exprIgnoreCaseMap.TryGetValue(excelParam.ParameterName, out var exprIcMatches))
        {
            return EvaluateExpressionMatch(row, exprIcMatches, excelParam);
        }
        if (exprNormMap.TryGetValue(normName, out var exprNormMatches))
        {
            return EvaluateExpressionMatch(row, exprNormMatches, excelParam);
        }

        // No match found in model parameters
        row.MatchType = MatchClassification.NoMatch;
        row.WritePolicy = SafeWriteClassification.NotApplicable;

        if (excelParam.Category == ParameterCategory.FeatureControl)
        {
            row.Status = ComparisonStatus.NotApplicable;
            row.Notes = "Feature suppression control; evaluated by iLogic rules.";
        }
        else if (excelParam.Category == ParameterCategory.Informational || excelParam.Category == ParameterCategory.Unknown)
        {
            row.Status = ComparisonStatus.NotApplicable;
            row.Notes = "Informational calculator output; no CAD parameter target expected.";
        }
        else if (excelParam.Category == ParameterCategory.SuppressionControl)
        {
            row.Status = ComparisonStatus.MissingInModel;
            row.Notes = "Suppression flag in Sheet1; no matching component occurrence in assembly hierarchy.";
        }
        else
        {
            row.Status = ComparisonStatus.MissingInModel;
            row.Notes = "Parameter defined in Sheet1 does not exist in top-level assembly parameters.";
        }

        return row;
    }

    private ParameterComparisonRow EvaluateParameterMatch(
        ParameterComparisonRow row,
        InventorParameterItem invParam,
        MatchClassification matchType)
    {
        row.MatchType = matchType;
        row.TargetDocument = invParam.DocumentName;
        row.TargetParameterName = invParam.Name;
        row.ModelExpression = invParam.Expression;
        row.ModelUnits = invParam.Units;

        // Parse numerical value from expression or value
        if (TryParseDoubleFromExpression(invParam.Expression, out double parsedVal))
        {
            row.ModelNumericValue = parsedVal;
        }
        else
        {
            row.ModelNumericValue = ConvertCadValueToUnits(invParam.Value, invParam.Units);
        }

        // Safe-Write Policy Evaluation
        if (invParam.IsReference)
        {
            row.WritePolicy = SafeWriteClassification.ReadOnlyReference;
            row.Notes = "Driven reference parameter in CAD; read-only.";
        }
        else if (invParam.IsFormulaDriven)
        {
            row.WritePolicy = SafeWriteClassification.ReadOnlyFormula;
            row.Notes = $"Parametric formula protected: '{invParam.Expression}'. Cannot overwrite with literal.";
        }
        else if (matchType == MatchClassification.UniqueNormalizedMatch)
        {
            row.WritePolicy = SafeWriteClassification.NormalizedRequiresApproval;
            row.Notes = "Normalized-only name match; requires explicit user approval before apply.";
        }
        else
        {
            row.WritePolicy = SafeWriteClassification.SafeToWrite;
        }

        // Comparison for Inspect Mode
        if (row.ExcelNumericValue.HasValue && row.ModelNumericValue.HasValue)
        {
            double diff = Math.Abs(row.ExcelNumericValue.Value - row.ModelNumericValue.Value);
            row.Delta = diff;

            if (diff <= 0.0005)
            {
                row.Status = ComparisonStatus.Match;
                if (string.IsNullOrEmpty(row.Notes)) row.Notes = "CAD parameter matches Excel calculation.";
            }
            else
            {
                row.Status = ComparisonStatus.Discrepancy;
                string discMsg = $"Value discrepancy: Excel={row.ExcelNumericValue.Value:F4}, CAD={row.ModelNumericValue.Value:F4} (Delta={diff:F4})";
                row.Notes = string.IsNullOrEmpty(row.Notes) ? discMsg : $"{row.Notes} | {discMsg}";
            }
        }
        else
        {
            // String / non-numeric comparison
            if (string.Equals(row.ExcelValueRaw.Trim(), invParam.Expression.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                row.Status = ComparisonStatus.Match;
            }
            else
            {
                row.Status = ComparisonStatus.Discrepancy;
                string diffMsg = $"Expression difference: Excel='{row.ExcelValueRaw}', CAD='{invParam.Expression}'";
                row.Notes = string.IsNullOrEmpty(row.Notes) ? diffMsg : $"{row.Notes} | {diffMsg}";
            }
        }

        return row;
    }

    private ParameterComparisonRow EvaluateExpressionMatch(
        ParameterComparisonRow row,
        List<InventorParameterItem> candidates,
        Sheet1ParameterItem excelParam)
    {
        var primary = candidates[0];
        row.MatchType = MatchClassification.ExpressionDrivenMatch;
        row.TargetDocument = primary.DocumentName;
        row.TargetParameterName = primary.Name;
        row.ModelExpression = primary.Expression;
        row.ModelUnits = primary.Units;
        row.WritePolicy = SafeWriteClassification.ReadOnlyFormula;

        if (TryParseDoubleFromExpression(primary.Expression, out double parsedVal))
        {
            row.ModelNumericValue = parsedVal;
        }
        else
        {
            row.ModelNumericValue = ConvertCadValueToUnits(primary.Value, primary.Units);
        }

        string targetInfo = candidates.Count > 1
            ? $"CAD parameters ({string.Join(", ", candidates.Take(3).Select(c => c.Name))}{(candidates.Count > 3 ? $" +{candidates.Count - 3} more" : "")})"
            : $"CAD parameter '{primary.Name}'";

        if (row.ExcelNumericValue.HasValue && row.ModelNumericValue.HasValue)
        {
            double diff = Math.Abs(row.ExcelNumericValue.Value - row.ModelNumericValue.Value);
            row.Delta = diff;

            if (diff <= 0.0005)
            {
                row.Status = ComparisonStatus.Match;
                row.Notes = $"{targetInfo} expression references '{excelParam.ParameterName}' and value matches ({row.ModelNumericValue.Value:F4} {primary.Units}).";
            }
            else
            {
                row.Status = ComparisonStatus.Discrepancy;
                row.Notes = $"{targetInfo} expression references '{excelParam.ParameterName}' with value discrepancy: Excel={row.ExcelNumericValue.Value:F4}, CAD={row.ModelNumericValue.Value:F4} (Delta={diff:F4}).";
            }
        }
        else
        {
            row.Status = ComparisonStatus.Match;
            row.Notes = $"{targetInfo} expression references '{excelParam.ParameterName}'.";
        }

        return row;
    }

    private static ParameterComparisonRow CreateAmbiguousMatch(
        ParameterComparisonRow row,
        List<InventorParameterItem> candidates)
    {
        row.MatchType = MatchClassification.AmbiguousMatch;
        row.WritePolicy = SafeWriteClassification.AmbiguousTarget;
        row.Status = ComparisonStatus.Discrepancy;
        row.Notes = $"Ambiguous match: Found {candidates.Count} candidate parameters in model ({string.Join(", ", candidates.Select(c => c.Name))}).";
        return row;
    }

    private static bool TryMatchSuppression(
        Sheet1ParameterItem excelParam,
        Dictionary<string, OccurrenceInventoryItem> occurrencesByName,
        out OccurrenceInventoryItem? matchedOcc,
        out string mappedOccName)
    {
        matchedOcc = null;
        mappedOccName = string.Empty;

        var m = SuppressionParamRegex.Match(excelParam.ParameterName);
        if (!m.Success)
        {
            m = GeneralSuppressionParamRegex.Match(excelParam.ParameterName);
            if (!m.Success) return false;
        }

        string rawNum = m.Groups["num"].Value;
        string suffix = m.Groups["suffix"].Success ? m.Groups["suffix"].Value : "1";

        // Convert Part_091_30102_466_1 -> 091-30102-466:1
        // Replace underscores in number with hyphens
        string partNum = rawNum.Replace('_', '-');
        mappedOccName = $"{partNum}:{suffix}";

        if (occurrencesByName.TryGetValue(mappedOccName, out matchedOcc))
        {
            return true;
        }

        // Try direct name match without hyphen conversion
        string altName = $"{rawNum}:{suffix}";
        if (occurrencesByName.TryGetValue(altName, out matchedOcc))
        {
            mappedOccName = altName;
            return true;
        }

        // Try finding any occurrence starting with partNum: or matching PartNumber
        var prefixMatch = occurrencesByName.Values.FirstOrDefault(o =>
            o.OccurrenceName.StartsWith($"{partNum}:", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.PartNumber, partNum, StringComparison.OrdinalIgnoreCase));

        if (prefixMatch != null)
        {
            matchedOcc = prefixMatch;
            mappedOccName = prefixMatch.OccurrenceName;
            return true;
        }

        return true; // Recognizable suppression pattern, even if occurrence is not found in assembly
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return name.Trim().Replace("-", "").Replace("_", "").ToLowerInvariant();
    }

    private static bool TryParseDouble(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseDoubleFromExpression(string? expr, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(expr)) return false;
        var m = Regex.Match(expr.Trim(), @"^[-+]?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?");
        if (m.Success)
        {
            return double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
        return false;
    }

    private static List<OccurrenceInventoryItem> FlattenOccurrences(IEnumerable<OccurrenceInventoryItem> items)
    {
        var list = new List<OccurrenceInventoryItem>();
        foreach (var item in items)
        {
            list.Add(item);
            list.AddRange(FlattenOccurrences(item.Children));
        }
        return list;
    }

    private static double ConvertCadValueToUnits(double valInDbUnits, string units)
    {
        if (string.IsNullOrWhiteSpace(units)) return valInDbUnits;
        string u = units.Trim().ToLowerInvariant();
        if (u == "in" || u == "inch" || u == "inches")
        {
            return valInDbUnits / 2.54;
        }
        if (u == "ft" || u == "feet")
        {
            return valInDbUnits / 30.48;
        }
        if (u == "mm" || u == "millimeter" || u == "millimeters")
        {
            return valInDbUnits * 10.0;
        }
        if (u == "deg" || u == "degree" || u == "degrees")
        {
            return valInDbUnits * (180.0 / Math.PI);
        }
        return valInDbUnits;
    }
}
