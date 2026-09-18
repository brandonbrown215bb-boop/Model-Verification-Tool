using System.Collections.Generic;
using System.Linq;
using InventorValidator.Geometry.Models;
using InventorValidator.UI.ViewModels;
using Xunit;

namespace InventorValidator.Tests;

public class ChannelResultsViewModelTests
{
    private GeometryValidationResult CreateSampleValidationResult()
    {
        var result = new GeometryValidationResult
        {
            SessionId = "TestSession",
            SegmentStartReferenceName = "Bhd Loc from Seg Start",
            SegmentStartZOffset = 0.750,
            TotalHolesExpected = 10,
            MatchedCount = 6,
            WarningCount = 1,
            FailureCount = 2,
            MissingCount = 1,
            ExtraCount = 2
        };

        // Add 6 matches
        for (int i = 0; i < 6; i++)
        {
            result.Results.Add(new HoleMatchResult
            {
                Expected = new ExpectedHole { ChannelGroup = "Floor Channels", ChannelName = "FLOOR_CHAN_4", HoleIndex = i, ReferencedPart = "091-30102-461" },
                Actual = new ActualHole { Id = i + 1, Position = new Point3D(2.31 + i, 0, 0.75), Diameter = 0.218, ParentPartNumber = "091-30102-461" },
                Status = HoleMatchStatus.Match,
                DeltaAxis = 0.001,
                DeltaZ = 0.0
            });
        }

        // Add 1 warning
        result.Results.Add(new HoleMatchResult
        {
            Expected = new ExpectedHole { ChannelGroup = "South Wall Channels", ChannelName = "LEFT_HAND_CHAN_1", HoleIndex = 0, ReferencedPart = "091-30102-462" },
            Actual = new ActualHole { Id = 7, Position = new Point3D(179, 1.915, 0.75), Diameter = 0.218, ParentPartNumber = "091-30102-462" },
            Status = HoleMatchStatus.Warning,
            DeltaAxis = 0.020,
            DeltaZ = 0.0
        });

        // Add 2 failures
        for (int i = 0; i < 2; i++)
        {
            result.Results.Add(new HoleMatchResult
            {
                Expected = new ExpectedHole { ChannelGroup = "North Wall Channels", ChannelName = "RIGHT_HAND_CHAN_1", HoleIndex = i, ReferencedPart = "091-30102-461" },
                Actual = new ActualHole { Id = 8 + i, Position = new Point3D(0, 2.000 + i, 0.75), Diameter = 0.218, ParentPartNumber = "091-30102-461" },
                Status = HoleMatchStatus.Mislocated,
                DeltaAxis = 0.105,
                DeltaZ = 0.0
            });
        }

        // Add 1 missing
        result.Results.Add(new HoleMatchResult
        {
            Expected = new ExpectedHole { ChannelGroup = "Roof Channels", ChannelName = "ROOF_CHAN_1", HoleIndex = 0, ReferencedPart = "091-30102-461" },
            Actual = null,
            Status = HoleMatchStatus.MissingExpected
        });

        // Add 2 extra
        result.Results.Add(new HoleMatchResult
        {
            Expected = null,
            Actual = new ActualHole { Id = 101, Position = new Point3D(50, 50, 10), Diameter = 0.500, ParentPartNumber = "Unrelated_Part" },
            Status = HoleMatchStatus.ExtraActual
        });
        result.Results.Add(new HoleMatchResult
        {
            Expected = null,
            Actual = new ActualHole { Id = 102, Position = new Point3D(60, 60, 10), Diameter = 0.500, ParentPartNumber = "Unrelated_Part" },
            Status = HoleMatchStatus.ExtraActual
        });

        // Pattern diagnostic
        result.ChannelDiagnostics.Add(new PatternDiagnosticResult
        {
            ChannelGroup = "North Wall Channels",
            ChannelName = "RIGHT_HAND_CHAN_1",
            IsSystematicShift = true,
            DiagnosticMessage = "Systematic +0.105\" Y shift across 2 holes"
        });

        return result;
    }

    [Fact]
    public void LoadResults_PopulatesSummaryKPIsAndItemsCorrectly()
    {
        var vm = new ChannelResultsViewModel();
        var data = CreateSampleValidationResult();

        vm.LoadResults(data);

        Assert.True(vm.HasResults);
        Assert.Equal(10, vm.TotalExpectedCount);
        Assert.Equal(6, vm.MatchedCount);
        Assert.Equal(1, vm.WarningCount);
        Assert.Equal(2, vm.FailureCount);
        Assert.Equal(1, vm.MissingCount);
        Assert.Equal(2, vm.ExtraCount);
        Assert.Equal("Bhd Loc from Seg Start", vm.SegmentStartReference);
        Assert.Equal("0.7500\"", vm.SegmentStartZOffset);

        // 6 + 1 + 2 + 1 + 2 = 12 total items
        Assert.Equal(12, vm.AllResults.Count);
        Assert.Equal(12, vm.FilteredResults.Count);
    }

    [Fact]
    public void FilterByGroup_WhenSelected_ShowsOnlyMatchingGroup()
    {
        var vm = new ChannelResultsViewModel();
        vm.LoadResults(CreateSampleValidationResult());

        vm.SelectedGroupFilter = "Floor Channels";

        Assert.Equal(6, vm.FilteredResults.Count);
        Assert.All(vm.FilteredResults, r => Assert.Equal("Floor Channels", r.ChannelGroup));
    }

    [Fact]
    public void FilterByStatus_WhenSelected_ShowsOnlyMatchingStatus()
    {
        var vm = new ChannelResultsViewModel();
        vm.LoadResults(CreateSampleValidationResult());

        vm.SelectedStatusFilter = "Matches Only";
        Assert.Equal(6, vm.FilteredResults.Count);

        vm.SelectedStatusFilter = "Failures/Discrepancies";
        Assert.Equal(2, vm.FilteredResults.Count);

        vm.SelectedStatusFilter = "Missing Only";
        Assert.Single(vm.FilteredResults);

        vm.SelectedStatusFilter = "Extra Holes";
        Assert.Equal(2, vm.FilteredResults.Count);
    }

    [Fact]
    public void SearchText_FiltersOnChannelOrPartName()
    {
        var vm = new ChannelResultsViewModel();
        vm.LoadResults(CreateSampleValidationResult());

        vm.SearchText = "LEFT_HAND_CHAN_1";
        Assert.Single(vm.FilteredResults);
        Assert.Equal("LEFT_HAND_CHAN_1", vm.FilteredResults[0].ChannelName);

        vm.SearchText = "Unrelated_Part";
        Assert.Equal(2, vm.FilteredResults.Count);
    }

    [Fact]
    public void ClearFiltersCommand_ResetsAllFilters()
    {
        var vm = new ChannelResultsViewModel();
        vm.LoadResults(CreateSampleValidationResult());

        vm.SelectedGroupFilter = "North Wall Channels";
        vm.SelectedStatusFilter = "Failures/Discrepancies";
        vm.SearchText = "RIGHT_HAND";
        Assert.Equal(2, vm.FilteredResults.Count);

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal("All Groups", vm.SelectedGroupFilter);
        Assert.Equal("All Statuses", vm.SelectedStatusFilter);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal(12, vm.FilteredResults.Count);
    }

    [Fact]
    public void SelectedChannelFilter_UpdatesDiagnosticBanner()
    {
        var vm = new ChannelResultsViewModel();
        vm.LoadResults(CreateSampleValidationResult());

        vm.SelectedChannelFilter = "RIGHT_HAND_CHAN_1";

        Assert.True(vm.HasSelectedDiagnostic);
        Assert.NotNull(vm.SelectedDiagnostic);
        Assert.True(vm.SelectedDiagnostic!.IsSystematicShift);
        Assert.Contains("+0.105\" Y shift", vm.SelectedDiagnostic.DiagnosticMessage);
    }

    [Fact]
    public void Reset_ClearsAllResultsAndRestoresInitialState()
    {
        var vm = new ChannelResultsViewModel();
        vm.LoadResults(CreateSampleValidationResult());
        Assert.True(vm.HasResults);
        Assert.Equal(12, vm.AllResults.Count);
        Assert.Equal(10, vm.TotalExpectedCount);

        vm.Reset("Custom reset message for testing");

        Assert.False(vm.HasResults);
        Assert.Equal("Custom reset message for testing", vm.StatusMessage);
        Assert.Empty(vm.AllResults);
        Assert.Empty(vm.FilteredResults);
        Assert.Empty(vm.ChannelDiagnostics);
        Assert.Empty(vm.AvailableChannelNames);
        Assert.Null(vm.SelectedDiagnostic);
        Assert.False(vm.HasSelectedDiagnostic);
        Assert.Equal(0, vm.TotalExpectedCount);
        Assert.Equal(0, vm.MatchedCount);
        Assert.Equal(0, vm.WarningCount);
        Assert.Equal(0, vm.FailureCount);
        Assert.Equal(0, vm.MissingCount);
        Assert.Equal(0, vm.ExtraCount);
        Assert.Equal("Assembly Z=0", vm.SegmentStartReference);
        Assert.Equal("0.0000\"", vm.SegmentStartZOffset);
        Assert.Null(vm.SelectedItem);
    }

    [Fact]
    public void SelectedItem_WhenResultsLoaded_DefaultsToFirstItem()
    {
        var vm = new ChannelResultsViewModel();
        vm.LoadResults(CreateSampleValidationResult());

        Assert.NotNull(vm.SelectedItem);
        Assert.Equal(vm.FilteredResults[0], vm.SelectedItem);
    }

    [Fact]
    public void SetStatusFilterCommand_UpdatesSelectedStatusFilterAndFilteredResults()
    {
        var vm = new ChannelResultsViewModel();
        vm.LoadResults(CreateSampleValidationResult());

        vm.SetStatusFilterCommand.Execute("Failures/Discrepancies");

        Assert.Equal("Failures/Discrepancies", vm.SelectedStatusFilter);
        Assert.Equal(2, vm.FilteredResults.Count);
        Assert.All(vm.FilteredResults, r => Assert.Equal(HoleMatchStatus.Mislocated, r.Status));
    }

    [Fact]
    public void SetGroupFilterCommand_UpdatesSelectedGroupFilter()
    {
        var vm = new ChannelResultsViewModel();
        vm.LoadResults(CreateSampleValidationResult());

        vm.SetGroupFilterCommand.Execute("Roof Channels");

        Assert.Equal("Roof Channels", vm.SelectedGroupFilter);
        Assert.Equal(1, vm.FilteredResults.Count);
        Assert.Equal(HoleMatchStatus.MissingExpected, vm.FilteredResults[0].Status);
    }

    [Fact]
    public void ShowOverlaysInInventor_TogglesStateCorrectly()
    {
        var vm = new ChannelResultsViewModel();
        Assert.True(vm.ShowOverlaysInInventor);

        vm.ShowOverlaysInInventor = false;
        Assert.False(vm.ShowOverlaysInInventor);

        vm.ShowOverlaysInInventor = true;
        Assert.True(vm.ShowOverlaysInInventor);
    }

    [Fact]
    public void ClearOverlaysCommand_CanExecuteSafelyWithoutSession()
    {
        var vm = new ChannelResultsViewModel();
        vm.ClearOverlaysCommand.Execute(null); // Should not throw
    }
}
