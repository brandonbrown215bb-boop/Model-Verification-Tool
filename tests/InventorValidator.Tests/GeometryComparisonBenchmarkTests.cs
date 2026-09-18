using System;
using System.Collections.Generic;
using System.Linq;
using InventorValidator.Excel;
using InventorValidator.Geometry;
using InventorValidator.Geometry.Models;
using Xunit;

namespace InventorValidator.Tests;

public class GeometryComparisonBenchmarkTests
{
    [Fact]
    public void Validate_Sample10006Benchmark_MatchesObservedPhysicalBehaviors()
    {
        // 1. Setup Excel expected items for sample 391-10006-023
        var items = new List<ChannelLocationItem>
        {
            // Floor Chan 4 (2 holes: 2.31, 5.00)
            new()
            {
                RowIndex = 9,
                ChannelGroup = "Floor Channels",
                ChannelName = "FLOOR_CHAN_4",
                Axis = "X",
                Offset = 2.310,
                Spacing = 2.690,
                Quantity = 2,
                ZLocation = 0.750,
                ReferencedPart = "091-30102-461",
                Status = ChannelStatus.Valid
            },
            // Floor Chan 5 (2 holes: 175.00, 176.69)
            new()
            {
                RowIndex = 10,
                ChannelGroup = "Floor Channels",
                ChannelName = "FLOOR_CHAN_5",
                Axis = "X",
                Offset = 175.000,
                Spacing = 1.690,
                Quantity = 2,
                ZLocation = 0.750,
                ReferencedPart = "091-30102-462",
                Status = ChannelStatus.Valid
            },
            // South Wall (22 holes: Y=1.895 + i*5.429, Z=0.75)
            new()
            {
                RowIndex = 45,
                ChannelGroup = "South Wall Channels",
                ChannelName = "LEFT_HAND_CHAN_1",
                Axis = "Y",
                Offset = 1.895,
                Spacing = 5.429,
                Quantity = 22,
                ZLocation = 0.750,
                ReferencedPart = "091-30102-462",
                Status = ChannelStatus.Valid
            },
            // North Wall (22 holes: Y=1.895 + i*5.429, Z=0.75)
            new()
            {
                RowIndex = 57,
                ChannelGroup = "North Wall Channels",
                ChannelName = "RIGHT_HAND_CHAN_1",
                Axis = "Y",
                Offset = 1.895,
                Spacing = 5.429,
                Quantity = 22,
                ZLocation = 0.750,
                ReferencedPart = "091-30102-461",
                Status = ChannelStatus.Valid
            },
            // Floor Chan 1 (16 holes: missing in physical model)
            new()
            {
                RowIndex = 6,
                ChannelGroup = "Floor Channels",
                ChannelName = "FLOOR_CHAN_1",
                Axis = "X",
                Offset = 9.000,
                Spacing = 5.267,
                Quantity = 16,
                ZLocation = 0.750,
                ReferencedPart = "091-30102-458",
                Status = ChannelStatus.Valid
            }
        };

        var expectedHoles = ExpectedHoleGenerator.GenerateExpectedHoles(items, segmentStartZOffset: 0.0, roofHeight: 118.0, unitWidth: 179.0);

        // 2. Synthesize actual physical holes found in CAD (from ChannelComparison.csv coordinates)
        var actualHoles = new List<ActualHole>();
        int id = 1;

        // FLOOR_CHAN_4 actuals (X=2.310, 5.000, Y=-0.0942, Z=0.750)
        actualHoles.Add(new ActualHole { Id = id++, Position = new Point3D(2.310, -0.0942, 0.750), Axis = new Vector3D(0, 1, 0), Diameter = 0.218, ParentPartNumber = "091-30102-461" });
        actualHoles.Add(new ActualHole { Id = id++, Position = new Point3D(5.000, -0.0942, 0.750), Axis = new Vector3D(0, 1, 0), Diameter = 0.218, ParentPartNumber = "091-30102-461" });

        // FLOOR_CHAN_5 actuals (X=175.000, 176.690, Y=-0.0418, Z=0.750)
        actualHoles.Add(new ActualHole { Id = id++, Position = new Point3D(175.000, -0.0418, 0.750), Axis = new Vector3D(0, 1, 0), Diameter = 0.218, ParentPartNumber = "091-30102-462" });
        actualHoles.Add(new ActualHole { Id = id++, Position = new Point3D(176.690, -0.0418, 0.750), Axis = new Vector3D(0, 1, 0), Diameter = 0.218, ParentPartNumber = "091-30102-462" });

        // South Wall actuals: shifted by +0.0087 in along Y (within 0.010 in tolerance)
        for (int i = 0; i < 22; i++)
        {
            double y = 1.895 + (i * 5.429) + 0.0087;
            actualHoles.Add(new ActualHole { Id = id++, Position = new Point3D(178.6375, y, 0.750), Axis = new Vector3D(1, 0, 0), Diameter = 0.218, ParentPartNumber = "091-30102-462" });
        }

        // North Wall actuals: shifted systematically by +0.105 in along Y (BtmBhdThk thickness shift)
        for (int i = 0; i < 22; i++)
        {
            double y = 1.895 + (i * 5.429) + 0.1050;
            actualHoles.Add(new ActualHole { Id = id++, Position = new Point3D(0.3625, y, 0.750), Axis = new Vector3D(1, 0, 0), Diameter = 0.218, ParentPartNumber = "091-30102-461" });
        }

        // Extra unrelated hole in CAD (e.g. lifting eye or wire passthrough)
        actualHoles.Add(new ActualHole { Id = id++, Position = new Point3D(90.0, 50.0, 10.0), Axis = new Vector3D(0, 0, 1), Diameter = 0.750, ParentPartNumber = "091-30102-450" });

        // 3. Execute matching engine
        var engine = new HoleMatchingEngine();
        var validation = engine.Validate(expectedHoles, actualHoles);

        // 4. Assertions

        // Floor Chan 4 & 5 should be 100% Match
        var fc4 = validation.Results.Where(r => r.ChannelName == "FLOOR_CHAN_4").ToList();
        Assert.Equal(2, fc4.Count);
        Assert.True(fc4.All(r => r.Status == HoleMatchStatus.Match));
        Assert.True(fc4.All(r => Math.Abs(r.DeltaAxis) < 0.0001 && Math.Abs(r.DeltaZ) < 0.0001));

        var fc5 = validation.Results.Where(r => r.ChannelName == "FLOOR_CHAN_5").ToList();
        Assert.Equal(2, fc5.Count);
        Assert.True(fc5.All(r => r.Status == HoleMatchStatus.Match));

        // South Wall: DeltaY = 0.0087 in -> Match (since <= 0.010 in tolerance)
        var sw = validation.Results.Where(r => r.ChannelName == "LEFT_HAND_CHAN_1").ToList();
        Assert.Equal(22, sw.Count);
        Assert.True(sw.All(r => r.Status == HoleMatchStatus.Match));
        Assert.True(sw.All(r => Math.Abs(r.DeltaAxis - 0.0087) < 0.0002));

        // North Wall: DeltaY = +0.105 in -> Mislocated (> 0.031 in tolerance)
        var nw = validation.Results.Where(r => r.ChannelName == "RIGHT_HAND_CHAN_1").ToList();
        Assert.Equal(22, nw.Count);
        Assert.True(nw.All(r => r.Status == HoleMatchStatus.Mislocated));

        // North Wall Pattern Diagnostic should flag systematic shift
        var nwDiag = validation.ChannelDiagnostics.First(d => d.ChannelName == "RIGHT_HAND_CHAN_1");
        Assert.True(nwDiag.IsSystematicShift);
        Assert.Equal(0.105, nwDiag.MeanSignedAxisDelta, precision: 3);
        Assert.Contains("+0.105\" Y shift", nwDiag.DiagnosticMessage);

        // Floor Chan 1: All 16 holes Missing
        var fc1 = validation.Results.Where(r => r.ChannelName == "FLOOR_CHAN_1").ToList();
        Assert.Equal(16, fc1.Count);
        Assert.True(fc1.All(r => r.Status == HoleMatchStatus.MissingExpected));

        // Extra hole should be present in results with ExtraActual status
        var extraResult = validation.Results.FirstOrDefault(r => r.Status == HoleMatchStatus.ExtraActual);
        Assert.NotNull(extraResult);
        Assert.Equal(0.750, extraResult!.Actual!.Diameter);

        // Overall summary counts
        Assert.Equal(2 + 2 + 22, validation.MatchedCount); // 26 matches (FC4, FC5, South Wall)
        Assert.Equal(22, validation.FailureCount); // 22 failures (North Wall)
        Assert.Equal(16, validation.MissingCount); // 16 missing (Floor Chan 1)
        Assert.Equal(1, validation.ExtraCount); // 1 extra CAD hole
    }
}
