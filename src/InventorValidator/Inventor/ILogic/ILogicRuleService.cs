using System.Runtime.InteropServices;
using System.Text;
using InventorValidator.Infrastructure;

namespace InventorValidator.Inventor.ILogic;

public class ILogicRuleService
{
    public const string ILogicAddInGuid = "{3BDD8D79-2179-4B11-8A5A-257B1C0263AC}";
    public static readonly string[] KnownILogicAddInGuids = new[]
    {
        "{3BDD8D79-2179-4B11-8A5A-257B1C0263AC}", // Standard Autodesk Inventor 2020/2024 ClassId
        "{3BDD8D7D-4001-448B-B80F-6C64FF6BEF02}"  // Legacy / alternate identifier
    };

    public const string RulesAttributeSetName = "iLogicInternalRules";
    public const string RuleListSetName = "iLogicRuleListSet";
    public const string RuleListAttributeName = "iLogicRuleList";

    /// <summary>
    /// Discovers all internal iLogic rules present in the specified document without hardcoded name assumptions.
    /// Uses the iLogic AddIn automation object when available, and falls back to inspecting document attribute sets.
    /// </summary>
    public virtual IReadOnlyList<string> GetAssemblyRules(object? doc)
    {
        var ruleNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (doc == null) return Array.Empty<string>();

        dynamic dDoc = doc;

        // 1. Primary: Query via iLogic AddIn Automation
        try
        {
            dynamic? app = null;
            try { app = dDoc.Parent; } catch { }
            if (app == null) { try { app = dDoc.Application; } catch { } }

            if (app != null)
            {
                dynamic? auto = GetILogicAutomation(app);
                if (auto != null)
                {
                    dynamic? rules = auto.Rules(dDoc);
                    if (rules != null)
                    {
                        try
                        {
                            foreach (dynamic rule in rules)
                            {
                                try
                                {
                                    string name = (string)rule.Name;
                                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                                    {
                                        ruleNames.Add(name);
                                    }
                                }
                                catch { }
                            }
                        }
                        catch
                        {
                            // Fall back to indexed loop if enumeration fails
                            try
                            {
                                int count = rules.Count;
                                for (int i = 1; i <= count; i++)
                                {
                                    try
                                    {
                                        dynamic rule = rules.Item[i];
                                        string name = (string)rule.Name;
                                        if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                                        {
                                            ruleNames.Add(name);
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not query iLogic automation rules: {ex.Message}");
        }

        // 2. Secondary: Query Document AttributeSets (iLogicRuleListSet and iLogicRule_*)
        if (ruleNames.Count == 0)
        {
            try
            {
                dynamic attrSets = dDoc.AttributeSets;
                if (attrSets != null)
                {
                    int setCount = attrSets.Count;
                    for (int i = 1; i <= setCount; i++)
                    {
                        dynamic set = attrSets.Item[i];
                        string setName = (string)set.Name;

                        // Check binary rule list set (iLogicRuleListSet -> iLogicRuleList)
                        if (string.Equals(setName, RuleListSetName, StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                dynamic attr = set.Item[RuleListAttributeName];
                                object? rawVal = null;
                                try { rawVal = attr?.Value; } catch { }
                                if (rawVal is byte[] bytes)
                                {
                                    var parsed = ParseRuleListBytes(bytes);
                                    foreach (var p in parsed)
                                    {
                                        if (!string.IsNullOrWhiteSpace(p) && seen.Add(p))
                                        {
                                            ruleNames.Add(p);
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        // Check legacy/alternate attribute set name
                        else if (string.Equals(setName, RulesAttributeSetName, StringComparison.OrdinalIgnoreCase))
                        {
                            int attrCount = set.Count;
                            for (int j = 1; j <= attrCount; j++)
                            {
                                try
                                {
                                    dynamic attr = set.Item[j];
                                    string ruleName = (string)attr.Name;
                                    if (!string.IsNullOrWhiteSpace(ruleName) && seen.Add(ruleName))
                                    {
                                        ruleNames.Add(ruleName);
                                    }
                                }
                                catch { }
                            }
                        }
                        // Check individual iLogicRule_<Name> sets as last resort
                        else if (setName.StartsWith("iLogicRule_", StringComparison.OrdinalIgnoreCase) &&
                                 !setName.Equals("iLogicRuleListSet", StringComparison.OrdinalIgnoreCase))
                        {
                            string rawName = setName.Substring("iLogicRule_".Length);
                            if (!string.IsNullOrWhiteSpace(rawName) && seen.Add(rawName))
                            {
                                ruleNames.Add(rawName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not inspect AttributeSets for iLogic rules: {ex.Message}");
            }
        }

        return ruleNames;
    }

    /// <summary>
    /// Parses the binary byte array stored in iLogicRuleListSet.iLogicRuleList.
    /// Layout: [1 byte version][4 bytes count][[4 bytes L1][L1 bytes name UTF-16][4 bytes L2][L2 bytes internalName UTF-16]]...
    /// </summary>
    public static List<string> ParseRuleListBytes(byte[] bytes)
    {
        var names = new List<string>();
        if (bytes == null || bytes.Length < 5)
            return names;

        try
        {
            int offset = 1; // skip 1-byte version
            int count = BitConverter.ToInt32(bytes, offset);
            offset += 4;

            for (int i = 0; i < count && offset + 4 <= bytes.Length; i++)
            {
                int nameLen = BitConverter.ToInt32(bytes, offset);
                offset += 4;

                if (nameLen > 0 && offset + nameLen <= bytes.Length)
                {
                    string ruleName = Encoding.Unicode.GetString(bytes, offset, nameLen);
                    names.Add(ruleName);
                    offset += nameLen;
                }

                // Skip internal storage name
                if (offset + 4 <= bytes.Length)
                {
                    int internalLen = BitConverter.ToInt32(bytes, offset);
                    offset += 4;
                    if (internalLen > 0 && offset + internalLen <= bytes.Length)
                    {
                        offset += internalLen;
                    }
                }
            }
        }
        catch { }

        return names;
    }

    /// <summary>
    /// Executes the specified iLogic rule in the document.
    /// </summary>
    public virtual bool RunRule(object? doc, string ruleName, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(ruleName))
        {
            errorMessage = "No rule name specified.";
            return false;
        }

        if (doc == null)
        {
            errorMessage = "Unable to access Autodesk Inventor Application instance from document.";
            return false;
        }

        dynamic dDoc = doc;

        try
        {
            dynamic? app = null;
            try { app = dDoc.Parent; } catch { }
            if (app == null) { try { app = dDoc.Application; } catch { } }

            if (app == null)
            {
                errorMessage = "Unable to access Autodesk Inventor Application instance from document.";
                return false;
            }

            dynamic? auto = GetILogicAutomation(app);
            if (auto == null)
            {
                errorMessage = "Autodesk Inventor iLogic Add-in is not available or could not be activated.";
                return false;
            }

            try
            {
                if (!auto.RulesEnabled)
                {
                    auto.RulesEnabled = true;
                }
            }
            catch { }

            try
            {
                auto.SilentOperation = true;
            }
            catch { }

            // Ensure document has at least one view and is activated so ThisApplication.ActiveDocument / ActiveView are valid
            try
            {
                dynamic views = dDoc.Views;
                if (views != null && views.Count == 0)
                {
                    views.Add();
                }
                dDoc.Activate();
            }
            catch { }

            // If the document contains an "iLogic" representation that is not currently active, activate it silently
            try
            {
                dynamic compDef = dDoc.ComponentDefinition;
                dynamic repMgr = compDef.RepresentationsManager;

                // Inventor 2020 LevelOfDetailRepresentations
                try
                {
                    dynamic activeLod = repMgr.ActiveLevelOfDetailRepresentation;
                    if (activeLod != null && !string.Equals((string)activeLod.Name, "iLogic", StringComparison.OrdinalIgnoreCase))
                    {
                        dynamic lods = repMgr.LevelOfDetailRepresentations;
                        foreach (dynamic lod in lods)
                        {
                            if (string.Equals((string)lod.Name, "iLogic", StringComparison.OrdinalIgnoreCase))
                            {
                                lod.Activate(true);
                                break;
                            }
                        }
                    }
                }
                catch { }

                // Inventor 2024 ModelStates
                try
                {
                    dynamic modelStates = compDef.ModelStates;
                    if (modelStates != null)
                    {
                        dynamic activeMs = modelStates.ActiveModelState;
                        if (activeMs != null && !string.Equals((string)activeMs.Name, "iLogic", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (dynamic ms in modelStates)
                            {
                                if (string.Equals((string)ms.Name, "iLogic", StringComparison.OrdinalIgnoreCase))
                                {
                                    ms.Activate();
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }

            // Temporarily deactivate Autodesk Vault Add-in during rule execution if active
            // to avoid massive per-component Vault status network check overhead during suppression changes
            dynamic? vaultAddIn = null;
            bool wasVaultActive = false;
            try
            {
                vaultAddIn = app.ApplicationAddIns.ItemById["{48B682BC-42E6-4953-84C5-3D253B52E77B}"];
                if (vaultAddIn != null && vaultAddIn.Activated)
                {
                    wasVaultActive = true;
                    vaultAddIn.Deactivate();
                }
            }
            catch { }

            try
            {
                DiagnosticsLogger.Instance.Info($"Executing iLogic rule '{ruleName}' in document '{dDoc.DisplayName}'...");
                int result = auto.RunRule(dDoc, ruleName);

                if (result != 0)
                {
                    errorMessage = $"iLogic rule '{ruleName}' exited with return code: {result}";
                    DiagnosticsLogger.Instance.Warn(errorMessage);
                    return false;
                }

                DiagnosticsLogger.Instance.Success($"Successfully executed iLogic rule '{ruleName}'.");
                return true;
            }
            finally
            {
                if (wasVaultActive && vaultAddIn != null)
                {
                    try { vaultAddIn.Activate(); } catch { }
                }
            }
        }
        catch (COMException comEx)
        {
            errorMessage = $"iLogic rule '{ruleName}' COM error (0x{comEx.ErrorCode:X8}): {comEx.Message}";
            DiagnosticsLogger.Instance.Error(errorMessage, comEx);
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"iLogic rule '{ruleName}' execution error: {ex.Message}";
            DiagnosticsLogger.Instance.Error(errorMessage, ex);
            return false;
        }
    }

    /// <summary>
    /// Executes all internal iLogic rules present in the document in order.
    /// </summary>
    public virtual bool RunAllRules(object? doc, out string? errorMessage)
    {
        errorMessage = null;
        var rules = GetAssemblyRules(doc);
        if (rules.Count == 0)
        {
            errorMessage = "No iLogic rules detected in document.";
            return false;
        }

        DiagnosticsLogger.Instance.Info($"Executing all {rules.Count} iLogic rule(s) in sequence: {string.Join(", ", rules)}");
        var errors = new List<string>();

        foreach (var rule in rules)
        {
            bool success = RunRule(doc, rule, out string? err);
            if (!success && !string.IsNullOrEmpty(err))
            {
                errors.Add(err);
            }
        }

        if (errors.Count > 0)
        {
            errorMessage = $"Encountered issues running rules: {string.Join("; ", errors)}";
            return false;
        }

        return true;
    }

    private static dynamic? GetILogicAutomation(dynamic app)
    {
        try
        {
            dynamic addIns = app.ApplicationAddIns;
            dynamic? iLogicAddIn = null;

            // 1. Try ItemById lookup for known GUIDs
            foreach (var guid in KnownILogicAddInGuids)
            {
                try
                {
                    iLogicAddIn = addIns.ItemById[guid];
                    if (iLogicAddIn != null) break;
                }
                catch { }
            }

            // 2. Try scanning all addins by ClassIdString
            if (iLogicAddIn == null)
            {
                int count = addIns.Count;
                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        dynamic item = addIns.Item[i];
                        string id = (string)item.ClassIdString;
                        if (KnownILogicAddInGuids.Any(g => string.Equals(id, g, StringComparison.OrdinalIgnoreCase)))
                        {
                            iLogicAddIn = item;
                            break;
                        }
                    }
                    catch { }
                }
            }

            // 3. Fallback: Search by DisplayName ("iLogic")
            if (iLogicAddIn == null)
            {
                int count = addIns.Count;
                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        dynamic item = addIns.Item[i];
                        string disp = (string)item.DisplayName;
                        if (string.Equals(disp, "iLogic", StringComparison.OrdinalIgnoreCase) ||
                            disp.IndexOf("iLogic", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            iLogicAddIn = item;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (iLogicAddIn == null)
            {
                DiagnosticsLogger.Instance.Warn("Autodesk Inventor iLogic Add-in could not be located in ApplicationAddIns.");
                return null;
            }

            if (!iLogicAddIn.Activated)
            {
                DiagnosticsLogger.Instance.Info("Activating iLogic Add-in...");
                iLogicAddIn.Activate();
            }

            dynamic auto = iLogicAddIn.Automation;
            if (auto == null)
            {
                DiagnosticsLogger.Instance.Warn("iLogic Add-in is activated but its Automation object is null.");
            }
            return auto;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Failed to retrieve iLogic Automation: {ex.Message}");
            return null;
        }
    }
}
