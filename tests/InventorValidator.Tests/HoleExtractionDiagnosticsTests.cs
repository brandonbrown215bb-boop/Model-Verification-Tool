using System;
using System.IO;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace InventorValidator.Tests;

public class HoleExtractionDiagnosticsTests
{
    private readonly ITestOutputHelper _output;

    public HoleExtractionDiagnosticsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CheckSurfaceTypeEnumValues()
    {
        string interopPath = @"C:\Program Files\Autodesk\Inventor 2020\Bin\Public Assemblies\Autodesk.Inventor.Interop.dll";
        if (!File.Exists(interopPath))
        {
            _output.WriteLine("Interop DLL not found at default path.");
            return;
        }

        var asm = Assembly.LoadFrom(interopPath);
        var enumType = asm.GetType("Inventor.SurfaceTypeEnum");
        Assert.NotNull(enumType);

        foreach (var name in Enum.GetNames(enumType))
        {
            var val = Convert.ToInt32(Enum.Parse(enumType, name));
            _output.WriteLine($"{name} = {val}");
        }

        var cylVal = Convert.ToInt32(Enum.Parse(enumType, "kCylinderSurface"));
        Assert.Equal(5891, cylVal);
        Assert.Equal(5891, InventorValidator.Inventor.InventorHoleExtractionService.CylinderSurfaceType);
    }

    [Fact]
    public async Task ExtractHolesFromRealSample_Skid02()
    {
        string iamPath = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 02\02 (RF1\SQ FLTR TYPE8 118 X 179\391-10005-004.iam";
        string calcPath = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 02\02 (RF1\SQ FLTR TYPE8 118 X 179\Calc_10005_004.xls";

        if (!File.Exists(iamPath) || !File.Exists(calcPath))
        {
            _output.WriteLine("Sample files not found, skipping.");
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "HoleDiagTest_" + Guid.NewGuid().ToString("N")[..8]);
        var workspaceManager = new InventorValidator.Session.WorkspaceManager(tempRoot);
        var session = new InventorValidator.Inventor.InventorAutomationSession();

        try
        {
            _output.WriteLine($"[1] Creating workspace at: {tempRoot}");
            var manifest = await workspaceManager.CreateWorkspaceAsync(iamPath, calcPath);

            _output.WriteLine($"[2] Parsing Channel Loc from Excel: {Path.GetFileName(calcPath)}");
            var excelSession = new InventorValidator.Excel.ExcelAutomationSession();
            var calcResult = await excelSession.RecalculateAndParseAsync(manifest.CopiedCalculatorPath);
            _output.WriteLine($"Found {calcResult.ChannelLocations.Count} Channel Loc items.");

            _output.WriteLine($"[3] Launching Inventor & extracting holes from: {Path.GetFileName(iamPath)}");
            var progress = new Progress<string>(msg => _output.WriteLine($"  [{DateTime.Now:HH:mm:ss}] {msg}"));
            
            // Start session and inventory assembly
            await session.StartAndInventoryAsync(manifest.CopiedIamPath, requestedVersion: "2020", isVisible: false, timeout: TimeSpan.FromSeconds(180), progress: progress);

            // Extract holes
            var extraction = await session.ExtractHolesAsync(progress);
            _output.WriteLine($"Extracted {extraction.RawFaceCount} raw faces -> {extraction.MergedHoleCount} deduplicated holes.");
            _output.WriteLine($"Datum: '{extraction.SegmentStartReferenceName}' @ Z={extraction.SegmentStartZOffset:F4}\"");

            // Print first 20 holes
            _output.WriteLine("Sample Extracted Holes:");
            foreach (var h in extraction.Holes.Take(20))
            {
                _output.WriteLine($"  Hole #{h.Id}: Pos=({h.Position.X:F4}, {h.Position.Y:F4}, {h.Position.Z:F4}) Dia={h.Diameter:F3}\" Axis=({h.Axis.X:F2},{h.Axis.Y:F2},{h.Axis.Z:F2}) Part='{h.ParentPartNumber}'");
            }

            // Generate expected holes and validate
            double roofHeight = 0.0;
            double unitWidth = 0.0;
            var roofParam = calcResult.Sheet1Parameters.Find(p => p.ParameterName.Equals("RoofHeight", StringComparison.OrdinalIgnoreCase) || p.ParameterName.Equals("Roof_Height", StringComparison.OrdinalIgnoreCase));
            if (roofParam != null && double.TryParse(roofParam.DisplayedValue, out double rh)) roofHeight = rh;

            var expectedHoles = InventorValidator.Geometry.ExpectedHoleGenerator.GenerateExpectedHoles(
                calcResult.ChannelLocations,
                extraction.SegmentStartZOffset,
                roofHeight,
                unitWidth);

            _output.WriteLine($"Generated {expectedHoles.Count} expected holes.");

            var matchingEngine = new InventorValidator.Geometry.HoleMatchingEngine();
            var validation = matchingEngine.Validate(
                expectedHoles,
                extraction.Holes,
                extraction.SegmentStartReferenceName,
                extraction.SegmentStartZOffset);

            _output.WriteLine($"Validation Results: {validation.MatchedCount} matched, {validation.WarningCount} warning, {validation.FailureCount} mislocated, {validation.MissingCount} missing, {validation.ExtraCount} extra.");

            Assert.True(extraction.RawFaceCount > 0, "Raw face count should be > 0!");
            Assert.True(extraction.MergedHoleCount > 0, "Merged hole count should be > 0!");
            Assert.Equal(122, validation.MatchedCount);
            Assert.Equal(0, validation.MissingCount);
            Assert.Equal(0, validation.FailureCount);
        }
        finally
        {
            session.CloseSession();
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }
}
