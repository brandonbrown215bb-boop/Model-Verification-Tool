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
}
