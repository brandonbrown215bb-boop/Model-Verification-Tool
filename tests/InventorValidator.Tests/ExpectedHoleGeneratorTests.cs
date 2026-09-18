using System.Collections.Generic;
using InventorValidator.Excel;
using InventorValidator.Geometry;
using InventorValidator.Geometry.Models;
using Xunit;

namespace InventorValidator.Tests;

public class ExpectedHoleGeneratorTests
{
    [Fact]
    public void GenerateExpectedHoles_FloorChannel_GeneratesXArrayAndZLocation()
    {
        var item = new ChannelLocationItem
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
        };

        var holes = ExpectedHoleGenerator.GenerateExpectedHoles(new[] { item }, segmentStartZOffset: 0.0);

        Assert.Equal(2, holes.Count);

        // Hole 0
        Assert.Equal(0, holes[0].HoleIndex);
        Assert.Equal(2.310, holes[0].ExpectedPosition.X, precision: 4);
        Assert.Equal(0.750, holes[0].ExpectedPosition.Z, precision: 4);
        Assert.Equal(AuthoritativeAxisPair.XZ, holes[0].AuthoritativeAxes);

        // Hole 1
        Assert.Equal(1, holes[1].HoleIndex);
        Assert.Equal(5.000, holes[1].ExpectedPosition.X, precision: 4);
        Assert.Equal(0.750, holes[1].ExpectedPosition.Z, precision: 4);
    }

    [Fact]
    public void GenerateExpectedHoles_SouthWall_GeneratesYArrayAndZLocation()
    {
        var item = new ChannelLocationItem
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
        };

        var holes = ExpectedHoleGenerator.GenerateExpectedHoles(new[] { item }, segmentStartZOffset: 0.5);

        Assert.Equal(22, holes.Count);
        Assert.Equal(1.895, holes[0].ExpectedPosition.Y, precision: 4);
        Assert.Equal(1.250, holes[0].ExpectedPosition.Z, precision: 4); // 0.750 + 0.5
        Assert.Equal(AuthoritativeAxisPair.YZ, holes[0].AuthoritativeAxes);
    }

    [Fact]
    public void GenerateExpectedHoles_SkippedOrInvalidRow_ProducesZeroHoles()
    {
        var skippedItem = new ChannelLocationItem
        {
            RowIndex = 30,
            ChannelGroup = "Roof Channels",
            ChannelName = "ROOF_CHAN_3",
            Status = ChannelStatus.SkippedInvalidRow,
            Quantity = null,
            Offset = null
        };

        var holes = ExpectedHoleGenerator.GenerateExpectedHoles(new[] { skippedItem });
        Assert.Empty(holes);
    }
}
