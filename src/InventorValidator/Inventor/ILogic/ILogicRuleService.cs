using System.Runtime.InteropServices;
using InventorValidator.Infrastructure;

namespace InventorValidator.Inventor.ILogic;

public class ILogicRuleService
{
    public const string ILogicAddInGuid = "{3BDD8D7D-4001-448B-B80F-6C64FF6BEF02}";
    public const string RulesAttributeSetName = "iLogicInternalRules";

    /// <summary>
    /// Discovers all internal iLogic rules present in the specified document without hardcoded name assumptions.
    /// Uses the iLogic AddIn automation object when available, and falls back to inspecting document attribute sets.
    /// </summary>
    public virtual IReadOnlyList<string> GetAssemblyRules(object? doc)
    {
        var ruleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                                    if (!string.IsNullOrWhiteSpace(name))
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
                                        if (!string.IsNullOrWhiteSpace(name))
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

        // 2. Secondary: Query Document AttributeSets ("iLogicInternalRules")
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
                    if (string.Equals(setName, RulesAttributeSetName, StringComparison.OrdinalIgnoreCase))
                    {
                        int attrCount = set.Count;
                        for (int j = 1; j <= attrCount; j++)
                        {
                            try
                            {
                                dynamic attr = set.Item[j];
                                string ruleName = (string)attr.Name;
                                if (!string.IsNullOrWhiteSpace(ruleName))
                                {
                                    ruleNames.Add(ruleName);
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
            DiagnosticsLogger.Instance.Warn($"Could not inspect AttributeSets for iLogic rules: {ex.Message}");
        }

        return ruleNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
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

    private static dynamic? GetILogicAutomation(dynamic app)
    {
        try
        {
            dynamic addIns = app.ApplicationAddIns;
            dynamic? iLogicAddIn = null;

            try
            {
                iLogicAddIn = addIns.ItemById[ILogicAddInGuid];
            }
            catch
            {
                // Try case-insensitive scan
                int count = addIns.Count;
                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        dynamic item = addIns.Item[i];
                        string id = (string)item.ClassIdString;
                        if (string.Equals(id, ILogicAddInGuid, StringComparison.OrdinalIgnoreCase))
                        {
                            iLogicAddIn = item;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (iLogicAddIn == null)
                return null;

            if (!iLogicAddIn.Activated)
            {
                iLogicAddIn.Activate();
            }

            return iLogicAddIn.Automation;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Failed to retrieve iLogic Automation: {ex.Message}");
            return null;
        }
    }
}
