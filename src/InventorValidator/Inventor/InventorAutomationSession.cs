using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using InventorValidator.Excel;
using InventorValidator.Geometry;
using InventorValidator.Geometry.Models;
using InventorValidator.Infrastructure;
using InventorValidator.Inventor.ILogic;
using InventorValidator.Inventor.Models;

namespace InventorValidator.Inventor;

/// <summary>
/// Manages the complete lifecycle of a dedicated Autodesk Inventor automation session on a persistent STA thread with a work queue.
/// Keeps the instance active for interactive inspection, and guarantees clean teardown of app-owned processes in their native apartment.
/// </summary>
public sealed class InventorAutomationSession : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;
    private const int WindowsSizeMaximize = 32514;

    private readonly InventorProcessLauncher _launcher;
    private readonly InventorModelInventoryService _inventoryService;
    private readonly InventorVisualOverlayService _overlayService = new();
    private readonly ComReleaseScope _sessionScope = new();

    private readonly BlockingCollection<Action> _staWorkQueue = new();
    private readonly Thread _staThread;
    private readonly ManualResetEventSlim _threadReady = new(false);

    private dynamic? _inventorApp;
    private dynamic? _activeAssemblyDoc;
    private InventorProcessInfo? _processInfo;
    private bool _isDisposed;

    public InventorProcessInfo? ProcessInfo => _processInfo;
    public bool IsActive => _inventorApp != null && !_isDisposed;

    public InventorAutomationSession(
        InventorProcessLauncher? launcher = null,
        InventorModelInventoryService? inventoryService = null)
    {
        _launcher = launcher ?? new InventorProcessLauncher();
        _inventoryService = inventoryService ?? new InventorModelInventoryService();

        _staThread = new Thread(RunStaThread)
        {
            IsBackground = true,
            Name = "InventorAutomationSTA"
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        _threadReady.Wait();
    }

    private void RunStaThread()
    {
        _threadReady.Set();
        foreach (var action in _staWorkQueue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Error($"Error executing action on STA thread: {ex.Message}", ex);
            }
        }
    }

    private Task<T> ExecuteOnStaAsync<T>(Func<T> func)
    {
        if (_isDisposed || _staWorkQueue.IsAddingCompleted)
            throw new InvalidOperationException("Inventor automation session is closed.");

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _staWorkQueue.Add(() =>
        {
            try
            {
                var res = func();
                tcs.SetResult(res);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// Starts dedicated Inventor, opens the copied assembly, optionally repoints linked Excel spreadsheets, and inventories the model on the dedicated STA thread.
    /// </summary>
    public async Task<AssemblyInventoryResult> StartAndInventoryAsync(
        string iamPath,
        string requestedVersion = "Automatic",
        bool isVisible = false,
        TimeSpan? timeout = null,
        IProgress<string>? progress = null,
        string? repointCalculatorPath = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(300);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(effectiveTimeout);

        var task = ExecuteOnStaAsync(() =>
        {
            return StartAndInventoryInternal(iamPath, requestedVersion, isVisible, effectiveTimeout, progress, repointCalculatorPath, linkedCts.Token);
        });

        var timeoutTask = Task.Delay(effectiveTimeout, cancellationToken);
        var completedTask = await Task.WhenAny(task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            throw new TimeoutException($"Inventor automation session timed out after {effectiveTimeout.TotalSeconds:F0} seconds.");
        }

        return await task;
    }

    private AssemblyInventoryResult StartAndInventoryInternal(
        string iamPath,
        string requestedVersion,
        bool isVisible,
        TimeSpan timeout,
        IProgress<string>? progress,
        string? repointCalculatorPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(iamPath))
        {
            throw new FileNotFoundException($"Assembly file not found: {iamPath}", iamPath);
        }

        int targetYear = _launcher.ResolveTargetVersion(requestedVersion);

        bool canReuse = false;
        if (_inventorApp != null && !_isDisposed && _processInfo != null && _processInfo.MajorVersion == targetYear)
        {
            try
            {
                var proc = Process.GetProcessById(_processInfo.ProcessId);
                if (!proc.HasExited)
                {
                    canReuse = true;
                }
            }
            catch { }
        }

        if (canReuse)
        {
            progress?.Report($"Reusing active Autodesk Inventor {_processInfo!.MajorVersion} session (PID {_processInfo.ProcessId})...");
            DiagnosticsLogger.Instance.Info($"Reusing existing Inventor {_processInfo.MajorVersion} instance PID {_processInfo.ProcessId}");

            // Close any currently open assembly document
            if (_activeAssemblyDoc != null)
            {
                try
                {
                    _activeAssemblyDoc.Close(true);
                }
                catch { }
                _activeAssemblyDoc = null;
            }

            try
            {
                _inventorApp.Visible = isVisible;
            }
            catch { }
        }
        else
        {
            // Close any stale session objects
            if (_activeAssemblyDoc != null)
            {
                try { _activeAssemblyDoc.Close(true); } catch { }
                _activeAssemblyDoc = null;
            }

            progress?.Report($"Starting dedicated Autodesk Inventor {targetYear}...");

            cancellationToken.ThrowIfCancellationRequested();

            // Launch dedicated instance directly on STA thread
            var (app, info) = _launcher.LaunchDedicatedInstance(
                targetYear,
                _sessionScope,
                isVisible,
                progress,
                cancellationToken);

            _inventorApp = app;
            _processInfo = info;
        }

        cancellationToken.ThrowIfCancellationRequested();

        progress?.Report($"Opening copied assembly: {Path.GetFileName(iamPath)}...");
        DiagnosticsLogger.Instance.Info($"Opening assembly in Inventor PID {_processInfo.ProcessId} (Visible={isVisible}): {iamPath}");

        // Re-assert SilentOperation to ensure all dialogs are suppressed during document open
        try
        {
            _inventorApp.SilentOperation = true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not assert SilentOperation = true: {ex.Message}");
        }

        dynamic documents = _sessionScope.Track<object>(_inventorApp.Documents);
        dynamic? asmDoc = null;

        // Attempt to open with "iLogic" LevelOfDetail or ModelState representation via OpenWithOptions if present
        try
        {
            dynamic fm = _inventorApp.FileManager;
            bool hasILogicRep = false;
            string repKey = "LevelOfDetailRepresentation";

            try
            {
                object lodsObj = fm.GetLevelOfDetailRepresentations(iamPath);
                if (lodsObj is string[] lodArr && lodArr.Any(l => string.Equals(l, "iLogic", StringComparison.OrdinalIgnoreCase)))
                {
                    hasILogicRep = true;
                    repKey = "LevelOfDetailRepresentation";
                }
            }
            catch { }

            if (hasILogicRep)
            {
                dynamic options = _inventorApp.TransientObjects.CreateNameValueMap();
                options.Add(repKey, "iLogic");
                asmDoc = _sessionScope.Track<object>(documents.OpenWithOptions(iamPath, options, isVisible));
                DiagnosticsLogger.Instance.Info($"Opened assembly in '{repKey}=iLogic' representation.");
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not open assembly with representation options: {ex.Message}; falling back to standard Open...");
        }

        if (asmDoc == null)
        {
            asmDoc = _sessionScope.Track<object>(documents.Open(iamPath, isVisible));
        }

        if (asmDoc == null)
        {
            throw new InvalidOperationException($"Inventor failed to open assembly: {iamPath}");
        }
        _activeAssemblyDoc = asmDoc;

        // Ensure document has an active view and is activated so ThisApplication.ActiveDocument & ActiveView are valid for rules and automation
        try
        {
            dynamic views = asmDoc.Views;
            if (views != null && views.Count == 0)
            {
                views.Add();
            }
            asmDoc.Activate();
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not ensure document view and activation: {ex.Message}");
        }

        // Repoint linked OLE Excel spreadsheets if a calculator workbook is specified
        if (!string.IsNullOrEmpty(repointCalculatorPath))
        {
            progress?.Report($"Repointing linked Excel spreadsheets to {Path.GetFileName(repointCalculatorPath)}...");
            int repointedCount = RepointDocumentOleReferences(asmDoc, repointCalculatorPath);
            DiagnosticsLogger.Instance.Info($"Repointed {repointedCount} OLE spreadsheet reference(s) to '{repointCalculatorPath}'.");
            if (repointedCount > 0)
            {
                progress?.Report("Updating assembly with repointed spreadsheet parameters...");
                try
                {
                    asmDoc.Update2(true);
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Instance.Warn($"Assembly Update2 failed after OLE repointing: {ex.Message}; trying Update()...");
                    try { asmDoc.Update(); } catch { }
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Wait for document readiness / view fit if window is visible
        if (isVisible)
        {
            progress?.Report("Waiting for document graphics and model readiness...");
            try
            {
                _inventorApp.SilentOperation = false;
            }
            catch { }

            try
            {
                dynamic views = asmDoc.Views;
                if (views != null && views.Count > 0)
                {
                    try { views[1].WindowState = WindowsSizeMaximize; } catch { }
                    try { views[1].Fit(); } catch { }
                }
            }
            catch { }

            try
            {
                // Activate view fit to verify viewport readiness
                dynamic activeView = _sessionScope.Track<object>(_inventorApp.ActiveView);
                if (activeView != null)
                {
                    activeView.Fit();
                }
            }
            catch
            {
                // Best effort view fit
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Perform deep recursive inventory
        using var inventoryScope = new ComReleaseScope();
        var inventory = _inventoryService.InventoryAssembly(
            asmDoc,
            _processInfo!.VersionString,
            _processInfo!.ProcessId,
            inventoryScope,
            progress,
            cancellationToken);

        return inventory;
    }

    /// <summary>
    /// Updates the visibility of the dedicated Inventor window on demand.
    /// When making Inventor visible, ensures that the assembly document has an active, maximized view
    /// and that SilentOperation is disabled so the user can see and interact with the 3D model.
    /// </summary>
    public Task<bool> SetVisibleAsync(bool visible)
    {
        if (_inventorApp == null || _isDisposed)
            return Task.FromResult(false);

        return ExecuteOnStaAsync(() =>
        {
            try
            {
                dynamic? app = _inventorApp;
                if (app != null)
                {
                    if (visible)
                    {
                        // 1. Disable SilentOperation so user can interact with Inventor
                        try
                        {
                            app.SilentOperation = false;
                        }
                        catch { }

                        // 2. Make the main Inventor application window visible
                        app.Visible = true;

                        // 3. Ensure the active assembly document has an open, maximized view window
                        if (_activeAssemblyDoc != null)
                        {
                            try
                            {
                                dynamic views = _activeAssemblyDoc.Views;
                                dynamic? view = null;
                                if (views != null)
                                {
                                    if (views.Count == 0)
                                    {
                                        view = views.Add();
                                    }
                                    else
                                    {
                                        view = views[1];
                                    }
                                }

                                try
                                {
                                    _activeAssemblyDoc.Activate();
                                }
                                catch { }

                                if (view != null)
                                {
                                    try
                                    {
                                        view.WindowState = WindowsSizeMaximize;
                                    }
                                    catch { }

                                    try
                                    {
                                        view.Fit();
                                    }
                                    catch { }

                                    try
                                    {
                                        view.Update();
                                    }
                                    catch { }
                                }
                            }
                            catch (Exception ex)
                            {
                                DiagnosticsLogger.Instance.Warn($"Could not create or activate document view in Inventor: {ex.Message}");
                            }
                        }

                        // 4. Best-effort active view fit & update
                        try
                        {
                            dynamic activeView = app.ActiveView;
                            if (activeView != null)
                            {
                                activeView.Fit();
                                activeView.Update();
                            }
                        }
                        catch { }

                        // 5. Bring Inventor window to foreground and restore if minimized
                        try
                        {
                            long mainHwnd = Convert.ToInt64(app.MainFrameHWND);
                            if (mainHwnd != 0)
                            {
                                IntPtr hWnd = new IntPtr(mainHwnd);
                                ShowWindow(hWnd, SW_RESTORE);
                                SetForegroundWindow(hWnd);
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        app.Visible = false;
                        try
                        {
                            app.SilentOperation = true;
                        }
                        catch { }
                    }

                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not set Inventor Visible = {visible}: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Repoints all linked OLE Excel spreadsheet references in the active assembly and all referenced child documents
    /// to the specified calculator workbook, and triggers an assembly update.
    /// </summary>
    public Task<int> RepointOleSpreadsheetReferencesAsync(string newCalculatorPath)
    {
        if (string.IsNullOrWhiteSpace(newCalculatorPath))
            throw new ArgumentException("New calculator path cannot be empty.", nameof(newCalculatorPath));

        return ExecuteOnStaAsync(() =>
        {
            if (_activeAssemblyDoc == null)
                throw new InvalidOperationException("No active assembly document to repoint.");

            int count = RepointDocumentOleReferences(_activeAssemblyDoc, newCalculatorPath);
            if (count > 0)
            {
                try { _activeAssemblyDoc.Update2(true); } catch { _activeAssemblyDoc.Update(); }
            }
            return count;
        });
    }

    private int RepointDocumentOleReferences(dynamic rootDoc, string newCalculatorPath)
    {
        int repointedCount = 0;
        string fullCalculatorPath = Path.GetFullPath(newCalculatorPath);
        var visitedDocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void RepointSingleDoc(dynamic doc)
        {
            try
            {
                string docPath = (string)doc.FullFileName;
                if (!string.IsNullOrEmpty(docPath) && !visitedDocs.Add(docPath))
                    return;

                dynamic oleDescriptors = doc.ReferencedOLEFileDescriptors;
                if (oleDescriptors == null) return;
                int count = oleDescriptors.Count;

                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        dynamic oleDesc = oleDescriptors.Item[i];
                        string currentRef = (string)oleDesc.FullFileName;

                        string ext = Path.GetExtension(currentRef).ToLowerInvariant();
                        if (ext is ".xls" or ".xlsx" or ".xlsm")
                        {
                            if (!string.Equals(currentRef, fullCalculatorPath, StringComparison.OrdinalIgnoreCase))
                            {
                                DiagnosticsLogger.Instance.Info(
                                    $"Replacing OLE reference in '{Path.GetFileName(docPath)}': '{currentRef}' -> '{fullCalculatorPath}'");
                                oleDesc.ReplaceReference(fullCalculatorPath);
                                repointedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DiagnosticsLogger.Instance.Warn($"Could not repoint OLE descriptor #{i} in doc: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not query OLE references for doc: {ex.Message}");
            }
        }

        // 1. Repoint root assembly document
        RepointSingleDoc(rootDoc);

        // 2. Repoint all referenced documents (subassemblies and parts)
        try
        {
            dynamic allRefDocs = rootDoc.AllReferencedDocuments;
            if (allRefDocs != null)
            {
                int count = allRefDocs.Count;
                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        dynamic childDoc = allRefDocs.Item[i];
                        RepointSingleDoc(childDoc);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not iterate AllReferencedDocuments for OLE repointing: {ex.Message}");
        }

        return repointedCount;
    }

    /// <summary>
    /// Retrieves the names of all internal iLogic rules present in the active assembly document.
    /// </summary>
    public Task<IReadOnlyList<string>> GetAvailableRulesAsync()
    {
        return ExecuteOnStaAsync<IReadOnlyList<string>>(() =>
        {
            if (_activeAssemblyDoc == null || _isDisposed)
                return Array.Empty<string>();

            var ruleService = new ILogicRuleService();
            return ruleService.GetAssemblyRules(_activeAssemblyDoc);
        });
    }

    /// <summary>
    /// Refreshes linked spreadsheet parameters, executes the chosen iLogic rule (if requested),
    /// rebuilds the assembly, and computes before/after deltas for dimensions and suppressions.
    /// </summary>
    public Task<ApplyModeDeltaResult> ApplyAndRebuildAsync(
        string? selectedRuleName,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ExecuteOnStaAsync(() =>
        {
            return ApplyAndRebuildInternal(selectedRuleName, progress, cancellationToken);
        });
    }

    private ApplyModeDeltaResult ApplyAndRebuildInternal(
        string? selectedRuleName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (_activeAssemblyDoc == null || _isDisposed)
            throw new InvalidOperationException("No active assembly document available in this Inventor session.");

        var sw = Stopwatch.StartNew();
        var result = new ApplyModeDeltaResult
        {
            ExecutedRuleName = selectedRuleName
        };

        cancellationToken.ThrowIfCancellationRequested();

        // 1. Capture BEFORE snapshot of parameters and suppressions
        progress?.Report("Capturing pre-apply state snapshot...");
        var (beforeParams, beforeOccs) = CaptureAssemblySnapshot((object)_activeAssemblyDoc!);

        // 2. Trigger Inventor to refresh linked spreadsheet parameters
        progress?.Report("Updating assembly from linked spreadsheet...");
        try
        {
            _activeAssemblyDoc.Update2(true);
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Assembly Update2 before rule execution: {ex.Message}; trying Update()...");
            try { _activeAssemblyDoc.Update(); } catch { }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 3. Execute selected iLogic rule if specified and not skipped
        if (!string.IsNullOrWhiteSpace(selectedRuleName) &&
            !selectedRuleName.StartsWith("None", StringComparison.OrdinalIgnoreCase))
        {
            var ruleService = new ILogicRuleService();
            bool isAllRules = selectedRuleName.StartsWith("All Rules", StringComparison.OrdinalIgnoreCase);

            progress?.Report(isAllRules ? "Executing all iLogic rules in sequence..." : $"Executing iLogic rule '{selectedRuleName}'...");

            string? ruleError = null;
            bool ruleSuccess = isAllRules
                ? ruleService.RunAllRules(_activeAssemblyDoc, out ruleError)
                : ruleService.RunRule(_activeAssemblyDoc, selectedRuleName, out ruleError);

            result.RuleExecutedSuccessfully = ruleSuccess;
            result.RuleMessage = ruleError ?? (isAllRules ? "All rules executed successfully." : $"Rule '{selectedRuleName}' executed successfully.");

            if (!ruleSuccess)
            {
                DiagnosticsLogger.Instance.Warn($"iLogic rule execution encountered an issue: {ruleError}");
            }
        }
        else
        {
            result.RuleExecutedSuccessfully = true;
            result.RuleMessage = "No iLogic rule specified (skipped).";
            DiagnosticsLogger.Instance.Info("Skipping iLogic rule execution as requested.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 4. Rebuild assembly
        progress?.Report("Rebuilding assembly after updates...");
        try
        {
            _activeAssemblyDoc.Update2(true);
            result.Success = true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Assembly Update2 after rule execution: {ex.Message}; trying Rebuild2...");
            try
            {
                _activeAssemblyDoc.Rebuild2(true);
                result.Success = true;
            }
            catch (Exception rebuildEx)
            {
                result.Success = false;
                result.ErrorMessage = $"Rebuild error: {rebuildEx.Message}";
                DiagnosticsLogger.Instance.Error($"Rebuild failed: {rebuildEx.Message}", rebuildEx);
            }
        }

        // 5. Capture AFTER snapshot of parameters and suppressions
        progress?.Report("Computing before vs. after deltas...");
        var (afterParams, afterOccs) = CaptureAssemblySnapshot((object)_activeAssemblyDoc!);

        // 6. Compute deltas
        ComputeDeltas(beforeParams, afterParams, beforeOccs, afterOccs, result);

        result.Duration = sw.Elapsed;
        DiagnosticsLogger.Instance.Success(
            $"Apply Mode complete in {sw.Elapsed.TotalSeconds:F2}s: {result.ChangedParametersCount} parameter(s) changed, " +
            $"{result.ChangedSuppressionsCount} occurrence suppression(s) changed ({result.UnsuppressedCount} turned ON, {result.SuppressedCount} turned OFF).");

        return result;
    }

    private (Dictionary<string, (double Value, string Expression, string Units, string DocName)> Params,
            Dictionary<string, (string PartNumber, bool IsSuppressed)> Occs) CaptureAssemblySnapshot(object asmDocObj)
    {
        dynamic asmDoc = asmDocObj;
        var paramMap = new Dictionary<string, (double Value, string Expression, string Units, string DocName)>(StringComparer.OrdinalIgnoreCase);
        var occMap = new Dictionary<string, (string PartNumber, bool IsSuppressed)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            dynamic compDef = asmDoc.ComponentDefinition;
            dynamic parameters = compDef.Parameters;
            int paramCount = parameters.Count;
            string docName = string.Empty;
            try { docName = (string)asmDoc.DisplayName; } catch { }

            for (int i = 1; i <= paramCount; i++)
            {
                try
                {
                    dynamic p = parameters.Item[i];
                    string name = (string)p.Name;
                    double val = 0;
                    try { val = (double)p.Value; } catch { }
                    string expr = string.Empty;
                    try { expr = (string)p.Expression; } catch { }
                    string units = string.Empty;
                    try { units = (string)p.Units; } catch { }

                    paramMap[name] = (val, expr, units, docName);
                }
                catch { }
            }

            dynamic occurrences = compDef.Occurrences;
            int occCount = occurrences.Count;
            for (int i = 1; i <= occCount; i++)
            {
                try
                {
                    dynamic occ = occurrences.Item[i];
                    CaptureOccurrencesRecursive(occ, occMap);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Error capturing assembly snapshot: {ex.Message}");
        }

        return (paramMap, occMap);
    }

    private void CaptureOccurrencesRecursive(dynamic occ, Dictionary<string, (string PartNumber, bool IsSuppressed)> occMap)
    {
        try
        {
            string name = (string)occ.Name;
            bool isSuppressed = false;
            try { isSuppressed = (bool)occ.Suppressed; } catch { }
            string partNum = string.Empty;

            try
            {
                if (!isSuppressed)
                {
                    dynamic def = occ.Definition;
                    dynamic doc = def.Document;
                    dynamic propSets = doc.PropertySets;
                    dynamic dtProps = propSets["Design Tracking Properties"];
                    partNum = (string)dtProps["Part Number"].Value;
                }
            }
            catch { }

            occMap[name] = (partNum, isSuppressed);

            if (!isSuppressed)
            {
                try
                {
                    dynamic subOccs = occ.SubOccurrences;
                    if (subOccs != null)
                    {
                        int count = subOccs.Count;
                        for (int i = 1; i <= count; i++)
                        {
                            try
                            {
                                CaptureOccurrencesRecursive(subOccs.Item[i], occMap);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private void ComputeDeltas(
        Dictionary<string, (double Value, string Expression, string Units, string DocName)> beforeParams,
        Dictionary<string, (double Value, string Expression, string Units, string DocName)> afterParams,
        Dictionary<string, (string PartNumber, bool IsSuppressed)> beforeOccs,
        Dictionary<string, (string PartNumber, bool IsSuppressed)> afterOccs,
        ApplyModeDeltaResult result)
    {
        foreach (var (name, afterData) in afterParams)
        {
            if (beforeParams.TryGetValue(name, out var beforeData))
            {
                var delta = new ParameterDelta
                {
                    ParameterName = name,
                    DocumentName = afterData.DocName,
                    BeforeValue = beforeData.Value,
                    AfterValue = afterData.Value,
                    BeforeExpression = beforeData.Expression,
                    AfterExpression = afterData.Expression,
                    Units = afterData.Units
                };
                result.ParameterDeltas.Add(delta);
            }
        }

        var allOccNames = beforeOccs.Keys.Union(afterOccs.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var name in allOccNames)
        {
            bool beforeSup = beforeOccs.TryGetValue(name, out var bData) ? bData.IsSuppressed : false;
            bool afterSup = afterOccs.TryGetValue(name, out var aData) ? aData.IsSuppressed : false;
            string partNum = aData.PartNumber ?? bData.PartNumber ?? string.Empty;

            var sDelta = new SuppressionDelta
            {
                OccurrenceName = name,
                PartNumber = partNum,
                BeforeSuppressed = beforeSup,
                AfterSuppressed = afterSup
            };
            result.SuppressionDeltas.Add(sDelta);
        }
    }

    /// <summary>
    /// Extracts physical holes and Segment Start reference planes from the active assembly document on the dedicated STA thread.
    /// </summary>
    public async Task<InventorHoleExtractionResult> ExtractHolesAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_activeAssemblyDoc == null)
        {
            throw new InvalidOperationException("No assembly document is currently open in the Inventor session.");
        }

        var extractor = new InventorHoleExtractionService();
        return await ExecuteOnStaAsync(() =>
        {
            using var opScope = new ComReleaseScope();
            return extractor.ExtractHoles(_activeAssemblyDoc, opScope, progress, cancellationToken);
        });
    }

    /// <summary>
    /// Runs full geometry extraction and compares against expected channel locations.
    /// </summary>
    public async Task<GeometryValidationResult> ValidateGeometryAsync(
        IEnumerable<ChannelLocationItem> channelLocations,
        double roofHeight = 0.0,
        double unitWidth = 0.0,
        GeometryComparisonOptions? options = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Extracting physical hole geometry from CAD assembly...");
        var extraction = await ExtractHolesAsync(progress, cancellationToken);

        progress?.Report("Generating expected hole coordinates from Channel Loc...");
        var expectedHoles = ExpectedHoleGenerator.GenerateExpectedHoles(
            channelLocations,
            extraction.SegmentStartZOffset,
            roofHeight,
            unitWidth);

        progress?.Report($"Matching {expectedHoles.Count} expected holes against {extraction.MergedHoleCount} physical holes...");
        var matchingEngine = new HoleMatchingEngine(options);
        var validation = matchingEngine.Validate(
            expectedHoles,
            extraction.Holes,
            extraction.SegmentStartReferenceName,
            extraction.SegmentStartZOffset);

        return validation;
    }

    /// <summary>
    /// Renders transient 3D ClientGraphics markers in Autodesk Inventor for the validated channel geometry.
    /// </summary>
    public Task<bool> RenderOverlaysAsync(
        GeometryValidationResult result,
        IEnumerable<HoleMatchResult>? holesToRender = null,
        bool includeExtraHoles = false)
    {
        if (_activeAssemblyDoc == null || _inventorApp == null || _isDisposed)
            return Task.FromResult(false);

        return ExecuteOnStaAsync<bool>(() =>
        {
            return _overlayService.RenderOverlays(_inventorApp, _activeAssemblyDoc, result, holesToRender, includeExtraHoles);
        });
    }

    /// <summary>
    /// Centers the Inventor 3D camera close-up on the selected hole and highlights its occurrence.
    /// </summary>
    public Task<bool> ZoomAndHighlightHoleAsync(HoleMatchResult hole)
    {
        if (_activeAssemblyDoc == null || _inventorApp == null || _isDisposed)
            return Task.FromResult(false);

        return ExecuteOnStaAsync<bool>(() =>
        {
            return _overlayService.ZoomAndHighlightHole(_inventorApp, _activeAssemblyDoc, hole);
        });
    }

    /// <summary>
    /// Clears all transient 3D ClientGraphics overlays and occurrence highlights in Inventor.
    /// </summary>
    public Task ClearOverlaysAsync()
    {
        if (_activeAssemblyDoc == null || _isDisposed)
            return Task.CompletedTask;

        return ExecuteOnStaAsync<bool>(() =>
        {
            _overlayService.ClearOverlays(_activeAssemblyDoc);
            return true;
        });
    }

    /// <summary>
    /// Closes the active assembly document without saving and cleanly terminates
    /// the application-owned Inventor process on its native STA thread.
    /// </summary>
    public void CloseSession()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        DiagnosticsLogger.Instance.Info("Closing Inventor automation session...");

        if (Thread.CurrentThread == _staThread)
        {
            DoCloseInternal();
        }
        else if (!_staWorkQueue.IsAddingCompleted)
        {
            try
            {
                var closeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _staWorkQueue.Add(() =>
                {
                    try
                    {
                        DoCloseInternal();
                    }
                    finally
                    {
                        closeTcs.SetResult(true);
                    }
                });

                closeTcs.Task.Wait(8000);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Error during STA CloseSession: {ex.Message}");
            }
            finally
            {
                _staWorkQueue.CompleteAdding();
            }
        }

        // Wait briefly for STA thread shutdown
        if (_staThread.IsAlive)
        {
            _staThread.Join(3000);
        }

        // Strict PID confirmation cleanup
        if (_processInfo != null && _processInfo.IsApplicationOwned)
        {
            EnsureProcessTerminated(_processInfo.ProcessId);
        }
    }

    private void CloseAllDocumentsInternal()
    {
        try
        {
            if (_activeAssemblyDoc != null)
            {
                try { _overlayService.ClearOverlays(_activeAssemblyDoc); } catch { }
                try
                {
                    _activeAssemblyDoc.Close(true);
                }
                catch { }
                _activeAssemblyDoc = null;
            }

            if (_inventorApp != null)
            {
                try
                {
                    dynamic docs = _inventorApp.Documents;
                    if (docs != null)
                    {
                        int count = docs.Count;
                        for (int i = count; i >= 1; i--)
                        {
                            try
                            {
                                dynamic doc = docs.Item[i];
                                doc.Close(true);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Error while closing open Inventor documents: {ex.Message}");
        }
        finally
        {
            _activeAssemblyDoc = null;
        }
    }

    private void DoCloseInternal()
    {
        try
        {
            DiagnosticsLogger.Instance.Info("Closing assembly and open documents without saving...");
            CloseAllDocumentsInternal();

            InventorProcessInfo? info = _processInfo;
            dynamic? app = _inventorApp;

            if (app is not null && info is not null && info.IsApplicationOwned)
            {
                try
                {
                    DiagnosticsLogger.Instance.Info($"Quitting dedicated Inventor instance (PID {info.ProcessId})...");
                    app.Quit();
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Instance.Warn($"Error calling app.Quit(): {ex.Message}");
                }
            }
        }
        finally
        {
            _sessionScope.Dispose();
            _inventorApp = null;
        }
    }

    private static void EnsureProcessTerminated(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                bool exited = proc.WaitForExit(5000);
                if (!exited)
                {
                    DiagnosticsLogger.Instance.Warn($"Inventor process PID {pid} did not exit cleanly within 5s. Terminating...");
                    proc.Kill();
                    proc.WaitForExit(3000);
                }
            }
        }
        catch
        {
            // Process already terminated
        }
    }

    public void Dispose()
    {
        CloseSession();
    }
}
