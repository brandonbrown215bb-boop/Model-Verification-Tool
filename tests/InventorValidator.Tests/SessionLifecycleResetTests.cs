using System.Collections.Generic;
using InventorValidator.Excel;
using InventorValidator.Geometry.Models;
using InventorValidator.Inventor.Models;
using InventorValidator.Session;
using InventorValidator.UI.ViewModels;
using Xunit;

namespace InventorValidator.Tests;

public class SessionLifecycleResetTests
{
    private GeometryValidationResult CreateSampleValidationResult()
    {
        var result = new GeometryValidationResult
        {
            SessionId = "Model1_Session",
            SegmentStartReferenceName = "Assembly Z=0",
            SegmentStartZOffset = 0.0,
            TotalHolesExpected = 16,
            MatchedCount = 16,
            WarningCount = 0,
            FailureCount = 0,
            MissingCount = 0,
            ExtraCount = 1745
        };

        for (int i = 0; i < 16; i++)
        {
            result.Results.Add(new HoleMatchResult
            {
                Expected = new ExpectedHole { ChannelGroup = "Floor Channels", ChannelName = "FLOOR_CHAN_1", HoleIndex = i },
                Actual = new ActualHole { Id = i + 1, Position = new Point3D(i * 10, 0, 0), Diameter = 0.25 },
                Status = HoleMatchStatus.Match
            });
        }

        return result;
    }

    [Fact]
    public void ConsecutiveValidation_WhenSecondModelHasZeroChannels_ClearsStaleChannelResults()
    {
        // Setup ViewModels
        var channelVM = new ChannelResultsViewModel();
        var previewVM = new ParameterPreviewViewModel();
        var inventoryVM = new ModelInventoryViewModel();
        var calcVM = new CalculatorViewModel();

        // 1. Session 1 completes with channels (Model 1)
        var model1Geom = CreateSampleValidationResult();
        channelVM.LoadResults(model1Geom);
        Assert.True(channelVM.HasResults);
        Assert.Equal(16, channelVM.TotalExpectedCount);
        Assert.Equal(16, channelVM.MatchedCount);
        Assert.Equal(16, channelVM.AllResults.Count);

        // 2. User starts Session 2 (Model 2, zero channels)
        // MainViewModel.ExecuteStartValidationAsync resets all result VMs
        calcVM.Clear();
        inventoryVM.Clear();
        previewVM.Clear();
        channelVM.Reset("Validation session in progress...");

        Assert.False(channelVM.HasResults);
        Assert.Equal(0, channelVM.TotalExpectedCount);
        Assert.Empty(channelVM.AllResults);
        Assert.Equal("Validation session in progress...", channelVM.StatusMessage);

        // 3. Model 2 parses with 0 channels
        var model2Calc = new CalculatorSessionResult
        {
            WorkbookPath = @"C:\Temp\Calc_10006_026.xls",
            DataInputs = new List<DataInputItem>
            {
                new() { ParameterName = "IH", DisplayedValue = "118" }
            },
            ChannelLocations = new List<ChannelLocationItem>() // 0 channels
        };
        var model2Inv = new AssemblyInventoryResult
        {
            AssemblyName = "391-10006-026.iam",
            TotalOccurrencesCount = 8
        };
        var model2Comp = new ParameterComparisonResult();

        previewVM.LoadSessionData(new SessionManifest(), model2Calc, model2Inv, model2Comp, null!);
        channelVM.Reset("No channel locations defined in calculator for this model. Geometry validation will be skipped after apply.");

        // Wire callback as MainViewModel does
        previewVM.OnApplyCompletedWithoutGeometry = () =>
        {
            channelVM.Reset("No channel locations defined in calculator for this model. View applied parameters and model inventory for results.");
        };

        // Assert before apply: channelVM does not have model 1 results
        Assert.False(channelVM.HasResults);
        Assert.Empty(channelVM.AllResults);
        Assert.Equal(0, previewVM.SelectedTabIndex);

        // 4. Simulate Apply Mode completion when ChannelLocations.Count == 0
        previewVM.SelectedTabIndex = 2;
        previewVM.OnApplyCompletedWithoutGeometry.Invoke();

        // Assert after apply:
        Assert.Equal(2, previewVM.SelectedTabIndex); // Switched to Applied Results (Before vs After)
        Assert.False(channelVM.HasResults); // Old model results are NOT present
        Assert.Empty(channelVM.AllResults);
        Assert.Contains("No channel locations defined in calculator", channelVM.StatusMessage);
    }

    [Fact]
    public void ConsecutiveValidation_InspectMode_WhenSecondModelHasZeroChannels_ResetsChannelResults()
    {
        var channelVM = new ChannelResultsViewModel();

        // Session 1 leaves channel results
        channelVM.LoadResults(CreateSampleValidationResult());
        Assert.True(channelVM.HasResults);

        // Session 2 in Inspect mode with 0 channels
        channelVM.Reset("No channel locations defined in calculator for this model. Model inventory and parameter comparison verified.");

        Assert.False(channelVM.HasResults);
        Assert.Equal(0, channelVM.TotalExpectedCount);
        Assert.Equal(0, channelVM.MatchedCount);
        Assert.Empty(channelVM.AllResults);
        Assert.Contains("No channel locations defined", channelVM.StatusMessage);
    }
}
