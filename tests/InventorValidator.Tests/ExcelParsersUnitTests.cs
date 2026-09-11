using InventorValidator.Excel;
using InventorValidator.Excel.Parsers;
using Xunit;

namespace InventorValidator.Tests;

public class ExcelParsersUnitTests
{
    [Fact]
    public void ExcelCellError_ShouldDetectAndFormatErrorsCorrectly()
    {
        Assert.True(ExcelCellError.IsError(ExcelCellError.ErrRef));
        Assert.Equal("#REF!", ExcelCellError.FormatError(ExcelCellError.ErrRef));

        Assert.True(ExcelCellError.IsError(ExcelCellError.ErrValue));
        Assert.Equal("#VALUE!", ExcelCellError.FormatError(ExcelCellError.ErrValue));

        Assert.True(ExcelCellError.IsError(ExcelCellError.ErrDiv0));
        Assert.Equal("#DIV/0!", ExcelCellError.FormatError(ExcelCellError.ErrDiv0));

        Assert.True(ExcelCellError.IsError("#REF!"));
        Assert.Equal("#REF!", ExcelCellError.FormatError("#REF!"));

        Assert.False(ExcelCellError.IsError(123.45));
        Assert.Null(ExcelCellError.FormatError(123.45));

        Assert.False(ExcelCellError.IsError("Normal Text"));
        Assert.Null(ExcelCellError.FormatError("Normal Text"));
    }

    [Fact]
    public void Sheet1TabParser_NormalizeParameterName_ShouldTrimAndReplaceHyphens()
    {
        Assert.Equal("Part_091_30102_461_1", Sheet1TabParser.NormalizeParameterName("  Part-091-30102-461-1  "));
        Assert.Equal("IH", Sheet1TabParser.NormalizeParameterName(" IH "));
        Assert.Equal("Top_Bhd_Thk", Sheet1TabParser.NormalizeParameterName("Top Bhd Thk"));
    }

    [Theory]
    [InlineData("Part_091_30102_461_1", "ul", ParameterCategory.SuppressionControl)]
    [InlineData("Part_091_30102_496_2", "", ParameterCategory.SuppressionControl)]
    [InlineData("IH", "in", ParameterCategory.Dimension)]
    [InlineData("IW", "in", ParameterCategory.Dimension)]
    [InlineData("TopBhdThk", "in", ParameterCategory.Dimension)]
    [InlineData("CoilsHigh", "ul", ParameterCategory.UnitlessCount)]
    [InlineData("RowsDeep", "ul", ParameterCategory.UnitlessCount)]
    [InlineData("Feature_Hole1", "", ParameterCategory.FeatureControl)]
    [InlineData("MaterialStyle", "", ParameterCategory.Property)]
    public void Sheet1TabParser_ClassifyParameter_ShouldCategorizeAccurately(string name, string unit, ParameterCategory expected)
    {
        var category = Sheet1TabParser.ClassifyParameter(name, unit, 1.0);
        Assert.Equal(expected, category);
    }

    [Fact]
    public void Sheet1TabParser_Parse_ShouldParseRowsAndDetectFormulaErrors()
    {
        // Construct 1-based 2D array matching Excel COM UsedRange
        var array = new object[4, 5];
        // Headers
        array[1, 1] = "Parameter";
        array[1, 2] = "Value";
        array[1, 3] = "UM";
        array[1, 4] = "Comments";

        // Row 2: Normal dimension
        array[2, 1] = "IH";
        array[2, 2] = 118.0;
        array[2, 3] = "in";
        array[2, 4] = "Inside Height";

        // Row 3: Formula error #REF!
        array[3, 1] = "BadParam";
        array[3, 2] = ExcelCellError.ErrRef;
        array[3, 3] = "in";
        array[3, 4] = "Corrupted cell";

        var result = Sheet1TabParser.Parse(array);

        Assert.Equal(2, result.Count);

        var p1 = result[0];
        Assert.Equal("IH", p1.ParameterName);
        Assert.Equal("118", p1.DisplayedValue);
        Assert.Equal("in", p1.Unit);
        Assert.Equal(ParameterCategory.Dimension, p1.Category);
        Assert.Null(p1.FormulaError);
        Assert.True(p1.IsSafeToWrite);

        var p2 = result[1];
        Assert.Equal("BadParam", p2.ParameterName);
        Assert.Equal("#REF!", p2.FormulaError);
        Assert.False(p2.IsSafeToWrite);
    }

    [Fact]
    public void DataTabParser_Parse_ShouldExtractInputsAndErrorCheck()
    {
        var array = new object[5, 6];
        // Headers
        array[1, 1] = "Input parameter";
        array[1, 2] = "Current value";
        array[1, 3] = "Description";
        array[1, 4] = "Values For MOM";
        array[1, 5] = "Error Check";

        // Row 2: Input
        array[2, 1] = "IH";
        array[2, 2] = 118.0;
        array[2, 3] = "Inside Height";
        array[2, 4] = "Numeric > 0";
        array[2, 5] = 0.0;

        // Row 3: Input
        array[3, 1] = "IW";
        array[3, 2] = 179.0;
        array[3, 3] = "Inside Width";
        array[3, 4] = "Numeric > 0";
        array[3, 5] = 0.0;

        // Row 4: Error_Check summary
        array[4, 1] = "Error_Check";
        array[4, 2] = 0.0;

        var (inputs, errVal, errRaw, isPassed) = DataTabParser.Parse(array);

        Assert.Equal(2, inputs.Count);
        Assert.Equal("IH", inputs[0].ParameterName);
        Assert.Equal("118", inputs[0].DisplayedValue);
        Assert.Equal(0.0, errVal);
        Assert.True(isPassed);
    }

    [Fact]
    public void DataTabParser_Parse_WhenErrorCheckNonZero_ShouldFailCheck()
    {
        var array = new object[3, 3];
        array[1, 1] = "Input parameter";
        array[1, 2] = "Current value";

        array[2, 1] = "Error_Check";
        array[2, 2] = 1.0; // Failure

        var (_, errVal, _, isPassed) = DataTabParser.Parse(array);

        Assert.Equal(1.0, errVal);
        Assert.False(isPassed);
    }

    [Fact]
    public void ChannelLocTabParser_Parse_ShouldExtractChannelsAndHandleRefErrors()
    {
        var array = new object[8, 9];
        // Floor channels header
        array[1, 2] = "Channel Name";
        array[1, 3] = "Z_LOCATION";
        array[1, 4] = "X_ARRAY_OFFSET";
        array[1, 5] = "X_ARRAY_QTY";
        array[1, 6] = "X_ARRAY_SPACING";
        array[1, 7] = "FLOOR CHANNELS";

        // Valid channel: FLOOR_CHAN_1
        array[2, 2] = "FLOOR_CHAN_1";
        array[2, 3] = 0.750;
        array[2, 4] = 9.000;
        array[2, 5] = 4;
        array[2, 6] = 5.267;
        array[2, 8] = "091-30102-458";

        // Inactive channel: FLOOR_CHAN_6 (0 qty)
        array[3, 2] = "FLOOR_CHAN_6";
        array[3, 3] = 0.0;
        array[3, 4] = 0.0;
        array[3, 5] = 0;
        array[3, 6] = 0.0;

        // Roof channels header
        array[4, 2] = "Channel Name";
        array[4, 3] = "Z_LOCATION";
        array[4, 4] = "X_ARRAY_OFFSET";
        array[4, 5] = "X_ARRAY_QTY";
        array[4, 6] = "X_ARRAY_SPACING";
        array[4, 7] = "ROOF CHANNELS";

        // Orphaned #REF! channel: ROOF_CHAN_3
        array[5, 2] = "ROOF_CHAN_3";
        array[5, 3] = ExcelCellError.ErrRef;
        array[5, 4] = ExcelCellError.ErrRef;
        array[5, 5] = ExcelCellError.ErrRef;
        array[5, 6] = ExcelCellError.ErrRef;

        // South wall channels header
        array[6, 2] = "Channel Name";
        array[6, 3] = "Z_LOCATION";
        array[6, 4] = "Y_ARRAY_OFFSET";
        array[6, 5] = "Y_ARRAY_QTY";
        array[6, 6] = "Y_ARRAY_SPACING";
        array[6, 7] = "SOUTH WALL CHANNELS";

        // Valid south wall channel without any referenced part number
        array[7, 2] = "LEFT_HAND_CHAN_1";
        array[7, 3] = 0.750;
        array[7, 4] = 1.895;
        array[7, 5] = 22;
        array[7, 6] = 5.429;
        // Notice array[7, 8] is left null/empty: Part numbers are optional!

        var items = ChannelLocTabParser.Parse(array);

        Assert.Equal(4, items.Count);

        // FLOOR_CHAN_1: Valid
        var fc1 = items[0];
        Assert.Equal("FLOOR_CHAN_1", fc1.ChannelName);
        Assert.Equal("X", fc1.Axis);
        Assert.Equal(ChannelStatus.Valid, fc1.Status);
        Assert.Equal(4, fc1.Quantity);
        var positions = fc1.GenerateExpectedPositions();
        Assert.Equal(4, positions.Count);
        Assert.Equal(9.000, positions[0], precision: 3);
        Assert.Equal(9.000 + (3 * 5.267), positions[3], precision: 3);

        // FLOOR_CHAN_6: Skipped
        var fc6 = items[1];
        Assert.Equal(ChannelStatus.SkippedInvalidRow, fc6.Status);

        // ROOF_CHAN_3: Skipped due to #REF!
        var rc3 = items[2];
        Assert.Equal("ROOF_CHAN_3", rc3.ChannelName);
        Assert.Equal("Roof Channels", rc3.ChannelGroup);
        Assert.Equal(ChannelStatus.SkippedInvalidRow, rc3.Status);
        Assert.Contains("#REF!", rc3.Notes);

        // LEFT_HAND_CHAN_1: Valid without referenced part number
        var lhc1 = items[3];
        Assert.Equal("LEFT_HAND_CHAN_1", lhc1.ChannelName);
        Assert.Equal("South Wall Channels", lhc1.ChannelGroup);
        Assert.Equal("Y", lhc1.Axis);
        Assert.Equal(ChannelStatus.Valid, lhc1.Status);
        Assert.Equal(22, lhc1.Quantity);
        Assert.True(string.IsNullOrEmpty(lhc1.ReferencedPart));
    }

    [Fact]
    public void ChannelLocTabParser_Parse_WhenZLocationZero_ShouldMarkSkippedWithZeroZNote()
    {
        var array = new object[3, 8];
        array[1, 1] = "Channel Name";
        array[1, 2] = "Z_LOCATION";
        array[1, 3] = "X_ARRAY_OFFSET";
        array[1, 4] = "X_ARRAY_QTY";
        array[1, 5] = "X_ARRAY_SPACING";
        array[1, 6] = "FLOOR CHANNELS";

        // Row has non-zero qty and offset, but Z is 0.0
        array[2, 1] = "FLOOR_CHAN_2";
        array[2, 2] = 0.000;
        array[2, 3] = 10.0;
        array[2, 4] = 4;
        array[2, 5] = 5.0;

        var items = ChannelLocTabParser.Parse(array);
        Assert.Single(items);
        Assert.Equal(ChannelStatus.SkippedInvalidRow, items[0].Status);
        Assert.Contains("Zero Z-location", items[0].Notes);
    }

    [Fact]
    public void ChannelUnificationService_Unify_WhenSameZAcrossSides_ShouldExpressAsOneChannel()
    {
        var rawItems = new List<ChannelLocationItem>
        {
            new() { ChannelGroup = "Floor Channels", ChannelName = "FLOOR_CHAN_1", ZLocation = 1.0, Offset = 1.0, Quantity = 3, Spacing = 5.0, Status = ChannelStatus.Valid },
            new() { ChannelGroup = "Roof Channels", ChannelName = "ROOF_CHAN_1", ZLocation = 1.0, Offset = 1.0, Quantity = 3, Spacing = 5.0, Status = ChannelStatus.Valid },
            new() { ChannelGroup = "South Wall Channels", ChannelName = "LEFT_HAND_CHAN_1", ZLocation = 1.0, Offset = 2.0, Quantity = 10, Spacing = 5.0, Status = ChannelStatus.Valid },
            new() { ChannelGroup = "North Wall Channels", ChannelName = "RIGHT_HAND_CHAN_1", ZLocation = 1.0, Offset = 2.0, Quantity = 10, Spacing = 5.0, Status = ChannelStatus.Valid }
        };

        var unified = ChannelUnificationService.Unify(rawItems);

        Assert.Single(unified);
        var ch = unified[0];
        Assert.Equal(1.0, ch.ZLocation);
        Assert.Equal(4, ch.SideCount);
        Assert.Contains("Floor", ch.SidesSummary);
        Assert.Contains("Roof", ch.SidesSummary);
        Assert.Contains("South Wall (Left)", ch.SidesSummary);
        Assert.Contains("North Wall (Right)", ch.SidesSummary);
        Assert.Equal(4, ch.SegmentCount);
        Assert.Equal(26, ch.TotalHoles); // 3 + 3 + 10 + 10
        Assert.Equal(ChannelStatus.Valid, ch.Status);
    }

    [Fact]
    public void ChannelUnificationService_Unify_WhenDifferentZ_ShouldExpressAsSeparateChannels()
    {
        var rawItems = new List<ChannelLocationItem>
        {
            new() { ChannelGroup = "Floor Channels", ChannelName = "FLOOR_CHAN_1", ZLocation = 0.5, Offset = 1.0, Quantity = 4, Spacing = 5.0, Status = ChannelStatus.Valid },
            new() { ChannelGroup = "Roof Channels", ChannelName = "ROOF_CHAN_5", ZLocation = 5.5, Offset = 60.0, Quantity = 1, Spacing = 0.0, Status = ChannelStatus.Valid }
        };

        var unified = ChannelUnificationService.Unify(rawItems);

        Assert.Equal(2, unified.Count);
        Assert.Equal(0.5, unified[0].ZLocation);
        Assert.Equal(5.5, unified[1].ZLocation);
    }
}
