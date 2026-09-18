using System.IO;
using InventorValidator.Excel;
using InventorValidator.Excel.Parsers;
using InventorValidator.Inventor;
using InventorValidator.Inventor.Models;
using InventorValidator.Session;
using Xunit;

namespace InventorValidator.Tests;

public class FailClosedReliabilityTests
{
    [Fact]
    public void DataTabParser_WhenErrorCheckMissingAndInputsClean_ShouldReturnNaAndPass()
    {
        var array = new object[3, 3];
        array[1, 1] = "Input parameter";
        array[1, 2] = "Current value";

        array[2, 1] = "IH";
        array[2, 2] = 118.0;

        var (inputs, errVal, errRaw, isPassed) = DataTabParser.Parse(array);

        Assert.Single(inputs);
        Assert.Null(errVal);
        Assert.Equal("N/A", errRaw);
        Assert.True(isPassed, "Clean inputs without Error_Check summary should pass.");
    }

    [Fact]
    public void DataTabParser_WhenErrorCheckMissingAndInputHasFormulaError_ShouldFail()
    {
        var array = new object[3, 3];
        array[1, 1] = "Input parameter";
        array[1, 2] = "Current value";

        array[2, 1] = "IH";
        array[2, 2] = -2146826288; // Excel #REF! error code

        var (inputs, errVal, errRaw, isPassed) = DataTabParser.Parse(array);

        Assert.Single(inputs);
        Assert.True(inputs[0].HasError);
        Assert.False(isPassed, "Missing Error_Check with formula error on inputs must fail calculation check.");
    }

    [Fact]
    public void DataTabParser_WhenErrorCheckPresentAndNonZero_ShouldFail()
    {
        var array = new object[3, 3];
        array[1, 1] = "Input parameter";
        array[1, 2] = "Current value";

        array[2, 1] = "Error_Check";
        array[2, 2] = 1.0;

        var (_, errVal, errRaw, isPassed) = DataTabParser.Parse(array);

        Assert.Equal(1.0, errVal);
        Assert.Equal("1", errRaw);
        Assert.False(isPassed);
    }

    [Fact]
    public void DataTabParser_WhenErrorCheckPresentAndZero_ShouldPass()
    {
        var array = new object[3, 3];
        array[1, 1] = "Input parameter";
        array[1, 2] = "Current value";

        array[2, 1] = "Error_Check";
        array[2, 2] = 0.0;

        var (_, errVal, errRaw, isPassed) = DataTabParser.Parse(array);

        Assert.Equal(0.0, errVal);
        Assert.Equal("0", errRaw);
        Assert.True(isPassed);
    }

    [Fact]
    public void SuppressionMatching_ExactOccurrenceName_ShouldYieldVerifiedMatch()
    {
        var service = new ParameterMatchingService();
        var sheet1Items = new List<Sheet1ParameterItem>
        {
            new Sheet1ParameterItem
            {
                ParameterName = "Part_091_30102_466_1",
                DisplayedValue = "1",
                Category = ParameterCategory.SuppressionControl
            }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Occurrences = new List<OccurrenceInventoryItem>
            {
                new OccurrenceInventoryItem
                {
                    OccurrenceName = "091-30102-466:1",
                    PartNumber = "091-30102-466",
                    IsSuppressed = false // Active (1)
                }
            }
        };

        var result = service.CompareParameters(sheet1Items, inventory);

        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(MatchClassification.SuppressionMatch, row.MatchType);
        Assert.Equal(ComparisonStatus.Match, row.Status);
        Assert.Equal("091-30102-466:1", row.TargetOccurrenceName);
        Assert.True(row.ModelSuppressionState);
        Assert.Equal(1, result.MatchedCount);
    }

    [Fact]
    public void SuppressionMatching_HeuristicPartNumberFallback_ShouldBeDemotedToRequiresILogic()
    {
        var service = new ParameterMatchingService();
        var sheet1Items = new List<Sheet1ParameterItem>
        {
            // Notice no suffix :1 or suffix mismatch
            new Sheet1ParameterItem
            {
                ParameterName = "Part_091_30102_458",
                DisplayedValue = "1",
                Category = ParameterCategory.SuppressionControl
            }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Occurrences = new List<OccurrenceInventoryItem>
            {
                new OccurrenceInventoryItem
                {
                    OccurrenceName = "091-30102-458:99",
                    PartNumber = "091-30102-458",
                    IsSuppressed = false
                }
            }
        };

        var result = service.CompareParameters(sheet1Items, inventory);

        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(MatchClassification.RequiresILogicVerification, row.MatchType);
        Assert.Equal(ComparisonStatus.RequiresILogicVerification, row.Status);
        Assert.Contains("Not verified—requires iLogic", row.Notes);
        Assert.Equal(0, result.MatchedCount);
        Assert.Equal(1, result.RequiresILogicCount);
    }

    [Fact]
    public void SuppressionMatching_MissingOccurrence_ShouldBeMissingInModel()
    {
        var service = new ParameterMatchingService();
        var sheet1Items = new List<Sheet1ParameterItem>
        {
            new Sheet1ParameterItem
            {
                ParameterName = "Part_091_30102_999_1",
                DisplayedValue = "1",
                Category = ParameterCategory.SuppressionControl
            }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Occurrences = new List<OccurrenceInventoryItem>()
        };

        var result = service.CompareParameters(sheet1Items, inventory);

        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(ComparisonStatus.MissingInModel, row.Status);
        Assert.Equal(1, result.MissingInModelCount);
    }

    [Fact]
    public void WorkspaceManager_CleanupWorkspace_ShouldRemoveReadOnlyAndLockedFiles()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "iv_cleanup_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempRoot);
        string subDir = Path.Combine(tempRoot, "SubFolder");
        Directory.CreateDirectory(subDir);

        string roFile = Path.Combine(subDir, "readonly.txt");
        File.WriteAllText(roFile, "test data");
        File.SetAttributes(roFile, FileAttributes.ReadOnly);

        var manifest = new SessionManifest
        {
            WorkspaceDirectory = tempRoot,
            Status = SessionStatus.Ready
        };

        var manager = new WorkspaceManager();
        manager.CleanupWorkspace(manifest);

        Assert.False(Directory.Exists(tempRoot), "Temporary workspace folder should be deleted completely.");
        Assert.Equal(SessionStatus.CleanedUp, manifest.Status);
    }

    public class MockSoftwareVersion
    {
        public int Major { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    public class MockInventorApp
    {
        public MockSoftwareVersion SoftwareVersion { get; set; } = new();
    }

    [Fact]
    public void ValidateInventorVersion_WhenVersionMatches_ShouldNotThrow()
    {
        var app = new MockInventorApp
        {
            SoftwareVersion = new MockSoftwareVersion { Major = 24, DisplayName = "Autodesk Inventor Professional 2020" }
        };

        var ex = Record.Exception(() => InventorProcessLauncher.ValidateInventorVersion(app, 2020));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateInventorVersion_WhenVersionMismatches_ShouldThrowInvalidOperationException()
    {
        var app = new MockInventorApp
        {
            SoftwareVersion = new MockSoftwareVersion { Major = 28, DisplayName = "Autodesk Inventor Professional 2024" }
        };

        Assert.Throws<InvalidOperationException>(() => InventorProcessLauncher.ValidateInventorVersion(app, 2020));
    }
}
