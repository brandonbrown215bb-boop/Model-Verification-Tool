using InventorValidator.Excel;
using InventorValidator.Inventor.Models;
using InventorValidator.Session;
using InventorValidator.UI.ViewModels;
using Xunit;

namespace InventorValidator.Tests;

public class ParameterPreviewViewModelTests
{
    [Fact]
    public void DataInputItemViewModel_WhenValueChanged_MarksModified()
    {
        var item = new DataInputItemViewModel
        {
            ParameterName = "IH",
            OriginalValue = "118",
            CurrentValue = "118",
            Description = "Unit Height"
        };

        Assert.False(item.IsModified);

        item.CurrentValue = "124";
        Assert.True(item.IsModified);

        item.Reset();
        Assert.False(item.IsModified);
        Assert.Equal("118", item.CurrentValue);
    }

    [Fact]
    public void LoadSessionData_PopulatesInputsAndRulesWithoutHardcoding()
    {
        var vm = new ParameterPreviewViewModel();
        var manifest = new SessionManifest();

        var calcResult = new CalculatorSessionResult
        {
            DataInputs = new List<DataInputItem>
            {
                new() { ParameterName = "IH", DisplayedValue = "118", Description = "Height" },
                new() { ParameterName = "IW", DisplayedValue = "179", Description = "Width" }
            }
        };

        var invResult = new AssemblyInventoryResult
        {
            AvailableRules = new List<string> { "CustomRule_A", "CustomRule_B" }
        };

        var compResult = new ParameterComparisonResult
        {
            Rows = new List<ParameterComparisonRow>
            {
                new() { ExcelParameterName = "IH", TargetParameterName = "IH", Status = ComparisonStatus.Match }
            }
        };

        // Pass null session for UI state testing
        vm.LoadSessionData(manifest, calcResult, invResult, compResult, null!);

        Assert.Equal(2, vm.DataInputs.Count);
        // Includes All Rules option + 2 detected rules + Skip option = 4
        Assert.Equal(4, vm.AvailableRules.Count);
        Assert.Contains(ParameterPreviewViewModel.AllRulesOption, vm.AvailableRules);
        Assert.Equal(ParameterPreviewViewModel.AllRulesOption, vm.SelectedRule);
        Assert.Single(vm.PlannedChanges);
        Assert.False(vm.HasAppliedResults);
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void LoadSessionData_WhenSuppressionRuleExists_PrioritizesSuppressionRule()
    {
        var vm = new ParameterPreviewViewModel();
        var manifest = new SessionManifest();
        var calcResult = new CalculatorSessionResult();
        var compResult = new ParameterComparisonResult();

        var invResult = new AssemblyInventoryResult
        {
            AvailableRules = new List<string> { "Home View", "Part Suppression" }
        };

        vm.LoadSessionData(manifest, calcResult, invResult, compResult, null!);

        // Even though "Home View" is first, "Part Suppression" should be prioritized as default
        Assert.Equal("Part Suppression", vm.SelectedRule);
    }

    [Fact]
    public void LoadSessionData_WhenNoRulesExist_DefaultsToSkipOption()
    {
        var vm = new ParameterPreviewViewModel();
        var manifest = new SessionManifest();

        var calcResult = new CalculatorSessionResult
        {
            DataInputs = new List<DataInputItem>()
        };

        var invResult = new AssemblyInventoryResult
        {
            AvailableRules = new List<string>() // empty
        };

        var compResult = new ParameterComparisonResult();

        vm.LoadSessionData(manifest, calcResult, invResult, compResult, null!);

        Assert.Single(vm.AvailableRules);
        Assert.Equal("None (Skip Rule Execution)", vm.SelectedRule);
    }

    [Fact]
    public void ResetAllInputs_RevertsAllModifications()
    {
        var vm = new ParameterPreviewViewModel();
        var manifest = new SessionManifest();

        var calcResult = new CalculatorSessionResult
        {
            DataInputs = new List<DataInputItem>
            {
                new() { ParameterName = "IH", DisplayedValue = "118" },
                new() { ParameterName = "IW", DisplayedValue = "179" }
            }
        };

        var invResult = new AssemblyInventoryResult();
        var compResult = new ParameterComparisonResult();

        vm.LoadSessionData(manifest, calcResult, invResult, compResult, null!);

        vm.DataInputs[0].CurrentValue = "120";
        vm.DataInputs[1].CurrentValue = "185";
        Assert.True(vm.HasModifiedInputs);

        vm.ResetAllInputsCommand.Execute(null);

        Assert.False(vm.HasModifiedInputs);
        Assert.Equal("118", vm.DataInputs[0].CurrentValue);
        Assert.Equal("179", vm.DataInputs[1].CurrentValue);
    }

    [Fact]
    public void Clear_ResetsAllStateToInitialDefaults()
    {
        var vm = new ParameterPreviewViewModel();
        var manifest = new SessionManifest();
        var calcResult = new CalculatorSessionResult
        {
            DataInputs = new List<DataInputItem>
            {
                new() { ParameterName = "IH", DisplayedValue = "118" }
            }
        };
        var invResult = new AssemblyInventoryResult
        {
            AvailableRules = new List<string> { "Rule1" }
        };
        var compResult = new ParameterComparisonResult
        {
            Rows = new List<ParameterComparisonRow>
            {
                new() { ExcelParameterName = "IH", Status = ComparisonStatus.Match }
            }
        };

        vm.LoadSessionData(manifest, calcResult, invResult, compResult, null!);
        vm.SelectedTabIndex = 2;
        Assert.Single(vm.DataInputs);
        Assert.Equal(2, vm.AvailableRules.Count);
        Assert.Single(vm.PlannedChanges);
        Assert.Equal(2, vm.SelectedTabIndex);

        vm.Clear();

        Assert.Empty(vm.DataInputs);
        Assert.Empty(vm.AvailableRules);
        Assert.Empty(vm.PlannedChanges);
        Assert.Empty(vm.ChangedParameters);
        Assert.Empty(vm.ChangedSuppressions);
        Assert.False(vm.HasAppliedResults);
        Assert.Null(vm.SelectedRule);
        Assert.Null(vm.DeltaResult);
        Assert.Equal(0, vm.SelectedTabIndex);
    }
}
