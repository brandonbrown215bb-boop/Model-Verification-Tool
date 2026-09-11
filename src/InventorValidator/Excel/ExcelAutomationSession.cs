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
    /// </summary>
    public Task<CalculatorSessionResult> RecalculateAndParseAsync(
        string workbookPath,
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
                var result = RunInternal(workbookPath, effectiveTimeout, progress, cancellationToken);
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
        var scope = new ComReleaseScope();

        try
        {
            excelApp = Activator.CreateInstance(excelType);
            if (excelApp == null)
            {
                throw new InvalidOperationException("Failed to instantiate Excel.Application COM object.");
            }
            scope.Track<object>(excelApp);

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

            dynamic workbooks = scope.Track<object>(excelApp.Workbooks);
            dynamic wb = scope.Track<object>(workbooks.Open(
                workbookPath,
                UpdateLinks: 0,
                ReadOnly: true
            ));

            cancellationToken.ThrowIfCancellationRequested();

            // Preflight worksheets
            dynamic sheets = scope.Track<object>(wb.Worksheets);
            int sheetCount = sheets.Count;
            var sheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i <= sheetCount; i++)
            {
                dynamic ws = scope.Track<object>(sheets.Item[i]);
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

            // Recalculate
            progress?.Report("Recalculating Excel formulas (Full Rebuild)...");
            var sw = Stopwatch.StartNew();

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

            // Poll for calculation completion
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int calcState;
                try
                {
                    calcState = (int)excelApp.CalculationState;
                }
                catch
                {
                    calcState = XlDone;
                }

                if (calcState == XlDone)
                {
                    break;
                }

                Thread.Sleep(100);
            }

            sw.Stop();
            DiagnosticsLogger.Instance.Info($"Recalculation completed in {sw.ElapsedMilliseconds} ms.");

            cancellationToken.ThrowIfCancellationRequested();

            // Step 5: Parse Data tab
            progress?.Report("Parsing 'Data' worksheet...");
            dynamic wsData = scope.Track<object>(wb.Worksheets["Data"]);
            dynamic rngData = scope.Track<object>(wsData.UsedRange);
            object[,] dataArray = (object[,])rngData.Value2;
            var (dataInputs, errVal, errRaw, isPassed) = DataTabParser.Parse(dataArray);

            cancellationToken.ThrowIfCancellationRequested();

            // Step 5: Parse Sheet1 tab
            progress?.Report("Parsing 'Sheet1' worksheet...");
            dynamic wsSheet1 = scope.Track<object>(wb.Worksheets["Sheet1"]);
            dynamic rngSheet1 = scope.Track<object>(wsSheet1.UsedRange);
            object[,] sheet1Array = (object[,])rngSheet1.Value2;
            var sheet1Params = Sheet1TabParser.Parse(sheet1Array);

            cancellationToken.ThrowIfCancellationRequested();

            // Step 5: Parse Channel Loc tab
            progress?.Report("Parsing 'Channel Loc' worksheet...");
            dynamic wsChan = scope.Track<object>(wb.Worksheets["Channel Loc"]);
            dynamic rngChan = scope.Track<object>(wsChan.UsedRange);
            object[,] chanArray = (object[,])rngChan.Value2;
            var channelLocs = ChannelLocTabParser.Parse(chanArray);
            var unifiedChannels = ChannelUnificationService.Unify(channelLocs);

            var sessionResult = new CalculatorSessionResult
            {
                WorkbookPath = workbookPath,
                RecalculationDuration = sw.Elapsed,
                CalculationState = "xlDone",
                WorkbookErrorCheckValue = errVal,
                WorkbookErrorCheckRaw = errRaw,
                IsErrorCheckPassed = isPassed,
                DataInputs = dataInputs,
                Sheet1Parameters = sheet1Params,
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
            // Clean up Excel COM and process
            try
            {
                excelApp?.Quit();
            }
            catch
            {
                // Best-effort quit
            }

            scope.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Ensure the specific Excel process is terminated
            if (excelPid.HasValue)
            {
                try
                {
                    var proc = Process.GetProcessById(excelPid.Value);
                    if (!proc.HasExited)
                    {
                        if (!proc.WaitForExit(3000))
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
