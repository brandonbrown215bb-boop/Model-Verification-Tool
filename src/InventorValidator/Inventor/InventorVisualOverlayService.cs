using System;
using System.Collections.Generic;
using System.Linq;
using InventorValidator.Geometry.Models;
using InventorValidator.Infrastructure;

namespace InventorValidator.Inventor;

/// <summary>
/// Manages transient 3D ClientGraphics overlays, camera navigation, and occurrence highlighting
/// in Autodesk Inventor without dirtying the assembly document.
/// </summary>
public class InventorVisualOverlayService
{
    private const double InchesToCm = 2.54;
    private const string ClientGraphicsClientId = "InventorValidator_Overlays";
    private const string GraphicsDataSetsClientId = "InventorValidator_DataSets";

    private dynamic? _activeHighlightSet;

    /// <summary>
    /// Renders 3D visual markers for validated channel holes directly inside Autodesk Inventor's graphics pipeline.
    /// </summary>
    public bool RenderOverlays(
        dynamic inventorApp,
        dynamic asmDoc,
        GeometryValidationResult result,
        IEnumerable<HoleMatchResult>? holesToRender = null,
        bool includeExtraHoles = false)
    {
        if (inventorApp == null || asmDoc == null || result == null)
            return false;

        try
        {
            // 1. Clear any existing graphics first
            ClearOverlays(asmDoc);

            var items = (holesToRender ?? result.Results)
                .Where(m =>
                {
                    // Never render unrelated "Extra CAD" assembly holes (coils, brackets, fan skids, etc.)
                    if (string.Equals(m.ChannelGroup, "Extra CAD", StringComparison.OrdinalIgnoreCase))
                        return false;

                    // Only render extra holes if explicitly requested AND associated with a real channel
                    if (m.Status == HoleMatchStatus.ExtraActual && !includeExtraHoles)
                        return false;

                    return true;
                })
                .ToList();

            if (items.Count == 0)
            {
                try { asmDoc.Dirty = false; } catch { }
                DiagnosticsLogger.Instance.Info("No channel hole markers to render with current filter.");
                return true;
            }

            dynamic compDef = asmDoc.ComponentDefinition;
            dynamic transGeom = inventorApp.TransientGeometry;

            dynamic clientGraphicsCol = compDef.ClientGraphicsCollection;
            dynamic dataSetsCol = asmDoc.GraphicsDataSetsCollection;

            // Create client graphics and dataset containers
            dynamic clientGraphics = clientGraphicsCol.Add(ClientGraphicsClientId);
            dynamic dataSets = dataSetsCol.Add(GraphicsDataSetsClientId);

            // Create coordinate sets and color sets
            int nextDataId = 1;
            int nextNodeId = 1;
            int renderedMarkerCount = 0;

            var groupNodes = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);

            // Colors:
            // Expected: Cyan (0, 200, 255)
            // Match: Green (46, 160, 67)
            // Warning: Amber (210, 153, 34)
            // Discrepancy: Red (248, 81, 73)
            // Missing: Dark Red (207, 34, 46)
            // Extra: Purple (163, 113, 247)

            foreach (var match in items)
            {
                string group = string.IsNullOrWhiteSpace(match.ChannelGroup) ? "General" : match.ChannelGroup;
                if (!groupNodes.TryGetValue(group, out dynamic? groupNode))
                {
                    groupNode = clientGraphics.AddNode(nextNodeId++);
                    try { groupNode.BurnThrough = true; } catch { }
                    groupNodes[group] = groupNode;
                }

                // 1. Expected hole marker (if expected coordinate exists)
                if (match.Expected != null)
                {
                    var expPos = match.Expected.ExpectedPosition;
                    double expX = expPos.X * InchesToCm;
                    double expY = expPos.Y * InchesToCm;
                    double expZ = expPos.Z * InchesToCm;

                    dynamic expPt = transGeom.CreatePoint(expX, expY, expZ);
                    dynamic expCoordSet = dataSets.CreateCoordinateSet(nextDataId++);
                    expCoordSet.Add(1, expPt);

                    dynamic expColorSet = dataSets.CreateColorSet(nextDataId++);
                    expColorSet.Add(1, 0, 200, 255); // Cyan

                    dynamic expPoints = groupNode.AddPointGraphics();
                    expPoints.CoordinateSet = expCoordSet;
                    try
                    {
                        expPoints.SetCustomRenderStyle(expColorSet);
                        expPoints.BurnThrough = true;
                    }
                    catch { }
                    renderedMarkerCount++;
                }

                // 2. Actual hole marker (if actual coordinate exists)
                if (match.Actual != null)
                {
                    var actPos = match.Actual.Position;
                    double actX = actPos.X * InchesToCm;
                    double actY = actPos.Y * InchesToCm;
                    double actZ = actPos.Z * InchesToCm;

                    dynamic actPt = transGeom.CreatePoint(actX, actY, actZ);
                    dynamic actCoordSet = dataSets.CreateCoordinateSet(nextDataId++);
                    actCoordSet.Add(1, actPt);

                    dynamic actColorSet = dataSets.CreateColorSet(nextDataId++);
                    byte r = 46, g = 160, b = 67; // Default Match Green
                    switch (match.Status)
                    {
                        case HoleMatchStatus.Warning:
                            r = 210; g = 153; b = 34; // Amber
                            break;
                        case HoleMatchStatus.Mislocated:
                            r = 248; g = 81; b = 73;  // Red
                            break;
                        case HoleMatchStatus.MissingExpected:
                            r = 207; g = 34; b = 46;  // Dark Red
                            break;
                        case HoleMatchStatus.ExtraActual:
                            r = 163; g = 113; b = 247; // Purple
                            break;
                    }
                    actColorSet.Add(1, (int)r, (int)g, (int)b);

                    dynamic actPoints = groupNode.AddPointGraphics();
                    actPoints.CoordinateSet = actCoordSet;
                    try
                    {
                        actPoints.SetCustomRenderStyle(actColorSet);
                        actPoints.BurnThrough = true;
                    }
                    catch { }
                    renderedMarkerCount++;

                    // 3. If there is a discrepancy between expected and actual, draw a connecting line
                    if (match.Expected != null && match.Status is HoleMatchStatus.Mislocated or HoleMatchStatus.Warning)
                    {
                        var expPos = match.Expected.ExpectedPosition;
                        dynamic lineCoordSet = dataSets.CreateCoordinateSet(nextDataId++);
                        lineCoordSet.Add(1, transGeom.CreatePoint(expPos.X * InchesToCm, expPos.Y * InchesToCm, expPos.Z * InchesToCm));
                        lineCoordSet.Add(2, transGeom.CreatePoint(actX, actY, actZ));

                        dynamic lineColorSet = dataSets.CreateColorSet(nextDataId++);
                        lineColorSet.Add(1, (int)r, (int)g, (int)b);

                        dynamic lineGraphics = groupNode.AddLineGraphics();
                        lineGraphics.CoordinateSet = lineCoordSet;
                        try
                        {
                            lineGraphics.ColorSet = lineColorSet;
                            lineGraphics.BurnThrough = true;
                        }
                        catch { }
                    }
                }
            }

            // Re-render view
            try
            {
                dynamic activeView = inventorApp.ActiveView;
                if (activeView != null)
                {
                    activeView.Update();
                }
                else if (asmDoc != null)
                {
                    dynamic views = asmDoc.Views;
                    if (views != null && views.Count > 0)
                    {
                        views[1].Update();
                    }
                }
            }
            catch { }

            // Guarantee that transient client graphics do not dirty the CAD document
            try { asmDoc.Dirty = false; } catch { }

            DiagnosticsLogger.Instance.Success($"Rendered {renderedMarkerCount} 3D overlay markers across {groupNodes.Count} channel groups in Inventor.");
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Failed to render 3D ClientGraphics in Inventor: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Navigates the Autodesk Inventor camera to frame the selected hole close-up (~4-6" field of view)
    /// and highlights its physical component occurrence.
    /// </summary>
    public bool ZoomAndHighlightHole(dynamic inventorApp, dynamic asmDoc, HoleMatchResult hole)
    {
        if (inventorApp == null || asmDoc == null || hole == null)
            return false;

        try
        {
            // Determine target center point (inches -> cm)
            double targetX, targetY, targetZ;
            if (hole.Actual != null)
            {
                targetX = hole.Actual.Position.X * InchesToCm;
                targetY = hole.Actual.Position.Y * InchesToCm;
                targetZ = hole.Actual.Position.Z * InchesToCm;
            }
            else if (hole.Expected != null)
            {
                targetX = hole.Expected.ExpectedPosition.X * InchesToCm;
                targetY = hole.Expected.ExpectedPosition.Y * InchesToCm;
                targetZ = hole.Expected.ExpectedPosition.Z * InchesToCm;
            }
            else
            {
                return false;
            }

            dynamic transGeom = inventorApp.TransientGeometry;
            dynamic activeView = inventorApp.ActiveView;
            if (activeView == null && asmDoc != null)
            {
                try
                {
                    dynamic views = asmDoc.Views;
                    if (views != null && views.Count > 0)
                    {
                        activeView = views[1];
                    }
                }
                catch { }
            }

            if (activeView != null)
            {
                dynamic camera = activeView.Camera;
                dynamic currentEye = camera.Eye;
                dynamic currentTarget = camera.Target;

                // Compute viewing vector from target to eye
                double dx = (double)currentEye.X - (double)currentTarget.X;
                double dy = (double)currentEye.Y - (double)currentTarget.Y;
                double dz = (double)currentEye.Z - (double)currentTarget.Z;
                double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                // Default close-up framing distance: ~15 cm (~6 inches)
                double desiredDistance = 15.0;

                if (len < 0.001)
                {
                    dx = 10; dy = 10; dz = 15;
                    len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                }

                double unitDx = dx / len;
                double unitDy = dy / len;
                double unitDz = dz / len;

                camera.Target = transGeom.CreatePoint(targetX, targetY, targetZ);
                camera.Eye = transGeom.CreatePoint(
                    targetX + unitDx * desiredDistance,
                    targetY + unitDy * desiredDistance,
                    targetZ + unitDz * desiredDistance);

                try
                {
                    camera.Perspective = false; // Orthographic close-up inspection
                }
                catch { }

                camera.Apply();
            }

            // Occurrence highlighting via HighlightSet
            HighlightOccurrence(asmDoc, hole.OwningOccurrence);

            try { asmDoc.Dirty = false; } catch { }
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not zoom and highlight hole in Inventor: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Highlights an occurrence in Autodesk Inventor using a native HighlightSet.
    /// </summary>
    public void HighlightOccurrence(dynamic asmDoc, string? occurrenceName)
    {
        if (asmDoc == null) return;

        try
        {
            if (_activeHighlightSet != null)
            {
                try { _activeHighlightSet.Clear(); } catch { }
            }

            if (string.IsNullOrWhiteSpace(occurrenceName) || occurrenceName == "—")
                return;

            if (_activeHighlightSet == null)
            {
                try
                {
                    _activeHighlightSet = asmDoc.CreateHighlightSet();
                }
                catch { }
            }

            if (_activeHighlightSet == null) return;

            // Search occurrence in assembly
            dynamic compDef = asmDoc.ComponentDefinition;
            dynamic occurrences = compDef.Occurrences;
            dynamic? targetOcc = FindOccurrenceRecursive(occurrences, occurrenceName);

            if (targetOcc != null)
            {
                _activeHighlightSet.AddItem(targetOcc);
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Could not highlight occurrence '{occurrenceName}': {ex.Message}");
        }
    }

    private dynamic? FindOccurrenceRecursive(dynamic occurrences, string targetName)
    {
        if (occurrences == null) return null;

        int count = occurrences.Count;
        for (int i = 1; i <= count; i++)
        {
            try
            {
                dynamic occ = occurrences.Item[i];
                string name = (string)occ.Name;
                if (string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return occ;
                }

                dynamic subOccs = occ.SubOccurrences;
                if (subOccs != null && (int)subOccs.Count > 0)
                {
                    dynamic? found = FindOccurrenceRecursive(subOccs, targetName);
                    if (found != null) return found;
                }
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Deletes all temporary visual overlays, clears highlights, and restores document clean state.
    /// </summary>
    public void ClearOverlays(dynamic asmDoc)
    {
        if (asmDoc == null) return;

        try
        {
            if (_activeHighlightSet != null)
            {
                try { _activeHighlightSet.Clear(); } catch { }
                try { _activeHighlightSet.Delete(); } catch { }
                _activeHighlightSet = null;
            }

            dynamic compDef = asmDoc.ComponentDefinition;
            dynamic clientGraphicsCol = compDef.ClientGraphicsCollection;
            int cgCount = clientGraphicsCol.Count;
            for (int i = cgCount; i >= 1; i--)
            {
                try
                {
                    dynamic cg = clientGraphicsCol.Item[i];
                    string clientId = (string)cg.ClientId;
                    if (string.Equals(clientId, ClientGraphicsClientId, StringComparison.OrdinalIgnoreCase))
                    {
                        cg.Delete();
                    }
                }
                catch { }
            }

            dynamic dataSetsCol = asmDoc.GraphicsDataSetsCollection;
            int dsCount = dataSetsCol.Count;
            for (int i = dsCount; i >= 1; i--)
            {
                try
                {
                    dynamic ds = dataSetsCol.Item[i];
                    string clientId = (string)ds.ClientId;
                    if (string.Equals(clientId, GraphicsDataSetsClientId, StringComparison.OrdinalIgnoreCase))
                    {
                        ds.Delete();
                    }
                }
                catch { }
            }

            try { asmDoc.Dirty = false; } catch { }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.Instance.Warn($"Error clearing ClientGraphics overlays: {ex.Message}");
        }
    }
}
