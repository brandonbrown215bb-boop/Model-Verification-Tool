using InventorValidator.Geometry.Models;
using InventorValidator.Inventor;
using Xunit;

namespace InventorValidator.Tests;

public class InventorVisualOverlayServiceTests
{
    [Fact]
    public void RenderOverlays_WhenGivenNullArguments_ReturnsFalseWithoutThrowing()
    {
        var service = new InventorVisualOverlayService();

        bool r1 = service.RenderOverlays(null, null, null!);
        bool r2 = service.RenderOverlays(new object(), null, null!);
        bool r3 = service.RenderOverlays(null, new object(), new GeometryValidationResult());

        Assert.False(r1);
        Assert.False(r2);
        Assert.False(r3);
    }

    [Fact]
    public void ZoomAndHighlightHole_WhenGivenNullArguments_ReturnsFalseWithoutThrowing()
    {
        var service = new InventorVisualOverlayService();

        bool r1 = service.ZoomAndHighlightHole(null, null, null!);
        bool r2 = service.ZoomAndHighlightHole(new object(), null, null!);
        bool r3 = service.ZoomAndHighlightHole(null, new object(), new HoleMatchResult());

        Assert.False(r1);
        Assert.False(r2);
        Assert.False(r3);
    }

    [Fact]
    public void ClearOverlays_WhenGivenNullDoc_ExecutesSafelyWithoutThrowing()
    {
        var service = new InventorVisualOverlayService();
        service.ClearOverlays(null);
    }

    [Fact]
    public void HighlightOccurrence_WhenGivenNullDocOrEmptyName_ExecutesSafelyWithoutThrowing()
    {
        var service = new InventorVisualOverlayService();
        service.HighlightOccurrence(null, "SomeOcc");
        service.HighlightOccurrence(new object(), null);
        service.HighlightOccurrence(new object(), "—");
    }

    [Fact]
    public void RenderOverlays_WhenResultHasOnlyExtraCadHoles_ExcludesAllAndReturnsTrueSafely()
    {
        var service = new InventorVisualOverlayService();
        var validation = new GeometryValidationResult();
        validation.Results.Add(new HoleMatchResult
        {
            Expected = null,
            Actual = new ActualHole { Id = 1, Position = new Point3D(50, 50, 10), Diameter = 0.5 },
            Status = HoleMatchStatus.ExtraActual
        });

        // Dummy objects (non-null) - since all items are Extra CAD, it should filter all out and return true without calling COM properties
        bool result = service.RenderOverlays(new object(), new object(), validation);

        Assert.True(result);
    }

    [Fact]
    public void RenderOverlays_WhenHolesToRenderIsEmpty_ReturnsTrueSafely()
    {
        var service = new InventorVisualOverlayService();
        var validation = new GeometryValidationResult();
        validation.Results.Add(new HoleMatchResult
        {
            Expected = new ExpectedHole { ChannelGroup = "Floor Channels", ChannelName = "FC1" },
            Actual = new ActualHole { Id = 1, Position = new Point3D(10, 0, 0.75), Diameter = 0.25 },
            Status = HoleMatchStatus.Match
        });

        // Pass empty holesToRender collection
        bool result = service.RenderOverlays(new object(), new object(), validation, holesToRender: new List<HoleMatchResult>());

        Assert.True(result);
    }
}
