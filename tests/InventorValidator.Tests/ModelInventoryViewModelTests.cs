using InventorValidator.Inventor.Models;
using InventorValidator.UI.ViewModels;
using Xunit;

namespace InventorValidator.Tests;

public class ModelInventoryViewModelTests
{
    [Fact]
    public void LoadResults_WhenAllOccurrencesActive_ReportsCorrectActiveAndZeroSuppressed()
    {
        // Arrange
        var vm = new ModelInventoryViewModel();
        var occurrences = new List<OccurrenceInventoryItem>();
        for (int i = 1; i <= 52; i++)
        {
            occurrences.Add(new OccurrenceInventoryItem
            {
                OccurrenceName = $"Part_{i}:1",
                PartNumber = $"Part_{i}",
                IsSuppressed = false
            });
        }

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Occurrences = occurrences,
            ActiveOccurrencesCount = 52,
            SuppressedOccurrencesCount = 0,
            TotalOccurrencesCount = 52
        };

        // Act
        vm.LoadResults(inventory);

        // Assert
        Assert.Equal(52, vm.TotalOccurrences);
        Assert.Equal(52, vm.ActiveOccurrences);
        Assert.Equal(0, vm.SuppressedOccurrences);
        Assert.Equal(52, vm.OccurrenceRows.Count);
        Assert.All(vm.OccurrenceRows, row => Assert.True(row.IsActive));
        Assert.All(vm.OccurrenceRows, row => Assert.False(row.IsSuppressed));
        Assert.All(vm.OccurrenceRows, row => Assert.Equal("Active", row.StateText));
    }

    [Fact]
    public void LoadResults_WithMixedSuppression_ReportsAccurateActiveAndSuppressedMetrics()
    {
        // Arrange
        var vm = new ModelInventoryViewModel();
        var occurrences = new List<OccurrenceInventoryItem>();

        // 40 Active, 12 Suppressed
        for (int i = 1; i <= 40; i++)
        {
            occurrences.Add(new OccurrenceInventoryItem
            {
                OccurrenceName = $"ActivePart_{i}:1",
                PartNumber = $"ActivePart_{i}",
                IsSuppressed = false
            });
        }
        for (int i = 1; i <= 12; i++)
        {
            occurrences.Add(new OccurrenceInventoryItem
            {
                OccurrenceName = $"SuppressedPart_{i}:1",
                PartNumber = $"SuppressedPart_{i}",
                IsSuppressed = true
            });
        }

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Occurrences = occurrences,
            ActiveOccurrencesCount = 40,
            SuppressedOccurrencesCount = 12,
            TotalOccurrencesCount = 52
        };

        // Act
        vm.LoadResults(inventory);

        // Assert
        Assert.Equal(52, vm.TotalOccurrences);
        Assert.Equal(40, vm.ActiveOccurrences);
        Assert.Equal(12, vm.SuppressedOccurrences);
        Assert.Equal(52, vm.OccurrenceRows.Count);
        Assert.Equal(40, vm.OccurrenceRows.Count(r => r.IsActive));
        Assert.Equal(12, vm.OccurrenceRows.Count(r => r.IsSuppressed));
    }

    [Fact]
    public void LoadResults_WithNestedHierarchies_FlattensAllChildrenAndPreservesCounts()
    {
        // Arrange
        var vm = new ModelInventoryViewModel();
        var parentOcc = new OccurrenceInventoryItem
        {
            OccurrenceName = "SubAssembly:1",
            PartNumber = "SubAssembly",
            IsSuppressed = false,
            Children = new List<OccurrenceInventoryItem>
            {
                new() { OccurrenceName = "Child1:1", PartNumber = "Child1", IsSuppressed = false },
                new() { OccurrenceName = "Child2:1", PartNumber = "Child2", IsSuppressed = true }
            }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "ParentAssembly.iam",
            Occurrences = new List<OccurrenceInventoryItem> { parentOcc },
            ActiveOccurrencesCount = 2,
            SuppressedOccurrencesCount = 1,
            TotalOccurrencesCount = 3
        };

        // Act
        vm.LoadResults(inventory);

        // Assert
        Assert.Equal(3, vm.TotalOccurrences);
        Assert.Equal(2, vm.ActiveOccurrences);
        Assert.Equal(1, vm.SuppressedOccurrences);
        Assert.Equal(3, vm.OccurrenceRows.Count);
    }

    [Fact]
    public void Clear_ResetsAllModelInventoryData()
    {
        var vm = new ModelInventoryViewModel();
        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Occurrences = new List<OccurrenceInventoryItem>
            {
                new() { OccurrenceName = "Part:1", PartNumber = "Part", IsSuppressed = false }
            },
            TotalOccurrencesCount = 1,
            ActiveOccurrencesCount = 1
        };

        vm.LoadResults(inventory);
        Assert.Equal(1, vm.TotalOccurrences);
        Assert.Equal("TestAssembly.iam", vm.AssemblyName);

        vm.Clear();

        Assert.Null(vm.Inventory);
        Assert.Null(vm.Comparison);
        Assert.Equal(0, vm.TotalOccurrences);
        Assert.Equal("No assembly loaded", vm.AssemblyName);
        Assert.Empty(vm.ParameterRows);
        Assert.Empty(vm.OccurrenceRows);
        Assert.Empty(vm.FeatureRows);
    }
}
