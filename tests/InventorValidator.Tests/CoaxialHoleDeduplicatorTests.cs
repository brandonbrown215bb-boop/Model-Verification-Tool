using System.Collections.Generic;
using InventorValidator.Geometry;
using InventorValidator.Geometry.Models;
using Xunit;

namespace InventorValidator.Tests;

public class CoaxialHoleDeduplicatorTests
{
    [Fact]
    public void Deduplicate_TwoCoaxialFacesOnPlate_MergesIntoSingleHole()
    {
        // Plate thickness 0.105 in. Hole axis along Z. Entry face at Z=0.0, Exit face at Z=0.105
        var face1 = new ActualHole
        {
            Id = 1,
            Position = new Point3D(10.0, 20.0, 0.0),
            Axis = new Vector3D(0, 0, 1),
            Diameter = 0.218,
            ParentPartNumber = "091-30102-461",
            ParentOccurrenceName = "091-30102-461:1"
        };

        var face2 = new ActualHole
        {
            Id = 2,
            Position = new Point3D(10.0001, 19.9999, 0.105),
            Axis = new Vector3D(0, 0, 1),
            Diameter = 0.218,
            ParentPartNumber = "091-30102-461",
            ParentOccurrenceName = "091-30102-461:1"
        };

        var merged = CoaxialHoleDeduplicator.Deduplicate(new[] { face1, face2 });

        Assert.Single(merged);
        var hole = merged[0];
        Assert.Equal(2, hole.CoaxialFaceCount);
        Assert.Equal(10.0, hole.Position.X, precision: 3);
        Assert.Equal(20.0, hole.Position.Y, precision: 3);
        Assert.Equal(0.0525, hole.Position.Z, precision: 3); // midpoint
        Assert.Equal(0.218, hole.Diameter, precision: 4);
    }

    [Fact]
    public void Deduplicate_DistinctHoles_RetainsBoth()
    {
        var hole1 = new ActualHole
        {
            Id = 1,
            Position = new Point3D(2.31, 0, 0.75),
            Axis = new Vector3D(0, 0, 1),
            Diameter = 0.218
        };

        var hole2 = new ActualHole
        {
            Id = 2,
            Position = new Point3D(5.00, 0, 0.75),
            Axis = new Vector3D(0, 0, 1),
            Diameter = 0.218
        };

        var merged = CoaxialHoleDeduplicator.Deduplicate(new[] { hole1, hole2 });

        Assert.Equal(2, merged.Count);
    }
}
