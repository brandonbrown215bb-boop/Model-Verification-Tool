using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using InventorValidator.Excel.Parsers;
using InventorValidator.Infrastructure;

namespace InventorValidator.Excel;

/// <summary>
/// Manages a dedicated, invisible Microsoft Excel process running on an STA thread
/// to safely open, recalculate, and parse calculator workbooks (.xls and .xlsx).
/// </summary>
public sealed class ExcelAutomationSession
{
    private const int XlDone = 0; // xlDone
    private const int MsoAutomationSecurityForceDisable = 3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>
    /// Recalculates and parses the workbook on a dedicated STA thread.
    /// Optionally applies input overrides to the Data worksheet before recalculating and saving.
    /// </summary>
    public Task<CalculatorSessionResult> RecalculateAndParseAsync(
        string workbookPath,
        IDictionary<string, string>? inputOverrides = null,
        TimeSpan? timeout = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(120);
        var tcs = new TaskCompletionSource<CalculatorSessionResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var staThread = new Thread(() =>
        {
            try
            {
                var result = RunInternal(workbookPath, inputOverrides, effectiveTimeout, progress, cancellationToken);
                tcs.SetResult(result);
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();

        return tcs.Task;
    }

    private CalculatorSessionResult RunInternal(
        string workbookPath,
        IDictionary<string, string>? inputOverrides,
        TimeSpan timeout,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException($"Calculator file not found: {workbookPath}", workbookPath);
        }

        progress?.Report("Starting dedicated Excel process...");
        DiagnosticsLogger.Instance.Info($"Launching isolated Excel process for: {workbookPath}");

        Type excelType = Type.GetTypeFromProgID("Excel.Application")
            ?? throw new InvalidOperationException("Microsoft Excel is not installed or Excel.Application COM ProgID is not registered.");

        dynamic? excelApp = null;
        int? excelPid = null;
        var appScope = new ComReleaseScope();
        var workbookScope = new ComReleaseScope();

        try
        {
            excelApp = Activator.CreateInstance(excelType);
            if (excelApp == null)
            {
                throw new InvalidOperationException("Failed to instantiate Excel.Application COM object.");
            }
            appScope.Track<object>(excelApp);

            // Configure isolated Excel settings
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;
            excelApp.AskToUpdateLinks = false;
            excelApp.EnableEvents = false;
            try
            {
                excelApp.AutomationSecurity = MsoAutomationSecurityForceDisable;
            }
            catch
            {
                // AutomationSecurity may not be supported on all Office builds
            }

            // Retrieve PID to guarantee ownership
            try
            {
                IntPtr hwnd = (IntPtr)excelApp.Hwnd;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                {
                    excelPid = (int)pid;
                    DiagnosticsLogger.Instance.Info($"Dedicated Excel process started with PID {excelPid.Value}");
                }
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Instance.Warn($"Could not determine Excel process ID: {ex.Message}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report("Opening calculator workbook...");
            DiagnosticsLogger.Instance.Info($"Opening workbook: {workbookPath}");

            bool hasOverrides = inputOverrides != null && inputOverrides.Count > 0;
            dynamic workbooks = workbookScope.Track<object>(excelApp.Workbooks);
            dynamic wb = workbookScope.Track<object>(workbooks.Open(
                workbookPath,
                UpdateLinks: 0,
                ReadOnly: !hasOverrides
            ));

            cancellationToken.ThrowIfCancellationRequested();

            // Preflight worksheets
            dynamic sheets = workbookScope.Track<object>(wb.Worksheets);
            int sheetCount = sheets.Count;
            var sheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i <= sheetCount; i++)
            {
                dynamic ws = workbookScope.Track<object>(sheets.Item[i]);
                sheetNames.Add((string)ws.Name);
            }

            var missing = new List<string>();
            if (!sheetNames.Contains("Data")) missing.Add("Data");
            if (!sheetNames.Contains("Sheet1")) missing.Add("Sheet1");
            if (!sheetNames.Contains("Channel Loc")) missing.Add("Channel Loc");

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Workbook is missing required worksheet(s): {string.Join(", ", missing)}. Found worksheets: {string.Join(", ", sheetNames)}.");
            }

            if (hasOverrides)
            {
                progress?.Report("Applying parameter overrides to Data worksheet...");
                dynamic dataWs = workbookScope.Track<object>(sheets.Item["Data"]);
                dynamic usedRange = workbookScope.Track<object>(dataWs.UsedRange);
                object[,] dataValues = (object[,])usedRange.Value2;

                int rMin = dataValues.GetLowerBound(0);
                int rMax = dataValues.GetUpperBound(0);
                int cMin = dataValues.GetLowerBound(1);
                int cMax = dataValues.GetUpperBound(1);

                int colParam = -1;
                int colVal = -1;

                for (int r = rMin; r <= Math.Min(rMin + 15, rMax); r++)
                {
                    for (int c = cMin; c <= cMax; c++)
                    {
                        var text = dataValues[r, c]?.ToString()?.Trim() ?? string.Empty;
                        if (string.IsNullOrEmpty(text)) continue;

                        if ((text.Contains("Input", StringComparison.OrdinalIgnoreCase) && text.Contains("parameter", StringComparison.OrdinalIgnoreCase)) ||
                            text.Equals("Parameter", StringComparison.OrdinalIgnoreCase))
                        {
                            if (colParam == -1) colParam = c;
                        }
                        else if (text.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
                                 (text.StartsWith("Value", StringComparison.OrdinalIgnoreCase) && !text.Contains("MOM", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (colVal == -1) colVal = c;
                        }
                    }
                    if (colParam != -1 && colVal != -1) break;
                }

                if (colParam == -1) colParam = cMin;
                if (colVal == -1) colVal = cMin + 1;

                foreach (var (paramName, overrideVal) in inputOverrides!)
                {
                    for (int r = rMin + 1; r <= rMax; r++)
                    {
                        var cellParam = dataValues[r, colParam]?.ToString()?.Trim() ?? string.Empty;
                        if (string.Equals(cellParam, paramName, StringComparison.OrdinalIgnoreCase))
                        {
                            DiagnosticsLogger.Instance.Info($"Writing override for input '{paramName}' in Data row {r}, col {colVal}: '{overrideVal}'");
                            dynamic cell = usedRange.Cells[r, colVal];
                            if (double.TryParse(overrideVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double numVal))
                            {
                                cell.Value2 = numVal;
                            }
                            else
                            {
                                cell.Value2 = overrideVal;
                            }
                            try { Marshal.FinalReleaseComObject(cell); } catch { }
                            break;
                        }
                    }
                }
            }

            // Recalculate
            var sw = Stopwatch.StartNew();

            if (hasOverrides)
            {
                progress?.Report("Recalculating Excel formulas (Full Rebuild)...");
                try
                {
                    excelApp.CalculateFullRebuild();
                }
                catch
                {
                    try
                    {
                        excelApp.CalculateFull();
                    }
                    catch
                    {
                        excelApp.Calculate();
                    }
                }
            }
            else
            {
                progress?.Report("Validating Excel formulas...");
                try
                {
                    excelApp.Calculate();
                }
                catch { }
            }

            // Poll for calculation completion
            DateTime deadline = DateTime.UtcNow + timeout;
            bool calculationCompleted = false;
            string lastRecordedState = "Unknown";

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int calcState = -1;
                try
                {
                    calcState = (int)excelApp.CalculationState;
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Instance.Warn($"Could not query Excel CalculationState: {ex.Message}");
                    calcState = -1;
                }

                if (calcState == XlDone)
                {
                    calculationCompleted = true;
                    lastRecordedState = "xlDone";
                    break;
                }
                else if (calcState == 1)
                {
                    lastRecordedState = "xlCalculating";
                }
                else if (calcState == 2)
                {
                    lastRecordedState = "xlPending";
                }
                else
                {
                    lastRecordedState = "Unknown";
                }

                Thread.Sleep(100);
            }

            if (!calculationCompleted)
            {
                sw.Stop();
                throw new TimeoutException(
                    $"Excel recalculation timed out after {timeout.TotalSeconds:F0} seconds without reaching xlDone (last state: '{lastRecordedState}'). " +
                    "Aborting session to prevent false-positive validation against stale or uncalculated workbook formulas.");
            }

            sw.Stop();
            DiagnosticsLogger.Instance.Info($"Recalculation completed in {sw.ElapsedMilliseconds} ms.");

            if (hasOverrides)
            {
                progress?.Report("Saving recalculated disposable workbook...");
                wb.Save();
                DiagnosticsLogger.Instance.Info($"Saved disposable workbook with parameter overrides: {workbookPath}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Step 5: Parse Data tab
            progress?.Report("Parsing 'Data' worksheet...");
            dynamic wsData = workbookScope.Track<object>(sheets.Item["Data"]);
            dynamic rngData = workbookScope.Track<object>(wsData.UsedRange);
            object[,] dataArray = (object[,])rngData.Value2;
            var (dataInputs, errVal, errRaw, isPassed) = DataTabParser.Parse(dataArray);

            cancellationToken.ThrowIfCancellationRequested();

            // Step 5: Parse Sheet1 tab
            progress?.Report("Parsing 'Sheet1' worksheet...");
            dynamic wsSheet1 = workbookScope.Track<object>(sheets.Item["Sheet1"]);
            dynamic rngSheet1 = workbookScope.Track<object>(wsSheet1.UsedRange);
            object[,] sheet1Array = (object[,])rngSheet1.Value2;
            var (sheet1Params, holeSchedules, archetype) = Sheet1TabParser.ParseDetailed(sheet1Array);

            cancellationToken.ThrowIfCancellationRequested();

            // Step 5: Parse Channel Loc tab
            progress?.Report("Parsing 'Channel Loc' worksheet...");
            dynamic wsChan = workbookScope.Track<object>(sheets.Item["Channel Loc"]);
            dynamic rngChan = workbookScope.Track<object>(wsChan.UsedRange);
            object[,] chanArray = (object[,])rngChan.Value2;
            var channelLocs = ChannelLocTabParser.Parse(chanArray);
            var unifiedChannels = ChannelUnificationService.Unify(channelLocs);

            var sessionResult = new CalculatorSessionResult
            {
                WorkbookPath = workbookPath,
                RecalculationDuration = sw.Elapsed,
                CalculationState = lastRecordedState,
                WorkbookErrorCheckValue = errVal,
                WorkbookErrorCheckRaw = errRaw,
                IsErrorCheckPassed = isPassed,
                DataInputs = dataInputs,
                Sheet1Parameters = sheet1Params,
                DetectedArchetype = archetype,
                HoleSchedules = holeSchedules,
                ChannelLocations = channelLocs,
                UnifiedChannels = unifiedChannels
            };

            DiagnosticsLogger.Instance.Success(
                $"Workbook parsed successfully: {dataInputs.Count} inputs, {sheet1Params.Count} Sheet1 params, {unifiedChannels.Count} unified channel(s) ({channelLocs.Count} raw rows). Error_Check = {errRaw}");

            try
            {
                wb.Close(SaveChanges: false);
            }
            catch
            {
                // Best effort close
            }

            return sessionResult;
        }
        finally
        {
            // 1. Release all workbook, worksheet, and range COM objects
            try
            {
                workbookScope.Dispose();
            }
            catch { }

            // 2. Quit Excel application while no child COM references exist
            try
            {
                excelApp?.Quit();
            }
            catch
            {
                // Best-effort quit
            }

            // 3. Release Excel Application COM wrapper
            try
            {
                appScope.Dispose();
            }
            catch { }

            excelApp = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // 4. Ensure the specific Excel process is terminated
            if (excelPid.HasValue)
            {
                try
                {
                    var proc = Process.GetProcessById(excelPid.Value);
                    if (!proc.HasExited)
                    {
                        if (!proc.WaitForExit(5000))
                        {
                            proc.Kill();
                            DiagnosticsLogger.Instance.Warn($"Force-killed Excel PID {excelPid.Value} after timeout.");
                        }
                    }
                }
                catch
                {
                    // Process already exited
                }
            }

            DiagnosticsLogger.Instance.Info("Excel automation session cleaned up.");
        }
    }
}
