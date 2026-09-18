using System.Diagnostics;
using System.IO;
using InventorValidator.Excel;
using InventorValidator.Infrastructure;
using InventorValidator.Inventor;
using InventorValidator.Session;
using Xunit;
using Xunit.Abstractions;

namespace InventorValidator.Tests;

public class InventorAutomationIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private const string SampleIamPath = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 03\02 (CC\SQ COIL BHD FW 118X179\391-10006-023.iam";
    private const string SampleCalcPath = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 03\02 (CC\SQ COIL BHD FW 118X179\Calc_10006_023.xls";

    public InventorAutomationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task InventorAutomationSession_CanInventoryCopiedAssemblyAndShutDownCleanly()
    {
        var detector = new InventorVersionDetector();
        if (!detector.Info2020.IsInstalled && !detector.Info2024.IsInstalled)
        {
            // Skip if no Inventor installed on the machine
            _output.WriteLine("No Inventor installed, skipping test.");
            return;
        }

        if (!File.Exists(SampleIamPath))
        {
            // Skip if sample file path not accessible
            _output.WriteLine($"Sample file not found: {SampleIamPath}, skipping test.");
            return;
        }

        // 1. Create disposable workspace
        var tempRoot = Path.Combine(Path.GetTempPath(), "InventorValidatorTest_" + Guid.NewGuid().ToString("N")[..8]);
        var workspaceManager = new WorkspaceManager(tempRoot);
        SessionManifest? manifest = null;
        InventorAutomationSession? session = null;

        try
        {
            _output.WriteLine($"[1/5] Creating workspace at: {tempRoot}");
            manifest = await workspaceManager.CreateWorkspaceAsync(SampleIamPath, SampleCalcPath);
            Assert.True(File.Exists(manifest.CopiedIamPath));

            // 2. Parse Excel Sheet1 to enable parameter matching
            _output.WriteLine("[2/5] Recalculating and parsing Excel calculator...");
            var excelSession = new ExcelAutomationSession();
            var calcResult = await excelSession.RecalculateAndParseAsync(manifest.CopiedCalculatorPath);
            Assert.True(calcResult.Sheet1Parameters.Count > 0);
            _output.WriteLine($"Excel parsed {calcResult.Sheet1Parameters.Count} Sheet1 parameters.");

            // 3. Launch dedicated Inventor instance and inventory copied assembly
            _output.WriteLine("[3/5] Starting Inventor and inventorying assembly...");
            session = new InventorAutomationSession();
            var progress = new Progress<string>(msg =>
            {
                _output.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
                DiagnosticsLogger.Instance.Info(msg);
            });

            var inventoryResult = await session.StartAndInventoryAsync(
                manifest.CopiedIamPath,
                requestedVersion: "Automatic",
                timeout: TimeSpan.FromSeconds(300),
                progress: progress
            );

            // Assertions on CAD Model Inventory
            Assert.NotNull(inventoryResult);
            Assert.True(inventoryResult.ProcessId > 0);
            Assert.True(inventoryResult.TotalOccurrencesCount >= 100, $"Expected >= 100 occurrences, got {inventoryResult.TotalOccurrencesCount}");
            Assert.True(inventoryResult.Parameters.Count >= 300, $"Expected >= 300 parameters, got {inventoryResult.Parameters.Count}");
            Assert.True(inventoryResult.TotalDocumentsCount >= 40, $"Expected >= 40 unique documents, got {inventoryResult.TotalDocumentsCount}");

            // 4. Parameter Matching & Inspect mode comparison
            var matchingService = new ParameterMatchingService();
            var comparison = matchingService.CompareParameters(calcResult.Sheet1Parameters, inventoryResult);

            Assert.NotNull(comparison);
            Assert.True(comparison.TotalRows > 0);
            Assert.True(comparison.SafeToWriteCount >= 0);

            // 4b. Session Reuse & Warm Run Benchmark
            _output.WriteLine("[4b/5] Testing warm session reuse on active Inventor instance...");
            var warmSw = Stopwatch.StartNew();
            var warmResult = await session.StartAndInventoryAsync(
                manifest.CopiedIamPath,
                requestedVersion: "Automatic",
                timeout: TimeSpan.FromSeconds(30),
                progress: progress
            );
            warmSw.Stop();
            _output.WriteLine($"[WARM RUN] Warm inventory completed in {warmSw.Elapsed.TotalSeconds:F2} seconds (PID {warmResult.ProcessId})");
            Assert.True(warmSw.Elapsed.TotalSeconds < 30, $"Warm run expected < 30s, took {warmSw.Elapsed.TotalSeconds:F2}s");
            Assert.Equal(inventoryResult.ProcessId, warmResult.ProcessId);
            Assert.True(warmResult.TotalOccurrencesCount >= 100);

            // 5. Clean teardown verification
            int trackedPid = inventoryResult.ProcessId;
            session.CloseSession();
            session = null;

            // Confirm process is closed
            await Task.Delay(1000);
            bool processStillAlive = false;
            try
            {
                var proc = Process.GetProcessById(trackedPid);
                processStillAlive = !proc.HasExited;
            }
            catch
            {
                processStillAlive = false;
            }

            Assert.False(processStillAlive, $"Dedicated Inventor process PID {trackedPid} should be terminated after CloseSession.");
        }
        finally
        {
            session?.CloseSession();
            if (manifest != null)
            {
                workspaceManager.CleanupWorkspace(manifest);
            }

            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
            catch { }
        }
    }
}
