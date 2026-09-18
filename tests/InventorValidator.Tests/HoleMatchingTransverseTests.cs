using System.Collections.Generic;
using InventorValidator.Excel;
using InventorValidator.Geometry;
using InventorValidator.Geometry.Models;
using Xunit;

namespace InventorValidator.Tests;

public class HoleMatchingTransverseTests
{
    [Fact]
    public void CalculatorSessionResult_PrioritizesDataTabInputsForDimensions()
    {
        var result = new CalculatorSessionResult
        {
            // Sheet1 has the legacy formula typo where IW = =IH = 118
            Sheet1Parameters = new List<Sheet1ParameterItem>
            {
                new() { ParameterName = "IH", DisplayedValue = "118.000" },
                new() { ParameterName = "IW", DisplayedValue = "118.000" }
            },
            // Data tab has the genuine engineering inputs
            DataInputs = new List<DataInputItem>
            {
                new() { ParameterName = "IH", DisplayedValue = "118" },
                new() { ParameterName = "IW", DisplayedValue = "179" }
            }
        };

        // Act & Assert
        Assert.Equal(179.0, result.GetUnitWidth());
        Assert.Equal(118.0, result.GetRoofHeight());
    }

    [Fact]
    public void CalculatorSessionResult_FallsBackToSheet1WhenDataInputsMissing()
    {
        var result = new CalculatorSessionResult
        {
            Sheet1Parameters = new List<Sheet1ParameterItem>
            {
                new() { ParameterName = "IH", DisplayedValue = "96.000" },
                new() { ParameterName = "IW", DisplayedValue = "144.000" }
            },
            DataInputs = new List<DataInputItem>()
        };

        Assert.Equal(144.0, result.GetUnitWidth());
        Assert.Equal(96.0, result.GetRoofHeight());
    }

    [Fact]
    public void HoleMatchingEngine_DisqualifiesTransverselyDisplacedHoles()
    {
        // Expected South Wall channel hole at X = 118 (or 179), Y = 2.0, Z = 0.5
        var expected = new List<ExpectedHole>
        {
            new()
            {
                ChannelGroup = "South Wall Channels",
                ChannelName = "LEFT_HAND_CHAN_1",
                HoleIndex = 0,
                ReferencedPart = "LH BULKHEAD - 091-30101-058",
                ExpectedPosition = new Point3D(118.000, 2.000, 0.500),
                AuthoritativeAxes = AuthoritativeAxisPair.YZ,
                ArrayAxis = "Y",
                ArrayOffset = 2.0,
                ArraySpacing = 5.429,
                ArrayQuantity = 1
            }
        };

        // Physical hole on interior bottom brace (091-30101-083)
        // Displaced by 10.472" transversely in X, even though Y and Z are within 1.77"
        var actualHoles = new List<ActualHole>
        {
            new()
            {
                Id = 1,
                Position = new Point3D(107.528, 0.500, 1.444),
                Diameter = 0.218,
                ParentPartNumber = "091-30101-083",
                ParentOccurrenceName = "091-30101-083:1"
            }
        };

        var engine = new HoleMatchingEngine(new GeometryComparisonOptions
        {
            SearchRadius = 2.0,
            TransverseTolerance = 1.0
        });

        // Act
        var validation = engine.Validate(expected, actualHoles);

        // Assert: The wall channel hole should NOT match the interior hole
        Assert.Equal(0, validation.MatchedCount);
        Assert.Equal(1, validation.MissingCount);
        Assert.Equal(HoleMatchStatus.MissingExpected, validation.Results[0].Status);
    }

    [Fact]
    public void HoleMatchingEngine_MatchesPhysicalHoleWithinTransverseTolerance()
    {
        // Expected South Wall channel hole at X = 179.0, Y = 2.0, Z = 0.5
        var expected = new List<ExpectedHole>
        {
            new()
            {
                ChannelGroup = "South Wall Channels",
                ChannelName = "LEFT_HAND_CHAN_1",
                HoleIndex = 0,
                ReferencedPart = "LH BULKHEAD - 091-30101-058",
                ExpectedPosition = new Point3D(179.000, 2.000, 0.500),
                AuthoritativeAxes = AuthoritativeAxisPair.YZ,
                ArrayAxis = "Y",
                ArrayOffset = 2.0,
                ArraySpacing = 5.429,
                ArrayQuantity = 1
            }
        };

        // Real hole on LH Bulkhead (091-30101-078) at X = 178.662, Y = 2.000, Z = 0.500
        // Transverse delta in X is |178.662 - 179.000| = 0.338" <= 1.0"
        var actualHoles = new List<ActualHole>
        {
            new()
            {
                Id = 1,
                Position = new Point3D(178.662, 2.000, 0.500),
                Diameter = 0.218,
                ParentPartNumber = "091-30101-078",
                ParentOccurrenceName = "091-30101-078:1"
            }
        };

        var engine = new HoleMatchingEngine(new GeometryComparisonOptions
        {
            SearchRadius = 2.0,
            TransverseTolerance = 1.0
        });

        // Act
        var validation = engine.Validate(expected, actualHoles);

        // Assert
        Assert.Equal(1, validation.MatchedCount);
        Assert.Equal(0, validation.FailureCount);
        Assert.Equal(0, validation.MissingCount);
        Assert.Equal(HoleMatchStatus.Match, validation.Results[0].Status);
    }

    [Fact]
    public void RealWorkbook_Calc10005_004_ResolvesWidth179DespiteSheet1FormulaDefect()
    {
        const string samplePath = @"C:\Users\jbrow263\ISG\20183\Shell\Skid 02\02 (RF1\SQ FLTR TYPE8 118 X 179\Calc_10005_004.xls";
        if (!System.IO.File.Exists(samplePath)) return;

        Type? excelType = Type.GetTypeFromProgID("Excel.Application");
        if (excelType == null) return;

        dynamic? app = Activator.CreateInstance(excelType);
        if (app == null) return;

        try
        {
            app.Visible = false;
            app.DisplayAlerts = false;
            dynamic wb = app.Workbooks.Open(samplePath, UpdateLinks: 0, ReadOnly: true);
            try
            {
                dynamic wsData = wb.Sheets["Data"];
                object[,] dataArr = (object[,])wsData.UsedRange.Value2;
                var (dataInputs, _, _, _) = InventorValidator.Excel.Parsers.DataTabParser.Parse(dataArr);

                dynamic wsSheet1 = wb.Sheets["Sheet1"];
                object[,] sheet1Arr = (object[,])wsSheet1.UsedRange.Value2;
                var (sheet1Params, _, _) = InventorValidator.Excel.Parsers.Sheet1TabParser.ParseDetailed(sheet1Arr);

                var session = new CalculatorSessionResult
                {
                    DataInputs = dataInputs,
                    Sheet1Parameters = sheet1Params
                };

                // Confirm the legacy bug is indeed present on Sheet1 (IW evaluates to 118 instead of 179)
                var sheet1Iw = sheet1Params.Find(p => p.ParameterName == "IW");
                Assert.NotNull(sheet1Iw);
                Assert.True(double.TryParse(sheet1Iw.DisplayedValue, out double s1Val) && s1Val == 118.0);

                // Confirm our resolution properly gets 179.0 from Data
                Assert.Equal(179.0, session.GetUnitWidth());
                Assert.Equal(118.0, session.GetRoofHeight());
            }
            finally
            {
                wb.Close(false);
            }
        }
        finally
        {
            app.Quit();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(app);
        }
    }
}
