using InventorValidator.Inventor.Models;
using Xunit;

namespace InventorValidator.Tests;

public class ApplyModeDeltaTests
{
    [Fact]
    public void ParameterDelta_WhenValuesDiffer_ReportsHasChangedTrueAndCorrectDelta()
    {
        var delta = new ParameterDelta
        {
            ParameterName = "IH",
            DocumentName = "391-10006-023.iam",
            BeforeValue = 110.0,
            AfterValue = 118.0,
            BeforeExpression = "110 in",
            AfterExpression = "118 in",
            Units = "in"
        };

        Assert.True(delta.HasChanged);
        Assert.Equal(8.0, delta.Delta, precision: 4);
    }

    [Fact]
    public void ParameterDelta_WhenValuesSame_ReportsHasChangedFalse()
    {
        var delta = new ParameterDelta
        {
            ParameterName = "IW",
            DocumentName = "391-10006-023.iam",
            BeforeValue = 179.0,
            AfterValue = 179.0,
            BeforeExpression = "179 in",
            AfterExpression = "179 in",
            Units = "in"
        };

        Assert.False(delta.HasChanged);
        Assert.Equal(0.0, delta.Delta, precision: 4);
    }

    [Theory]
    [InlineData(true, false, "Unsuppressed (Turned ON)", true)]
    [InlineData(false, true, "Suppressed (Turned OFF)", true)]
    [InlineData(false, false, "Remained Active", false)]
    [InlineData(true, true, "Remained Suppressed", false)]
    public void SuppressionDelta_ReportsCorrectTransitions(bool before, bool after, string expectedText, bool hasChanged)
    {
        var delta = new SuppressionDelta
        {
            OccurrenceName = "091-30102-459:1",
            PartNumber = "091-30102-459",
            BeforeSuppressed = before,
            AfterSuppressed = after
        };

        Assert.Equal(hasChanged, delta.HasStateChanged);
        Assert.Equal(expectedText, delta.TransitionText);
    }

    [Fact]
    public void ApplyModeDeltaResult_AggregatesCountsCorrectly()
    {
        var result = new ApplyModeDeltaResult
        {
            Success = true,
            ExecutedRuleName = "Name1",
            RuleExecutedSuccessfully = true,
            ParameterDeltas = new List<ParameterDelta>
            {
                new() { ParameterName = "IH", BeforeValue = 110, AfterValue = 118 },
                new() { ParameterName = "IW", BeforeValue = 179, AfterValue = 179 },
                new() { ParameterName = "d8", BeforeValue = 4.25, AfterValue = 5.0 }
            },
            SuppressionDeltas = new List<SuppressionDelta>
            {
                new() { OccurrenceName = "Part1:1", BeforeSuppressed = true, AfterSuppressed = false },
                new() { OccurrenceName = "Part2:1", BeforeSuppressed = false, AfterSuppressed = true },
                new() { OccurrenceName = "Part3:1", BeforeSuppressed = false, AfterSuppressed = false }
            }
        };

        Assert.Equal(2, result.ChangedParametersCount);
        Assert.Equal(2, result.ChangedSuppressionsCount);
        Assert.Equal(1, result.UnsuppressedCount);
        Assert.Equal(1, result.SuppressedCount);
    }
}
