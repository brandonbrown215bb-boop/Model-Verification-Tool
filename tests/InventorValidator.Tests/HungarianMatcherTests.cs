using System;
using InventorValidator.Geometry;
using Xunit;

namespace InventorValidator.Tests;

public class HungarianMatcherTests
{
    [Fact]
    public void FindAssignments_SquareMatrix_FindsMinimumCostPermutation()
    {
        // 3x3 cost matrix
        // [ 10, 19,  8 ]
        // [ 15, 18, 12 ]
        // [ 14, 15, 10 ]
        // Optimal:
        // Row 0 -> Col 2 (cost 8)
        // Row 1 -> Col 0 (cost 15)
        // Row 2 -> Col 1 (cost 15)
        // Total cost: 38
        double[,] cost = new double[,]
        {
            { 10, 19, 8 },
            { 15, 18, 12 },
            { 14, 15, 10 }
        };

        int[] assignments = HungarianMatcher.FindAssignments(cost);

        Assert.Equal(3, assignments.Length);
        Assert.Equal(0, assignments[0]);
        Assert.Equal(2, assignments[1]);
        Assert.Equal(1, assignments[2]);
    }

    [Fact]
    public void FindAssignments_MoreCandidatesThanExpected_AssignsBestSubset()
    {
        // 2 expected rows, 4 candidates
        // Row 0 closest to col 1 (cost 0.1)
        // Row 1 closest to col 3 (cost 0.2)
        double[,] cost = new double[,]
        {
            { 5.0, 0.1, 8.0, 3.0 },
            { 4.0, 6.0, 7.0, 0.2 }
        };

        int[] assignments = HungarianMatcher.FindAssignments(cost);

        Assert.Equal(2, assignments.Length);
        Assert.Equal(1, assignments[0]);
        Assert.Equal(3, assignments[1]);
    }

    [Fact]
    public void FindAssignments_EmptyMatrix_ReturnsEmpty()
    {
        double[,] cost = new double[0, 0];
        int[] assignments = HungarianMatcher.FindAssignments(cost);
        Assert.Empty(assignments);
    }
}
