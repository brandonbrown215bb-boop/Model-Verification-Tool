using System;
using System.Collections.Generic;
using System.Linq;
using InventorValidator.Geometry.Models;

namespace InventorValidator.Geometry;

/// <summary>
/// Merges multiple coaxial cylindrical B-Rep faces representing the entry and exit sides
/// of a single physical through-hole in CAD.
/// </summary>
public static class CoaxialHoleDeduplicator
{
    /// <summary>
    /// Deduplicates a list of raw hole/cylinder instances into single physical holes.
    /// </summary>
    /// <param name="rawHoles">Collection of raw extracted holes/cylinder faces.</param>
    /// <param name="diameterTolerance">Maximum diameter difference to consider identical (default 0.005 in).</param>
    /// <param name="collinearAngleDegrees">Maximum angular deviation between hole axes (default 1.5 degrees).</param>
    /// <param name="maxRadialOffset">Maximum perpendicular distance from point to axis (default 0.010 in).</param>
    /// <param name="maxAxialSeparation">Maximum distance along axis between faces of the same hole (default 1.5 in).</param>
    public static List<ActualHole> Deduplicate(
        IEnumerable<ActualHole> rawHoles,
        double diameterTolerance = 0.005,
        double collinearAngleDegrees = 1.5,
        double maxRadialOffset = 0.010,
        double maxAxialSeparation = 1.5)
    {
        var inputList = rawHoles.ToList();
        var visited = new bool[inputList.Count];
        var mergedHoles = new List<ActualHole>();
        int nextId = 1;

        for (int i = 0; i < inputList.Count; i++)
        {
            if (visited[i]) continue;

            var cluster = new List<ActualHole> { inputList[i] };
            visited[i] = true;

            for (int j = i + 1; j < inputList.Count; j++)
            {
                if (visited[j]) continue;

                if (IsCoaxial(inputList[i], inputList[j], diameterTolerance, collinearAngleDegrees, maxRadialOffset, maxAxialSeparation))
                {
                    cluster.Add(inputList[j]);
                    visited[j] = true;
                }
            }

            // Create merged ActualHole from cluster
            mergedHoles.Add(MergeCluster(cluster, nextId++));
        }

        return mergedHoles;
    }

    private static bool IsCoaxial(
        ActualHole a,
        ActualHole b,
        double diameterTol,
        double angularTol,
        double maxRadialOffset,
        double maxAxialSep)
    {
        // 1. Diameter check
        if (Math.Abs(a.Diameter - b.Diameter) > diameterTol)
        {
            return false;
        }

        // 2. Collinear axis check
        if (!a.Axis.IsCollinear(b.Axis, angularTol))
        {
            return false;
        }

        // 3. Distance vector from a to b
        Vector3D axis = a.Axis.Normalize();
        double dx = b.Position.X - a.Position.X;
        double dy = b.Position.Y - a.Position.Y;
        double dz = b.Position.Z - a.Position.Z;

        // Projection along the axis
        double axialDist = Math.Abs(dx * axis.X + dy * axis.Y + dz * axis.Z);
        if (axialDist > maxAxialSep)
        {
            return false;
        }

        // Perpendicular (radial) distance from axis
        double totalDistSq = dx * dx + dy * dy + dz * dz;
        double radialDistSq = Math.Max(0, totalDistSq - (axialDist * axialDist));
        double radialDist = Math.Sqrt(radialDistSq);

        return radialDist <= maxRadialOffset;
    }

    private static ActualHole MergeCluster(List<ActualHole> cluster, int id)
    {
        if (cluster.Count == 1)
        {
            var single = cluster[0];
            single.Id = id;
            if (single.RawFaceCenters.Count == 0)
            {
                single.RawFaceCenters.Add(single.Position);
            }
            return single;
        }

        // Compute average center and average diameter
        double avgX = cluster.Average(h => h.Position.X);
        double avgY = cluster.Average(h => h.Position.Y);
        double avgZ = cluster.Average(h => h.Position.Z);
        double avgDia = cluster.Average(h => h.Diameter);

        var allCenters = new List<Point3D>();
        foreach (var h in cluster)
        {
            if (h.RawFaceCenters.Count > 0)
            {
                allCenters.AddRange(h.RawFaceCenters);
            }
            else
            {
                allCenters.Add(h.Position);
            }
        }

        var representative = cluster[0];
        return new ActualHole
        {
            Id = id,
            Position = new Point3D(avgX, avgY, avgZ),
            Axis = representative.Axis,
            Diameter = Math.Round(avgDia, 4),
            ParentOccurrenceName = representative.ParentOccurrenceName,
            ParentPartNumber = representative.ParentPartNumber,
            FeatureName = representative.FeatureName,
            CoaxialFaceCount = cluster.Count,
            RawFaceCenters = allCenters
        };
    }
}
