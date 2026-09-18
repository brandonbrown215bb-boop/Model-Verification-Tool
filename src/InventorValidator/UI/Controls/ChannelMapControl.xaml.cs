using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using InventorValidator.Geometry.Models;
using InventorValidator.UI.ViewModels;

namespace InventorValidator.UI.Controls;

public partial class ChannelMapControl : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable<HoleMatchResultItemViewModel>),
            typeof(ChannelMapControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(HoleMatchResultItemViewModel),
            typeof(ChannelMapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public static readonly DependencyProperty SelectedGroupProperty =
        DependencyProperty.Register(
            nameof(SelectedGroup),
            typeof(string),
            typeof(ChannelMapControl),
            new FrameworkPropertyMetadata("All Groups", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedGroupChanged));

    public IEnumerable<HoleMatchResultItemViewModel>? ItemsSource
    {
        get => (IEnumerable<HoleMatchResultItemViewModel>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public HoleMatchResultItemViewModel? SelectedItem
    {
        get => (HoleMatchResultItemViewModel?)GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string SelectedGroup
    {
        get => (string)GetValue(SelectedGroupProperty);
        set => SetValue(SelectedGroupProperty, value);
    }

    private bool _isPanning;
    private Point _lastPanPoint;
    private readonly Dictionary<HoleMatchResultItemViewModel, Shape> _holeGlyphs = new();
    private Shape? _activeSelectionHalo;

    public ChannelMapControl()
    {
        InitializeComponent();
        SizeChanged += (s, e) => RedrawMap();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChannelMapControl ctrl)
        {
            ctrl.RedrawMap();
        }
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChannelMapControl ctrl)
        {
            ctrl.UpdateSelectionHighlight();
        }
    }

    private static void OnSelectedGroupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChannelMapControl ctrl)
        {
            ctrl.SyncGroupButtons();
            ctrl.RedrawMap();
        }
    }

    private void SyncGroupButtons()
    {
        string g = SelectedGroup ?? "All Groups";
        BtnAllGroups.IsChecked = string.Equals(g, "All Groups", StringComparison.OrdinalIgnoreCase);
        BtnFloor.IsChecked = g.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0;
        BtnRoof.IsChecked = g.IndexOf("Roof", StringComparison.OrdinalIgnoreCase) >= 0;
        BtnSouthWall.IsChecked = g.IndexOf("South", StringComparison.OrdinalIgnoreCase) >= 0;
        BtnNorthWall.IsChecked = g.IndexOf("North", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnGroupFilterClicked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Content is string content)
        {
            SelectedGroup = content switch
            {
                "Floor" => "Floor Channels",
                "Roof" => "Roof Channels",
                "South Wall" => "South Wall Channels",
                "North Wall" => "North Wall Channels",
                _ => "All Groups"
            };
        }
    }

    public void RedrawMap()
    {
        MapCanvas.Children.Clear();
        _holeGlyphs.Clear();
        _activeSelectionHalo = null;

        var items = ItemsSource?.ToList();
        if (items == null || items.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "No channel location data available to visualize.",
                Foreground = (Brush)FindResource("FgMutedBrush"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(emptyText, 30);
            Canvas.SetTop(emptyText, 30);
            MapCanvas.Children.Add(emptyText);
            return;
        }

        // Filter by group if needed
        IEnumerable<HoleMatchResultItemViewModel> displayItems = items;
        if (!string.IsNullOrWhiteSpace(SelectedGroup) && !SelectedGroup.Equals("All Groups", StringComparison.OrdinalIgnoreCase))
        {
            displayItems = items.Where(i => i.ChannelGroup.IndexOf(SelectedGroup.Replace(" Channels", ""), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        var groups = displayItems
            .GroupBy(i => i.ChannelGroup)
            .OrderBy(g => GroupSortOrder(g.Key))
            .ToList();

        if (groups.Count == 0)
        {
            var noFilterText = new TextBlock
            {
                Text = $"No channels found for group: {SelectedGroup}",
                Foreground = (Brush)FindResource("FgMutedBrush"),
                FontSize = 13
            };
            Canvas.SetLeft(noFilterText, 30);
            Canvas.SetTop(noFilterText, 30);
            MapCanvas.Children.Add(noFilterText);
            return;
        }

        // Determine global axis range (inches) across all displayed items
        double minAxis = double.MaxValue;
        double maxAxis = double.MinValue;

        foreach (var it in displayItems)
        {
            double val = it.Model.Actual != null ? it.Model.Actual.Position.X : (it.Model.Expected?.ExpectedPosition.X ?? 0);
            if (it.ChannelGroup.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                val = it.Model.Actual != null ? it.Model.Actual.Position.Y : (it.Model.Expected?.ExpectedPosition.Y ?? 0);
            }

            if (val < minAxis) minAxis = val;
            if (val > maxAxis) maxAxis = val;
        }

        if (minAxis > maxAxis || Math.Abs(maxAxis - minAxis) < 0.1)
        {
            minAxis = 0;
            maxAxis = 180;
        }

        // Padding around coordinates
        double startAxis = Math.Max(0, Math.Floor(minAxis - 2.0));
        double endAxis = Math.Ceiling(maxAxis + 4.0);
        double axisSpan = Math.Max(10.0, endAxis - startAxis);

        // Layout metrics
        double trackLeft = 200.0;
        double availableWidth = Math.Max(700.0, CanvasContainer.ActualWidth - trackLeft - 60.0);
        double pixelsPerInch = availableWidth / axisSpan;

        double currentY = 20.0;

        foreach (var grp in groups)
        {
            string groupName = grp.Key;
            string axisLabel = groupName.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0 ? "Y (Width)" : "X (Length)";

            // 1. Group Header Banner
            var groupHeader = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 88, 166, 255)),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(10, 4, 10, 4),
                Width = trackLeft + availableWidth
            };
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = $"{groupName.ToUpperInvariant()}  •  Axis: {axisLabel}",
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = (Brush)FindResource("AccentBrush")
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = $"  ({grp.Count()} holes)",
                FontSize = 11,
                Foreground = (Brush)FindResource("FgMutedBrush")
            });
            groupHeader.Child = headerPanel;

            Canvas.SetLeft(groupHeader, 10);
            Canvas.SetTop(groupHeader, currentY);
            MapCanvas.Children.Add(groupHeader);
            currentY += 32.0;

            // 2. Axis Dimension Ruler
            DrawRuler(trackLeft, currentY, startAxis, endAxis, pixelsPerInch, axisLabel);
            currentY += 26.0;

            // 3. Channels in this group
            var channelSubgroups = grp.GroupBy(i => i.ChannelName).OrderBy(c => c.Key).ToList();

            foreach (var chan in channelSubgroups)
            {
                string chanName = chan.Key;
                var sampleHole = chan.FirstOrDefault();
                string partNum = sampleHole?.ReferencedPart ?? "";
                double zLoc = sampleHole?.Model.Expected?.ExpectedPosition.Z ?? (sampleHole?.Model.Actual?.Position.Z ?? 0.0);

                // Channel Label Block (Left)
                var labelBorder = new Border
                {
                    Width = trackLeft - 18.0,
                    Height = 36.0,
                    Background = (Brush)FindResource("BgSecondaryBrush"),
                    BorderBrush = (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 2, 8, 2)
                };
                var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                labelStack.Children.Add(new TextBlock
                {
                    Text = chanName,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    Foreground = (Brush)FindResource("FgBrightBrush")
                });
                labelStack.Children.Add(new TextBlock
                {
                    Text = $"Part: {partNum}  •  Z: {zLoc:F3}\"",
                    FontSize = 10,
                    Foreground = (Brush)FindResource("FgMutedBrush")
                });
                labelBorder.Child = labelStack;

                Canvas.SetLeft(labelBorder, 10);
                Canvas.SetTop(labelBorder, currentY);
                MapCanvas.Children.Add(labelBorder);

                // Channel Structural Beam Track (Right)
                var trackBeam = new Border
                {
                    Width = availableWidth,
                    Height = 36.0,
                    Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3)
                };

                // Centerline in beam
                var centerLine = new Line
                {
                    X1 = trackLeft,
                    Y1 = currentY + 18.0,
                    X2 = trackLeft + availableWidth,
                    Y2 = currentY + 18.0,
                    Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    StrokeDashArray = new DoubleCollection { 4, 4 },
                    StrokeThickness = 1
                };

                Canvas.SetLeft(trackBeam, trackLeft);
                Canvas.SetTop(trackBeam, currentY);
                MapCanvas.Children.Add(trackBeam);
                MapCanvas.Children.Add(centerLine);

                // Plot holes on this channel
                foreach (var hole in chan)
                {
                    DrawHole(hole, trackLeft, currentY + 18.0, startAxis, pixelsPerInch);
                }

                currentY += 46.0;
            }

            currentY += 16.0;
        }

        MapCanvas.Width = Math.Max(trackLeft + availableWidth + 50.0, 900.0);
        MapCanvas.Height = Math.Max(currentY + 50.0, 380.0);

        UpdateSelectionHighlight();
    }

    private void DrawRuler(double trackLeft, double y, double startAxis, double endAxis, double ppi, string axisLabel)
    {
        double rulerWidth = (endAxis - startAxis) * ppi;

        var rulerLine = new Line
        {
            X1 = trackLeft,
            Y1 = y + 14.0,
            X2 = trackLeft + rulerWidth,
            Y2 = y + 14.0,
            Stroke = (Brush)FindResource("BorderBrush"),
            StrokeThickness = 1
        };
        MapCanvas.Children.Add(rulerLine);

        // Major tick interval (every 12 inches if span > 36, else every 6 or 2 inches)
        double span = endAxis - startAxis;
        double tickInterval = span > 100 ? 24.0 : (span > 40 ? 12.0 : 6.0);

        double firstTick = Math.Ceiling(startAxis / tickInterval) * tickInterval;
        for (double t = firstTick; t <= endAxis; t += tickInterval)
        {
            double tx = trackLeft + (t - startAxis) * ppi;

            var tick = new Line
            {
                X1 = tx,
                Y1 = y + 8.0,
                X2 = tx,
                Y2 = y + 14.0,
                Stroke = (Brush)FindResource("FgMutedBrush"),
                StrokeThickness = 1
            };
            MapCanvas.Children.Add(tick);

            var tickText = new TextBlock
            {
                Text = $"{t:F0}\"",
                FontSize = 9,
                Foreground = (Brush)FindResource("FgMutedBrush")
            };
            Canvas.SetLeft(tickText, tx - 10.0);
            Canvas.SetTop(tickText, y - 4.0);
            MapCanvas.Children.Add(tickText);
        }
    }

    private void DrawHole(HoleMatchResultItemViewModel hole, double trackLeft, double trackCenterY, double startAxis, double ppi)
    {
        double expPos = hole.Model.Expected != null ? GetItemAxisPosition(hole.Model.Expected.ExpectedPosition, hole.ChannelGroup) : 0;
        double actPos = hole.Model.Actual != null ? GetItemAxisPosition(hole.Model.Actual.Position, hole.ChannelGroup) : 0;

        double expPx = trackLeft + (expPos - startAxis) * ppi;
        double actPx = trackLeft + (actPos - startAxis) * ppi;

        // 1. Draw expected hole reticle (if expected position exists)
        if (hole.Model.Expected != null)
        {
            var expReticle = new Ellipse
            {
                Width = 14,
                Height = 14,
                Stroke = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                StrokeDashArray = new DoubleCollection { 2, 2 },
                StrokeThickness = 1.2,
                Fill = Brushes.Transparent,
                ToolTip = CreateTooltip(hole)
            };
            Canvas.SetLeft(expReticle, expPx - 7);
            Canvas.SetTop(expReticle, trackCenterY - 7);
            MapCanvas.Children.Add(expReticle);
        }

        // 2. Discrepancy connector line if position mismatch
        if (hole.Model.Expected != null && hole.Model.Actual != null && Math.Abs(actPx - expPx) > 2.0)
        {
            var connector = new Line
            {
                X1 = expPx,
                Y1 = trackCenterY,
                X2 = actPx,
                Y2 = trackCenterY,
                Stroke = (Brush)new BrushConverter().ConvertFromString(hole.StatusColor)!,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 3, 2 }
            };
            MapCanvas.Children.Add(connector);

            // Small displacement callout pill
            var deltaBadge = new Border
            {
                Background = (Brush)FindResource("BgTertiaryBrush"),
                BorderBrush = (Brush)new BrushConverter().ConvertFromString(hole.StatusColor)!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(2, 0, 2, 0)
            };
            deltaBadge.Child = new TextBlock
            {
                Text = hole.DeltaAxisDisplay,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)new BrushConverter().ConvertFromString(hole.StatusColor)!
            };
            Canvas.SetLeft(deltaBadge, (expPx + actPx) / 2.0 - 12);
            Canvas.SetTop(deltaBadge, trackCenterY - 18);
            MapCanvas.Children.Add(deltaBadge);
        }

        // 3. Draw actual hole marker (or missing marker)
        Shape holeShape;
        Color statusColor = (Color)ColorConverter.ConvertFromString(hole.StatusColor);

        if (hole.Status == HoleMatchStatus.MissingExpected)
        {
            // Missing: Dark red ring with cross
            holeShape = new Ellipse
            {
                Width = 14,
                Height = 14,
                Stroke = new SolidColorBrush(statusColor),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(60, statusColor.R, statusColor.G, statusColor.B)),
                Cursor = Cursors.Hand,
                ToolTip = CreateTooltip(hole)
            };
            Canvas.SetLeft(holeShape, expPx - 7);
            Canvas.SetTop(holeShape, trackCenterY - 7);
        }
        else if (hole.Status == HoleMatchStatus.ExtraActual)
        {
            // Extra actual: Purple diamond
            holeShape = new Rectangle
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(statusColor),
                RenderTransform = new RotateTransform(45, 5, 5),
                Cursor = Cursors.Hand,
                ToolTip = CreateTooltip(hole)
            };
            Canvas.SetLeft(holeShape, actPx - 5);
            Canvas.SetTop(holeShape, trackCenterY - 5);
        }
        else
        {
            // Normal Match / Warning / Mislocated: Solid circle
            holeShape = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(statusColor),
                Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
                StrokeThickness = 1,
                Cursor = Cursors.Hand,
                ToolTip = CreateTooltip(hole)
            };
            Canvas.SetLeft(holeShape, actPx - 6);
            Canvas.SetTop(holeShape, trackCenterY - 6);
        }

        holeShape.MouseDown += (s, e) =>
        {
            SelectedItem = hole;
            e.Handled = true;
        };

        MapCanvas.Children.Add(holeShape);
        _holeGlyphs[hole] = holeShape;
    }

    private double GetItemAxisPosition(Point3D pt, string group)
    {
        return group.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0 ? pt.Y : pt.X;
    }

    private static int GroupSortOrder(string groupName) => groupName switch
    {
        "Roof Channels" => 1,
        "North Wall Channels" => 2,
        "South Wall Channels" => 3,
        "Floor Channels" => 4,
        _ => 5
    };

    private object CreateTooltip(HoleMatchResultItemViewModel hole)
    {
        var tip = new StackPanel { MaxWidth = 260 };
        tip.Children.Add(new TextBlock
        {
            Text = $"{hole.ChannelName} #{hole.HoleIndexDisplay} ({hole.StatusBadge})",
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString(hole.StatusColor)!
        });
        tip.Children.Add(new TextBlock { Text = $"Group: {hole.ChannelGroup}", FontSize = 10 });
        tip.Children.Add(new TextBlock { Text = $"Part: {hole.ReferencedPart}", FontSize = 10 });
        tip.Children.Add(new TextBlock { Text = $"Expected: {hole.ExpectedCoordDisplay}", FontSize = 10 });
        tip.Children.Add(new TextBlock { Text = $"Actual: {hole.ActualCoordDisplay}", FontSize = 10 });
        tip.Children.Add(new TextBlock { Text = $"Δ Axis: {hole.DeltaAxisDisplay}  •  Δ Z: {hole.DeltaZDisplay}", FontSize = 10, FontWeight = FontWeights.SemiBold });
        tip.Children.Add(new TextBlock { Text = $"Auth Error: {hole.AuthoritativeErrorDisplay}", FontSize = 10, FontWeight = FontWeights.Bold });
        if (!string.IsNullOrEmpty(hole.Notes))
        {
            tip.Children.Add(new TextBlock { Text = hole.Notes, FontSize = 9, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
        }
        return tip;
    }

    private void UpdateSelectionHighlight()
    {
        if (_activeSelectionHalo != null)
        {
            MapCanvas.Children.Remove(_activeSelectionHalo);
            _activeSelectionHalo = null;
        }

        if (SelectedItem != null && _holeGlyphs.TryGetValue(SelectedItem, out var glyph))
        {
            double gx = Canvas.GetLeft(glyph);
            double gy = Canvas.GetTop(glyph);
            double gw = glyph.Width;
            double gh = glyph.Height;

            _activeSelectionHalo = new Ellipse
            {
                Width = gw + 14,
                Height = gh + 14,
                Stroke = (Brush)FindResource("AccentBrush"),
                StrokeThickness = 2.5,
                Fill = new SolidColorBrush(Color.FromArgb(45, 88, 166, 255)),
                IsHitTestVisible = false
            };

            Canvas.SetLeft(_activeSelectionHalo, gx - 7);
            Canvas.SetTop(_activeSelectionHalo, gy - 7);
            MapCanvas.Children.Add(_activeSelectionHalo);
        }
    }

    // --- Pan & Zoom Controls ---

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point mousePos = e.GetPosition(MapCanvas);
        double zoomFactor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;

        double newScaleX = Math.Clamp(CanvasScale.ScaleX * zoomFactor, 0.25, 4.0);
        double newScaleY = Math.Clamp(CanvasScale.ScaleY * zoomFactor, 0.25, 4.0);

        CanvasScale.ScaleX = newScaleX;
        CanvasScale.ScaleY = newScaleY;

        TxtZoomLevel.Text = $"{newScaleX * 100:F0}%";
        e.Handled = true;
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(CanvasContainer);
            CanvasContainer.CaptureMouse();
            Cursor = Cursors.SizeAll;
        }
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            Point current = e.GetPosition(CanvasContainer);
            double dx = current.X - _lastPanPoint.X;
            double dy = current.Y - _lastPanPoint.Y;

            CanvasTranslate.X += dx;
            CanvasTranslate.Y += dy;

            _lastPanPoint = current;
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            CanvasContainer.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    private void OnZoomInClicked(object sender, RoutedEventArgs e)
    {
        CanvasScale.ScaleX = Math.Clamp(CanvasScale.ScaleX * 1.2, 0.25, 4.0);
        CanvasScale.ScaleY = Math.Clamp(CanvasScale.ScaleY * 1.2, 0.25, 4.0);
        TxtZoomLevel.Text = $"{CanvasScale.ScaleX * 100:F0}%";
    }

    private void OnZoomOutClicked(object sender, RoutedEventArgs e)
    {
        CanvasScale.ScaleX = Math.Clamp(CanvasScale.ScaleX / 1.2, 0.25, 4.0);
        CanvasScale.ScaleY = Math.Clamp(CanvasScale.ScaleY / 1.2, 0.25, 4.0);
        TxtZoomLevel.Text = $"{CanvasScale.ScaleX * 100:F0}%";
    }

    private void OnFitToWindowClicked(object sender, RoutedEventArgs e)
    {
        if (MapCanvas.Width <= 0 || MapCanvas.Height <= 0 || CanvasContainer.ActualWidth <= 0 || CanvasContainer.ActualHeight <= 0)
            return;

        double scaleX = CanvasContainer.ActualWidth / MapCanvas.Width;
        double scaleY = CanvasContainer.ActualHeight / MapCanvas.Height;
        double fitScale = Math.Clamp(Math.Min(scaleX, scaleY) * 0.95, 0.25, 2.5);

        CanvasScale.ScaleX = fitScale;
        CanvasScale.ScaleY = fitScale;
        CanvasTranslate.X = (CanvasContainer.ActualWidth - MapCanvas.Width * fitScale) / 2.0;
        CanvasTranslate.Y = (CanvasContainer.ActualHeight - MapCanvas.Height * fitScale) / 2.0;
        TxtZoomLevel.Text = $"{fitScale * 100:F0}%";
    }

    private void OnResetClicked(object sender, RoutedEventArgs e)
    {
        CanvasScale.ScaleX = 1.0;
        CanvasScale.ScaleY = 1.0;
        CanvasTranslate.X = 0;
        CanvasTranslate.Y = 0;
        TxtZoomLevel.Text = "100%";
    }
}
