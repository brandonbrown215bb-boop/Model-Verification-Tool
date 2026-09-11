using InventorValidator.Excel;
using InventorValidator.Inventor;
using InventorValidator.Inventor.Models;
using Xunit;

namespace InventorValidator.Tests;

public class ParameterMatchingServiceTests
{
    private readonly ParameterMatchingService _service = new();

    [Fact]
    public void ExactCaseSensitiveMatch_ClassifiesAsUniqueExactMatch_AndSafeToWrite()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "IH", DisplayedValue = "118.000", Unit = "in", Category = ParameterCategory.Dimension }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Parameters = new List<InventorParameterItem>
            {
                new()
                {
                    Name = "IH",
                    Expression = "118.000 in",
                    Value = 299.72,
                    Units = "in",
                    ParameterType = "Model",
                    IsFormulaDriven = false,
                    IsReference = false
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(MatchClassification.UniqueExactMatch, row.MatchType);
        Assert.Equal(SafeWriteClassification.SafeToWrite, row.WritePolicy);
        Assert.Equal(ComparisonStatus.Match, row.Status);
        Assert.Equal("IH", row.TargetParameterName);
    }

    [Fact]
    public void ExactCaseInsensitiveMatch_ClassifiesAsUniqueExactMatch()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "ih", DisplayedValue = "118.000", Unit = "in", Category = ParameterCategory.Dimension }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Parameters = new List<InventorParameterItem>
            {
                new()
                {
                    Name = "IH",
                    Expression = "118.000 in",
                    Value = 299.72,
                    Units = "in",
                    ParameterType = "Model",
                    IsFormulaDriven = false
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(MatchClassification.UniqueExactMatch, row.MatchType);
        Assert.Equal(SafeWriteClassification.SafeToWrite, row.WritePolicy);
        Assert.Equal(ComparisonStatus.Match, row.Status);
    }

    [Fact]
    public void TrimmedAndNormalizedMatch_RequiresApproval()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "Support_Loc_4", DisplayedValue = "41.413", Unit = "in", Category = ParameterCategory.Dimension }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Parameters = new List<InventorParameterItem>
            {
                new()
                {
                    Name = "Support-Loc-4",
                    Expression = "41.413 in",
                    Value = 105.189,
                    Units = "in",
                    ParameterType = "Model",
                    IsFormulaDriven = false
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(MatchClassification.UniqueNormalizedMatch, row.MatchType);
        Assert.Equal(SafeWriteClassification.NormalizedRequiresApproval, row.WritePolicy);
        Assert.Equal(ComparisonStatus.Match, row.Status);
    }

    [Fact]
    public void SuppressionParam_MatchingActiveOccurrence_ClassifiesAsSuppressionMatch()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "Part_091_30102_466_1", DisplayedValue = "1", Unit = "ul", Category = ParameterCategory.SuppressionControl }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Occurrences = new List<OccurrenceInventoryItem>
            {
                new()
                {
                    OccurrenceName = "091-30102-466:1",
                    PartNumber = "091-30102-466",
                    IsSuppressed = false
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(MatchClassification.SuppressionMatch, row.MatchType);
        Assert.Equal(SafeWriteClassification.SafeToWrite, row.WritePolicy);
        Assert.Equal(ComparisonStatus.Match, row.Status);
        Assert.True(row.ModelSuppressionState);
    }

    [Fact]
    public void SuppressionParam_DiscrepancyWhenExpectedActiveButSuppressedInModel()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "Part_091_30102_466_1", DisplayedValue = "1", Unit = "ul", Category = ParameterCategory.SuppressionControl }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Occurrences = new List<OccurrenceInventoryItem>
            {
                new()
                {
                    OccurrenceName = "091-30102-466:1",
                    PartNumber = "091-30102-466",
                    IsSuppressed = true // Suppressed in model!
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(MatchClassification.SuppressionMatch, row.MatchType);
        Assert.Equal(ComparisonStatus.Discrepancy, row.Status);
        Assert.False(row.ModelSuppressionState);
    }

    [Fact]
    public void FormulaDrivenParameter_IsProtectedAsReadOnlyFormula()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "d69", DisplayedValue = "41.413", Unit = "in", Category = ParameterCategory.Dimension }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Parameters = new List<InventorParameterItem>
            {
                new()
                {
                    Name = "d69",
                    Expression = "Support_Loc4 + 0.5 in",
                    Value = 105.189,
                    Units = "in",
                    ParameterType = "Model",
                    IsFormulaDriven = true // driven by equation
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(SafeWriteClassification.ReadOnlyFormula, row.WritePolicy);
        Assert.Contains("Parametric formula protected", row.Notes);
    }

    [Fact]
    public void ReferenceParameter_IsProtectedAsReadOnlyReference()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "Ref_Width", DisplayedValue = "179.000", Unit = "in", Category = ParameterCategory.Dimension }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Parameters = new List<InventorParameterItem>
            {
                new()
                {
                    Name = "Ref_Width",
                    Expression = "179.000 in",
                    Value = 454.66,
                    Units = "in",
                    ParameterType = "Reference",
                    IsReference = true
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(SafeWriteClassification.ReadOnlyReference, row.WritePolicy);
        Assert.Contains("read-only", row.Notes);
    }

    [Fact]
    public void DimensionalDiscrepancy_DetectsDeltaCorrectly()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "IW", DisplayedValue = "179.000", Unit = "in", Category = ParameterCategory.Dimension }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Parameters = new List<InventorParameterItem>
            {
                new()
                {
                    Name = "IW",
                    Expression = "175.000 in", // Model has 175.0, Excel has 179.0
                    Value = 444.5,
                    Units = "in",
                    ParameterType = "Model",
                    IsFormulaDriven = false
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(ComparisonStatus.Discrepancy, row.Status);
        Assert.NotNull(row.Delta);
        Assert.Equal(4.0, row.Delta.Value, precision: 3);
        Assert.Equal(1, result.DiscrepancyCount);
    }

    [Fact]
    public void InformationalParameter_MissingInModel_IsClassifiedAsNotApplicable()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "TotalWeight_Calc", DisplayedValue = "1250", Unit = "lbs", Category = ParameterCategory.Informational }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Parameters = new List<InventorParameterItem>()
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(ComparisonStatus.NotApplicable, row.Status);
        Assert.Equal(MatchClassification.NoMatch, row.MatchType);
    }

    [Fact]
    public void ExpressionDrivenMatch_WhenModelParameterExpressionMatchesExcelName_MatchesAndValidatesValue()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "IW", DisplayedValue = "179.000", Unit = "in", Category = ParameterCategory.Dimension }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Parameters = new List<InventorParameterItem>
            {
                new()
                {
                    Name = "d14",
                    Expression = "IW",
                    Value = 454.66, // 179.0 in * 2.54 cm/in = 454.66 cm
                    Units = "in",
                    ParameterType = "Model",
                    IsFormulaDriven = true
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(MatchClassification.ExpressionDrivenMatch, row.MatchType);
        Assert.Equal(ComparisonStatus.Match, row.Status);
        Assert.Equal(SafeWriteClassification.ReadOnlyFormula, row.WritePolicy);
        Assert.Equal("d14", row.TargetParameterName);
        Assert.NotNull(row.ModelNumericValue);
        Assert.Equal(179.0, row.ModelNumericValue.Value, precision: 3);
        Assert.Contains("IW", row.Notes);
    }

    [Fact]
    public void ExpressionDrivenMatch_WithDiscrepancy_DetectsDiscrepancyAndDelta()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "IW", DisplayedValue = "179.000", Unit = "in", Category = ParameterCategory.Dimension }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Parameters = new List<InventorParameterItem>
            {
                new()
                {
                    Name = "d14",
                    Expression = "IW",
                    Value = 444.5, // 175.0 in * 2.54 cm/in = 444.5 cm
                    Units = "in",
                    ParameterType = "Model",
                    IsFormulaDriven = true
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(MatchClassification.ExpressionDrivenMatch, row.MatchType);
        Assert.Equal(ComparisonStatus.Discrepancy, row.Status);
        Assert.NotNull(row.Delta);
        Assert.Equal(4.0, row.Delta.Value, precision: 3);
    }

    [Fact]
    public void SuppressionParam_WithoutSuffix_MatchesFirstInstanceOccurrence()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "Part_091_30102_458", DisplayedValue = "1", Unit = "ul", Category = ParameterCategory.SuppressionControl }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam",
            Occurrences = new List<OccurrenceInventoryItem>
            {
                new()
                {
                    OccurrenceName = "091-30102-458:1",
                    PartNumber = "091-30102-458",
                    IsSuppressed = false
                }
            }
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(MatchClassification.SuppressionMatch, row.MatchType);
        Assert.Equal(ComparisonStatus.Match, row.Status);
        Assert.Equal("091-30102-458:1", row.TargetOccurrenceName);
        Assert.True(row.ModelSuppressionState);
    }

    [Fact]
    public void FeatureControl_MissingInModel_IsClassifiedAsNotApplicableWithFeatureNotes()
    {
        // Arrange
        var sheet1Params = new List<Sheet1ParameterItem>
        {
            new() { ParameterName = "L1BHD_Angle_Hole_Mid_Suppression", DisplayedValue = "1", Unit = "ul", Category = ParameterCategory.FeatureControl }
        };

        var inventory = new AssemblyInventoryResult
        {
            AssemblyName = "TestAssembly.iam"
        };

        // Act
        var result = _service.CompareParameters(sheet1Params, inventory);

        // Assert
        Assert.Single(result.Rows);
        var row = result.Rows[0];
        Assert.Equal(ComparisonStatus.NotApplicable, row.Status);
        Assert.Contains("Feature suppression control", row.Notes);
    }
}

