using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using InventorValidator.Infrastructure;
using InventorValidator.Inventor.ILogic;
using InventorValidator.Inventor.Models;

namespace InventorValidator.Inventor;

/// <summary>
/// Extracts deep assembly metadata, occurrence hierarchy, parameters, and features
/// from an open Inventor AssemblyDocument.
/// </summary>
public class InventorModelInventoryService
{
    private static readonly Regex NumericLiteralRegex = new(
        @"^[-+]?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?\s*([a-zA-Z]+)?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Performs a full recursive inventory of an open AssemblyDocument.
    /// </summary>
    public AssemblyInventoryResult InventoryAssembly(
        dynamic asmDoc,
        string inventorVersionString,
        int processId,
        ComReleaseScope scope,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        progress?.Report("Reading assembly definition and representations...");

        var result = new AssemblyInventoryResult
        {
            TopAssemblyPath = (string)asmDoc.FullFileName,
            AssemblyName = (string)asmDoc.DisplayName,
            InventorVersion = inventorVersionString,
            ProcessId = processId
        };

        dynamic compDef = scope.Track<object>(asmDoc.ComponentDefinition);

        // 1. Representations (LOD / Model States)
        InventoryRepresentations(compDef, result, scope);

        cancellationToken.ThrowIfCancellationRequested();

        // 2. Parameters (Top-Level Model, User, and Reference parameters)
        progress?.Report("Inventorying assembly parameters...");
        InventoryParameters(compDef, result, scope);

        cancellationToken.ThrowIfCancellationRequested();

        // 2b. Available iLogic Rules (Dynamic discovery without hardcoded names)
        try
        {
            var ruleService = new ILogicRuleService();
            result.AvailableRules = ruleService.GetAssemblyRules((object)asmDoc).ToList();
            if (result.AvailableRules.Count > 0)
            {
                DiagnosticsLogger.Instance.Info($"Discovered {result.AvailableRules.Count} iLogic rule(s) in assembly: {string.Join(", ", result.AvailableRules)}");
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not inventory iLogic rules: {ex.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 3. Occurrences, Features & Deep Parameters (Recursive traversal with unique document caching)
        progress?.Report("Inventorying component occurrences and features...");
        var uniqueDocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partDocFeatureCache = new Dictionary<string, List<InventorFeatureItem>>(StringComparer.OrdinalIgnoreCase);
        var partNumberCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var occurrenceMetadataCache = new Dictionary<string, (string DocPath, string PartNumber, bool IsAssembly, List<InventorFeatureItem> Features)>(StringComparer.OrdinalIgnoreCase);

        uniqueDocs.Add(result.TopAssemblyPath);

        var timings = new InventoryTimings();
        dynamic occurrences = scope.Track<object>(compDef.Occurrences);
        int occCount = occurrences.Count;

        for (int i = 1; i <= occCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic occ = scope.Track<object>(occurrences.Item[i]);
            var occItem = InventoryOccurrenceRecursive(
                occ, 
                1, 
                string.Empty, 
                uniqueDocs, 
                partDocFeatureCache, 
                partNumberCache,
                occurrenceMetadataCache,
                result, 
                scope, 
                timings,
                cancellationToken);

            result.Occurrences.Add(occItem);
        }

        result.TotalDocumentsCount = uniqueDocs.Count;
        result.TotalOccurrencesCount = result.ActiveOccurrencesCount + result.SuppressedOccurrencesCount;
        result.ExtractionDuration = sw.Elapsed;

        progress?.Report($"[TIMING BREAKDOWN] PartNumbers: {timings.PnMs}ms, Features: {timings.FeatMs}ms, OccLookup: {timings.OccLookupMs}ms");

        DiagnosticsLogger.Instance.Success(
            $"Assembly inventory complete: {result.Occurrences.Count} top occurrences ({result.TotalOccurrencesCount} total, {result.ActiveOccurrencesCount} active), " +
            $"{result.Parameters.Count} parameters, {result.Features.Count} features, {result.TotalDocumentsCount} unique documents in {sw.Elapsed.TotalSeconds:F2}s " +
            $"(PartNumbers: {timings.PnMs}ms, Features: {timings.FeatMs}ms, OccLookup: {timings.OccLookupMs}ms)");

        return result;
    }

    private void InventoryRepresentations(dynamic compDef, AssemblyInventoryResult result, ComReleaseScope scope)
    {
        try
        {
            dynamic repMgr = scope.Track<object>(compDef.RepresentationsManager);

            // Check Inventor 2020 LevelOfDetailRepresentations
            try
            {
                dynamic lodReps = scope.Track<object>(repMgr.LevelOfDetailRepresentations);
                int count = lodReps.Count;
                dynamic activeLod = scope.Track<object>(repMgr.ActiveLevelOfDetailRepresentation);
                string activeName = (string)activeLod.Name;
                result.ActiveRepresentation = activeName;

                for (int i = 1; i <= count; i++)
                {
                    dynamic lod = scope.Track<object>(lodReps.Item[i]);
                    string name = (string)lod.Name;
                    bool isActive = string.Equals(name, activeName, StringComparison.OrdinalIgnoreCase);

                    if (string.Equals(name, "iLogic", StringComparison.OrdinalIgnoreCase))
                    {
                        result.HasILogicRepresentation = true;
                    }

                    result.Representations.Add(new InventorRepresentationItem
                    {
                        Name = name,
                        RepresentationType = "LOD",
                        IsActive = isActive
                    });
                }
            }
            catch
            {
                // LOD not supported or running under Inventor 2024 ModelStates
            }

            // Check Inventor 2024 ModelStates if representations list is empty
            if (result.Representations.Count == 0)
            {
                try
                {
                    dynamic modelStates = scope.Track<object>(compDef.ModelStates);
                    int count = modelStates.Count;
                    dynamic activeMs = scope.Track<object>(compDef.ActiveModelState);
                    string activeName = (string)activeMs.Name;
                    result.ActiveRepresentation = activeName;

                    for (int i = 1; i <= count; i++)
                    {
                        dynamic ms = scope.Track<object>(modelStates.Item[i]);
                        string name = (string)ms.Name;
                        bool isActive = string.Equals(name, activeName, StringComparison.OrdinalIgnoreCase);

                        if (string.Equals(name, "iLogic", StringComparison.OrdinalIgnoreCase))
                        {
                            result.HasILogicRepresentation = true;
                        }

                        result.Representations.Add(new InventorRepresentationItem
                        {
                            Name = name,
                            RepresentationType = "ModelState",
                            IsActive = isActive
                        });
                    }
                }
                catch
                {
                    // ModelStates query fallback
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not inventory representations: {ex.Message}");
        }
    }

    private void InventoryParameters(dynamic compDef, AssemblyInventoryResult result, ComReleaseScope scope)
    {
        try
        {
            dynamic parameters = scope.Track<object>(compDef.Parameters);

            // User Parameters
            try
            {
                dynamic userParams = scope.Track<object>(parameters.UserParameters);
                int uCount = userParams.Count;
                for (int i = 1; i <= uCount; i++)
                {
                    dynamic p = scope.Track<object>(userParams.Item[i]);
                    result.Parameters.Add(ExtractParameter(p, "User", result.TopAssemblyPath, result.AssemblyName));
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not read UserParameters: {ex.Message}");
            }

            // Model Parameters
            try
            {
                dynamic modelParams = scope.Track<object>(parameters.ModelParameters);
                int mCount = modelParams.Count;
                for (int i = 1; i <= mCount; i++)
                {
                    dynamic p = scope.Track<object>(modelParams.Item[i]);
                    result.Parameters.Add(ExtractParameter(p, "Model", result.TopAssemblyPath, result.AssemblyName));
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not read ModelParameters: {ex.Message}");
            }

            // Reference Parameters
            try
            {
                dynamic refParams = scope.Track<object>(parameters.ReferenceParameters);
                int rCount = refParams.Count;
                for (int i = 1; i <= rCount; i++)
                {
                    dynamic p = scope.Track<object>(refParams.Item[i]);
                    result.Parameters.Add(ExtractParameter(p, "Reference", result.TopAssemblyPath, result.AssemblyName));
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not read ReferenceParameters: {ex.Message}");
            }

            // Table Parameters (e.g. linked Excel spreadsheets)
            try
            {
                dynamic paramTables = scope.Track<object>(parameters.ParameterTables);
                int tCount = paramTables.Count;
                for (int t = 1; t <= tCount; t++)
                {
                    dynamic table = scope.Track<object>(paramTables.Item[t]);
                    dynamic tableParams = scope.Track<object>(table.TableParameters);
                    int pCount = tableParams.Count;
                    for (int i = 1; i <= pCount; i++)
                    {
                        dynamic p = scope.Track<object>(tableParams.Item[i]);
                        result.Parameters.Add(ExtractParameter(p, "Table", result.TopAssemblyPath, result.AssemblyName));
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not read TableParameters: {ex.Message}");
            }

            // Derived Parameters
            try
            {
                dynamic derivedTables = scope.Track<object>(parameters.DerivedParameterTables);
                int dtCount = derivedTables.Count;
                for (int dt = 1; dt <= dtCount; dt++)
                {
                    dynamic derivedTable = scope.Track<object>(derivedTables.Item[dt]);
                    dynamic derivedParams = scope.Track<object>(derivedTable.DerivedParameters);
                    int pCount = derivedParams.Count;
                    for (int i = 1; i <= pCount; i++)
                    {
                        dynamic p = scope.Track<object>(derivedParams.Item[i]);
                        result.Parameters.Add(ExtractParameter(p, "Derived", result.TopAssemblyPath, result.AssemblyName));
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not read DerivedParameters: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Error($"Error inventorying assembly parameters: {ex.Message}", ex);
        }
    }

    private static InventorParameterItem ExtractParameter(dynamic p, string paramType, string docPath, string docName)
    {
        string name = (string)p.Name;
        string expression = string.Empty;
        try { expression = (string)p.Expression; } catch { try { expression = p.Value?.ToString() ?? string.Empty; } catch { } }
        double val = 0.0;
        try { val = (double)p.Value; } catch { }
        string units = string.Empty;
        string? comment = null;

        try { units = (string)p.Units; } catch { }
        if (string.Equals(paramType, "User", StringComparison.OrdinalIgnoreCase))
        {
            try { comment = (string)p.Comment; } catch { }
        }

        bool isReference = string.Equals(paramType, "Reference", StringComparison.OrdinalIgnoreCase);
        bool isFormulaDriven = !isReference && !IsLiteralNumericExpression(expression);

        return new InventorParameterItem
        {
            Name = name,
            DocumentPath = docPath,
            DocumentName = docName,
            ParameterType = paramType,
            Expression = expression,
            Value = val,
            Units = units,
            Comment = comment,
            IsFormulaDriven = isFormulaDriven,
            IsReference = isReference
        };
    }

    private static bool IsLiteralNumericExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;
        string trimmed = expression.Trim();
        return NumericLiteralRegex.IsMatch(trimmed);
    }

    private OccurrenceInventoryItem InventoryOccurrenceRecursive(
        dynamic occ,
        int depth,
        string parentPath,
        HashSet<string> uniqueDocs,
        Dictionary<string, List<InventorFeatureItem>> partDocFeatureCache,
        Dictionary<string, string> partNumberCache,
        Dictionary<string, (string DocPath, string PartNumber, bool IsAssembly, List<InventorFeatureItem> Features)> occurrenceMetadataCache,
        AssemblyInventoryResult result,
        ComReleaseScope scope,
        InventoryTimings timings,
        CancellationToken cancellationToken)
    {
        var swOcc = Stopwatch.StartNew();
        string occName = (string)occ.Name;
        string fullPath = string.IsNullOrEmpty(parentPath) ? occName : $"{parentPath}/{occName}";
        bool isSuppressed = false;

        try
        {
            isSuppressed = (bool)occ.Suppressed;
        }
        catch
        {
            // Some occurrences may report suppression via error or alternate property
        }
        timings.OccLookupMs += swOcc.ElapsedMilliseconds;

        var item = new OccurrenceInventoryItem
        {
            OccurrenceName = occName,
            OccurrencePath = fullPath,
            IsSuppressed = isSuppressed,
            DepthLevel = depth
        };

        if (isSuppressed)
        {
            result.SuppressedOccurrencesCount++;
            // Suppressed occurrences do not have active document or definition handles
            item.PartNumber = ExtractPartNumberFromOccurrenceName(occName);
            return item;
        }

        result.ActiveOccurrencesCount++;

        string occPrefix = ExtractPartNumberFromOccurrenceName(occName);

        // Check if we have already inventoried this occurrence definition
        if (occurrenceMetadataCache.TryGetValue(occPrefix, out var cachedMeta))
        {
            item.DocumentPath = cachedMeta.DocPath;
            item.PartNumber = cachedMeta.PartNumber;

            if (cachedMeta.Features != null && cachedMeta.Features.Count > 0)
            {
                foreach (var f in cachedMeta.Features)
                {
                    result.Features.Add(new InventorFeatureItem
                    {
                        FeatureName = f.FeatureName,
                        FeatureType = f.FeatureType,
                        ContainingOccurrence = occName,
                        DocumentPath = cachedMeta.DocPath,
                        ElementCount = f.ElementCount,
                        HoleDiameter = f.HoleDiameter,
                        IsSuppressed = f.IsSuppressed
                    });
                }
            }

            // Only subassemblies can have sub-occurrences
            if (cachedMeta.IsAssembly)
            {
                try
                {
                    dynamic subOccs = scope.Track<object>(occ.SubOccurrences);
                    int subCount = subOccs.Count;

                    for (int s = 1; s <= subCount; s++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        dynamic subOcc = scope.Track<object>(subOccs.Item[s]);
                        var subItem = InventoryOccurrenceRecursive(
                            subOcc,
                            depth + 1,
                            fullPath,
                            uniqueDocs,
                            partDocFeatureCache,
                            partNumberCache,
                            occurrenceMetadataCache,
                            result,
                            scope,
                            timings,
                            cancellationToken);

                        item.Children.Add(subItem);
                    }
                }
                catch { }
            }

            return item;
        }

        string docPath = string.Empty;
        bool isAssembly = false;
        List<InventorFeatureItem>? extractedFeatures = null;

        try
        {
            int docType = 0;
            try { docType = (int)occ.DefinitionDocumentType; } catch { }
            isAssembly = docType == 12291; // kAssemblyDocumentObject

            dynamic def = scope.Track<object>(occ.Definition);
            dynamic doc = scope.Track<object>(def.Document);
            docPath = (string)doc.FullFileName;

            string pn = string.Empty;
            if (!string.IsNullOrEmpty(docPath))
            {
                uniqueDocs.Add(docPath);
                item.DocumentPath = docPath;

                // Cached Part Number extraction
                var swPn = Stopwatch.StartNew();
                if (!partNumberCache.TryGetValue(docPath, out pn!))
                {
                    pn = ExtractPartNumber(doc, occName, scope);
                    partNumberCache[docPath] = pn;
                }
                item.PartNumber = pn;
                timings.PnMs += swPn.ElapsedMilliseconds;
            }
            else
            {
                pn = ExtractPartNumber(doc, occName, scope);
                item.PartNumber = pn;
            }

            // Feature extraction for channel parts
            if (!string.IsNullOrEmpty(docPath) && IsPotentialChannelComponent(occName, docPath))
            {
                var swFeat = Stopwatch.StartNew();
                if (!partDocFeatureCache.TryGetValue(docPath, out extractedFeatures!))
                {
                    extractedFeatures = isAssembly ? new List<InventorFeatureItem>() : ExtractPartFeatures(def, docPath, scope);
                    partDocFeatureCache[docPath] = extractedFeatures;
                }
                timings.FeatMs += swFeat.ElapsedMilliseconds;

                // Associate feature occurrences with results
                foreach (var f in extractedFeatures)
                {
                    result.Features.Add(new InventorFeatureItem
                    {
                        FeatureName = f.FeatureName,
                        FeatureType = f.FeatureType,
                        ContainingOccurrence = occName,
                        DocumentPath = docPath,
                        ElementCount = f.ElementCount,
                        HoleDiameter = f.HoleDiameter,
                        IsSuppressed = f.IsSuppressed
                    });
                }
            }

            // Record in metadata cache
            occurrenceMetadataCache[occPrefix] = (docPath, item.PartNumber, isAssembly, extractedFeatures ?? new List<InventorFeatureItem>());

            // Sub-occurrences (only for assemblies)
            if (isAssembly)
            {
                try
                {
                    dynamic subOccs = scope.Track<object>(occ.SubOccurrences);
                    int subCount = subOccs.Count;

                    for (int s = 1; s <= subCount; s++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        dynamic subOcc = scope.Track<object>(subOccs.Item[s]);
                        var subItem = InventoryOccurrenceRecursive(
                            subOcc,
                            depth + 1,
                            fullPath,
                            uniqueDocs,
                            partDocFeatureCache,
                            partNumberCache,
                            occurrenceMetadataCache,
                            result,
                            scope,
                            timings,
                            cancellationToken);

                        item.Children.Add(subItem);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Error querying occurrence '{occName}': {ex.Message}");
            item.PartNumber = ExtractPartNumberFromOccurrenceName(occName);
        }

        return item;
    }

    private static string ExtractPartNumber(dynamic doc, string occName, ComReleaseScope scope)
    {
        try
        {
            dynamic propSets = scope.Track<object>(doc.PropertySets);
            dynamic designProps = scope.Track<object>(propSets.Item["Design Tracking Properties"]);
            dynamic pnProp = scope.Track<object>(designProps.Item["Part Number"]);
            string pn = (string)pnProp.Value;
            if (!string.IsNullOrWhiteSpace(pn))
            {
                return pn.Trim();
            }
        }
        catch
        {
            // Fall through
        }

        return ExtractPartNumberFromOccurrenceName(occName);
    }

    private static string ExtractPartNumberFromOccurrenceName(string occName)
    {
        // e.g. "091-30102-466:1" -> "091-30102-466"
        int colonIdx = occName.LastIndexOf(':');
        if (colonIdx > 0)
        {
            return occName.Substring(0, colonIdx).Trim();
        }
        return occName.Trim();
    }

    private static bool IsPotentialChannelComponent(string occName, string docPath)
    {
        return occName.Contains("chan", StringComparison.OrdinalIgnoreCase) ||
               docPath.Contains("chan", StringComparison.OrdinalIgnoreCase);
    }

    private List<InventorFeatureItem> ExtractPartFeatures(dynamic partDef, string docPath, ComReleaseScope scope)
    {
        var list = new List<InventorFeatureItem>();

        try
        {
            dynamic features = scope.Track<object>(partDef.Features);

            // Hole Features
            try
            {
                dynamic holeFeatures = scope.Track<object>(features.HoleFeatures);
                int hCount = holeFeatures.Count;
                for (int i = 1; i <= hCount; i++)
                {
                    dynamic hf = scope.Track<object>(holeFeatures.Item[i]);
                    string name = (string)hf.Name;
                    bool isSuppressed = false;
                    try { isSuppressed = (bool)hf.Suppressed; } catch { }

                    double? diameter = null;
                    try
                    {
                        bool isTapped = false;
                        try { isTapped = (bool)hf.Tapped; } catch { }
                        if (!isTapped)
                        {
                            dynamic holeParam = hf.HoleDiameter;
                            if (holeParam != null)
                            {
                                diameter = ((double)holeParam.Value) / 2.54;
                            }
                        }
                    }
                    catch { }

                    list.Add(new InventorFeatureItem
                    {
                        FeatureName = name,
                        FeatureType = "HoleFeature",
                        DocumentPath = docPath,
                        HoleDiameter = diameter,
                        IsSuppressed = isSuppressed,
                        ElementCount = 1
                    });
                }
            }
            catch { }

            // Rectangular Pattern Features
            try
            {
                dynamic patFeatures = scope.Track<object>(features.RectangularPatternFeatures);
                int pCount = patFeatures.Count;
                for (int i = 1; i <= pCount; i++)
                {
                    dynamic pf = scope.Track<object>(patFeatures.Item[i]);
                    string name = (string)pf.Name;
                    bool isSuppressed = false;
                    try { isSuppressed = (bool)pf.Suppressed; } catch { }

                    int elementCount = 1;
                    try
                    {
                        dynamic elements = pf.PatternElements;
                        elementCount = elements.Count;
                    }
                    catch { }

                    list.Add(new InventorFeatureItem
                    {
                        FeatureName = name,
                        FeatureType = "RectangularPatternFeature",
                        DocumentPath = docPath,
                        IsSuppressed = isSuppressed,
                        ElementCount = elementCount
                    });
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not extract features from '{Path.GetFileName(docPath)}': {ex.Message}");
        }

        return list;
    }

    private class InventoryTimings
    {
        public long PnMs;
        public long FeatMs;
        public long OccLookupMs;
    }
}

