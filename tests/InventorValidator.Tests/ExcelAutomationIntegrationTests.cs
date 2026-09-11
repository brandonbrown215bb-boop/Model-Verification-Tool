using System.Diagnostics;
using System.IO;
using InventorValidator.Excel;
using Xunit;

namespace InventorValidator.Tests;

public class ExcelAutomationIntegrationTests
{
    private const string SampleCalc = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 03\02 (CC\SQ COIL BHD FW 118X179\Calc_10006_023.xls";

    [Fact]
    public async Task RecalculateAndParseAsync_WithRealSample_ShouldSucceedAndCleanUpProcess()
    {
        if (!File.Exists(SampleCalc))
        {
            // Skip test if sample is not present in the current test environment
            return;
        }

        // Measure starting excel processes
        int excelCountBefore = Process.GetProcessesByName("EXCEL").Length;

        // Create temporary copy to guarantee never touching the original
        string tempDir = Path.Combine(Path.GetTempPath(), $"iv_excel_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempCalc = Path.Combine(tempDir, Path.GetFileName(SampleCalc));
        File.Copy(SampleCalc, tempCalc, overwrite: true);

        try
        {
            var session = new ExcelAutomationSession();
            var progress = new Progress<string>();

            var result = await session.RecalculateAndParseAsync(
                tempCalc,
                timeout: TimeSpan.FromSeconds(60),
                progress: progress
            );

            Assert.NotNull(result);
            Assert.Equal("xlDone", result.CalculationState);
            Assert.True(result.IsErrorCheckPassed, $"Expected Error_Check == 0.0, but got: {result.WorkbookErrorCheckRaw}");

            // Verify Data Inputs
            Assert.True(result.TotalDataInputs >= 25, $"Expected >= 25 Data inputs, got {result.TotalDataInputs}");
            Assert.Contains(result.DataInputs, d => d.ParameterName.Equals("IH", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.DataInputs, d => d.ParameterName.Equals("IW", StringComparison.OrdinalIgnoreCase));

            // Verify Sheet1 Parameters
            Assert.True(result.TotalSheet1Parameters >= 500, $"Expected >= 500 Sheet1 parameters, got {result.TotalSheet1Parameters}");
            Assert.True(result.DimensionCount > 50, $"Expected > 50 Dimensions, got {result.DimensionCount}");
            Assert.True(result.SuppressionCount >= 95, $"Expected >= 95 Suppression parameters, got {result.SuppressionCount}");

            // Verify Channel Locations and Unification across unit sides
            Assert.True(result.ChannelLocations.Count >= 20, $"Expected >= 20 raw channel rows, got {result.ChannelLocations.Count}");
            Assert.Single(result.UnifiedChannels);
            Assert.Equal(1, result.ValidChannelsCount);
            var uChan1 = result.UnifiedChannels[0];
            Assert.Equal(0.750, uChan1.ZLocation, precision: 3);
            Assert.Equal(4, uChan1.SideCount);
            Assert.Contains("Floor", uChan1.SidesSummary);
            Assert.Contains("Roof", uChan1.SidesSummary);
            Assert.Contains("South Wall (Left)", uChan1.SidesSummary);
            Assert.Contains("North Wall (Right)", uChan1.SidesSummary);

            // Check known sample fixtures from PHASED_PLAN.md
            var fc1 = result.ChannelLocations.FirstOrDefault(c => c.ChannelName == "FLOOR_CHAN_1");
            Assert.NotNull(fc1);
            Assert.Equal(ChannelStatus.Valid, fc1.Status);
            Assert.Equal("091-30102-458", fc1.ReferencedPart);
            Assert.Equal(16, fc1.Quantity);

            var fc4 = result.ChannelLocations.FirstOrDefault(c => c.ChannelName == "FLOOR_CHAN_4");
            Assert.NotNull(fc4);
            Assert.Equal(ChannelStatus.Valid, fc4.Status);
            Assert.Equal(2, fc4.Quantity);

            var fc5 = result.ChannelLocations.FirstOrDefault(c => c.ChannelName == "FLOOR_CHAN_5");
            Assert.NotNull(fc5);
            Assert.Equal(ChannelStatus.Valid, fc5.Status);
            Assert.Equal(2, fc5.Quantity);

            // Verify known orphaned #REF! rows are flagged and skipped without failing the session
            var rc3 = result.ChannelLocations.FirstOrDefault(c => c.ChannelName == "ROOF_CHAN_3");
            Assert.NotNull(rc3);
            Assert.Equal(ChannelStatus.SkippedInvalidRow, rc3.Status);

            var rc8 = result.ChannelLocations.FirstOrDefault(c => c.ChannelName == "ROOF_CHAN_8");
            Assert.NotNull(rc8);
            Assert.Equal(ChannelStatus.SkippedInvalidRow, rc8.Status);

            var lhc1 = result.ChannelLocations.FirstOrDefault(c => c.ChannelName == "LEFT_HAND_CHAN_1");
            Assert.NotNull(lhc1);
            Assert.Equal(ChannelStatus.Valid, lhc1.Status);
            Assert.Equal("South Wall Channels", lhc1.ChannelGroup);
            Assert.Equal("Y", lhc1.Axis);
            Assert.Equal(22, lhc1.Quantity);

            var rhc1 = result.ChannelLocations.FirstOrDefault(c => c.ChannelName == "RIGHT_HAND_CHAN_1");
            Assert.NotNull(rhc1);
            Assert.Equal(ChannelStatus.Valid, rhc1.Status);
            Assert.Equal("North Wall Channels", rhc1.ChannelGroup);
            Assert.Equal("Y", rhc1.Axis);
            Assert.Equal(22, rhc1.Quantity);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }

            // Verify no leftover Excel process was orphaned
            // Allow up to 2 seconds for process termination to finalize
            Thread.Sleep(1000);
            int excelCountAfter = Process.GetProcessesByName("EXCEL").Length;
            Assert.True(excelCountAfter <= excelCountBefore,
                $"Excel process leak detected! Before: {excelCountBefore}, After: {excelCountAfter}");
        }
    }

    [Fact]
    public async Task RecalculateAndParseAsync_WhenMissingRequiredSheets_ShouldThrowDescriptiveException()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"iv_missing_sheets_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string dummyFile = Path.Combine(tempDir, "InvalidWorkbook.xlsx");

        Type? excelType = Type.GetTypeFromProgID("Excel.Application");
        if (excelType == null) return;

        dynamic? excel = Activator.CreateInstance(excelType);
        if (excel == null) return;
        try
        {
            excel.Visible = false;
            excel.DisplayAlerts = false;
            dynamic wb = excel.Workbooks.Add();
            wb.SaveAs(dummyFile);
            wb.Close(false);
        }
        finally
        {
            excel.Quit();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(excel);
        }

        try
        {
            var session = new ExcelAutomationSession();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                session.RecalculateAndParseAsync(dummyFile, timeout: TimeSpan.FromSeconds(15))
            );

            Assert.Contains("missing required worksheet", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Data", ex.Message);
            Assert.Contains("Channel Loc", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private const string Sample2Calc = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 02\01 AB)\SQ AIR BLENDER ASSEMBLY 118X179 9\Calc_10020_001.xls";

    [Fact]
    public async Task RecalculateAndParseAsync_WithAirBlenderSampleWithoutPartNumbers_ShouldSucceedWithValidChannels()
    {
        if (!File.Exists(Sample2Calc)) return;

        string tempDir = Path.Combine(Path.GetTempPath(), $"iv_excel_airblender_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string tempCalc = Path.Combine(tempDir, Path.GetFileName(Sample2Calc));
        File.Copy(Sample2Calc, tempCalc, overwrite: true);

        try
        {
            var session = new ExcelAutomationSession();
            var result = await session.RecalculateAndParseAsync(tempCalc, timeout: TimeSpan.FromSeconds(60));

            Assert.NotNull(result);
            Assert.Equal("xlDone", result.CalculationState);
            Assert.True(result.IsErrorCheckPassed);

            // Verify Data Inputs
            Assert.True(result.TotalDataInputs >= 10);
            Assert.Contains(result.DataInputs, d => d.ParameterName.Equals("IH", StringComparison.OrdinalIgnoreCase));

            // Verify Sheet1 has suppression controls
            Assert.True(result.SuppressionCount >= 3, $"Expected >= 3 Suppression controls, got {result.SuppressionCount}");
            Assert.Contains(result.Sheet1Parameters, p => p.ParameterName.Contains("WireWaySuppression", StringComparison.OrdinalIgnoreCase));

            // Verify Channels are Valid without part numbers and unified to 1 channel across all 4 sides
            Assert.Single(result.UnifiedChannels);
            Assert.Equal(1, result.ValidChannelsCount);
            Assert.Equal(16, result.TotalSegmentsCount);

            var uChan2 = result.UnifiedChannels[0];
            Assert.Equal(1.000, uChan2.ZLocation, precision: 3);
            Assert.Equal(4, uChan2.SideCount);
            Assert.Equal(16, uChan2.SegmentCount);
            Assert.Equal(114, uChan2.TotalHoles); // (6*3 + 17) Floor + (6*3 + 17) Roof + 22 LH + 22 RH = 114
            Assert.Contains("Floor", uChan2.SidesSummary);
            Assert.Contains("Roof", uChan2.SidesSummary);
            Assert.Contains("South Wall (Left)", uChan2.SidesSummary);
            Assert.Contains("North Wall (Right)", uChan2.SidesSummary);

            var fc1 = result.ChannelLocations.FirstOrDefault(c => c.ChannelName == "FLOOR_CHAN_1");
            Assert.NotNull(fc1);
            Assert.Equal(ChannelStatus.Valid, fc1.Status);
            Assert.Equal(3, fc1.Quantity);
            Assert.Equal(0.8125, fc1.Offset!.Value, precision: 4);
            Assert.Equal(5.365, fc1.Spacing!.Value, precision: 3);
            Assert.Equal(1.000, fc1.ZLocation!.Value, precision: 3);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
