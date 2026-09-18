using System;

namespace InventorValidator.Geometry;

/// <summary>
/// Implementation of the Kuhn-Munkres (Hungarian) algorithm for globally optimal minimum-cost
/// bipartite matching between expected holes (rows) and actual holes (columns).
/// Complexity: O(N^3).
/// </summary>
public static class HungarianMatcher
{
    /// <summary>
    /// Finds the minimum-cost 1-to-1 assignment for an N x M cost matrix.
    /// </summary>
    /// <param name="costMatrix">Rectangular cost matrix where costMatrix[i, j] is the cost of assigning row i to col j.</param>
    /// <returns>An array of length N where result[i] is the assigned column index j, or -1 if unassigned.</returns>
    public static int[] FindAssignments(double[,] costMatrix)
    {
        int rows = costMatrix.GetLength(0);
        int cols = costMatrix.GetLength(1);

        if (rows == 0 || cols == 0)
        {
            var empty = new int[rows];
            Array.Fill(empty, -1);
            return empty;
        }

        // We pad to a square matrix of size dim x dim
        int dim = Math.Max(rows, cols);
        double[,] matrix = new double[dim, dim];

        // Find max finite value in costMatrix to use as padding for non-existent edges
        double maxVal = 0.0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (!double.IsInfinity(costMatrix[r, c]) && !double.IsNaN(costMatrix[r, c]))
                {
                    maxVal = Math.Max(maxVal, costMatrix[r, c]);
                }
            }
        }
        double padCost = maxVal > 0 ? maxVal * 1000.0 : 100000.0;

        for (int r = 0; r < dim; r++)
        {
            for (int c = 0; c < dim; c++)
            {
                if (r < rows && c < cols)
                {
                    double val = costMatrix[r, c];
                    matrix[r, c] = double.IsInfinity(val) || double.IsNaN(val) ? padCost : val;
                }
                else
                {
                    matrix[r, c] = padCost;
                }
            }
        }

        int[] match = SolveSquare(matrix, dim);

        // Filter assignments back to original rows and cols
        int[] result = new int[rows];
        for (int r = 0; r < rows; r++)
        {
            int assignedCol = match[r];
            if (assignedCol >= 0 && assignedCol < cols && matrix[r, assignedCol] < padCost * 0.9)
            {
                result[r] = assignedCol;
            }
            else
            {
                result[r] = -1;
            }
        }

        return result;
    }

    /// <summary>
    /// Solves the square minimum-cost assignment problem using 1-indexed potential reduction.
    /// Returns 0-indexed array where match[row] = assigned col.
    /// </summary>
    private static int[] SolveSquare(double[,] cost, int n)
    {
        // 1-based indexing for standard textbook Hungarian implementation
        double[] u = new double[n + 1];
        double[] v = new double[n + 1];
        int[] p = new int[n + 1];
        int[] way = new int[n + 1];

        for (int i = 1; i <= n; i++)
        {
            p[0] = i;
            int j0 = 0;
            double[] minv = new double[n + 1];
            bool[] used = new bool[n + 1];
            Array.Fill(minv, double.PositiveInfinity);

            do
            {
                used[j0] = true;
                int i0 = p[j0];
                double delta = double.PositiveInfinity;
                int j1 = 0;

                for (int j = 1; j <= n; j++)
                {
                    if (!used[j])
                    {
                        double cur = cost[i0 - 1, j - 1] - u[i0] - v[j];
                        if (cur < minv[j])
                        {
                            minv[j] = cur;
                            way[j] = j0;
                        }
                        if (minv[j] < delta)
                        {
                            delta = minv[j];
                            j1 = j;
                        }
                    }
                }

                for (int j = 0; j <= n; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minv[j] -= delta;
                    }
                }

                j0 = j1;
            } while (p[j0] != 0);

            do
            {
                int j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        int[] result = new int[n];
        for (int j = 1; j <= n; j++)
        {
            if (p[j] != 0)
            {
                result[p[j] - 1] = j - 1;
            }
        }

        return result;
    }
}
