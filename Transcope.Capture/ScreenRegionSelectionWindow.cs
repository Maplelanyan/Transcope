using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfCursors = System.Windows.Input.Cursors;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace Transcope.Capture;

internal sealed class ScreenRegionSelectionWindow : Window
{
    private const double MinimumSelectionSize = 8;
    private const double CornerMarkerSize = 12;

    private readonly Canvas surface;
    private readonly WpfRectangle topMask;
    private readonly WpfRectangle bottomMask;
    private readonly WpfRectangle leftMask;
    private readonly WpfRectangle rightMask;
    private readonly Border selectionFrame;
    private readonly Border dimensionBadge;
    private readonly TextBlock dimensionText;
    private readonly Border[] cornerMarkers;
    private WpfPoint? dragStart;

    public ScreenRegionSelectionWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = WpfCursors.Cross;
        Focusable = true;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        surface = new Canvas
        {
            ClipToBounds = true,
            Background = MediaBrushes.Transparent
        };

        SolidColorBrush maskBrush = new(MediaColor.FromArgb(156, 8, 12, 18));
        topMask = CreateMask(maskBrush);
        bottomMask = CreateMask(maskBrush);
        leftMask = CreateMask(maskBrush);
        rightMask = CreateMask(maskBrush);

        selectionFrame = new Border
        {
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(77, 163, 255)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(MediaColor.FromArgb(24, 77, 163, 255)),
            CornerRadius = new CornerRadius(4),
            Effect = new DropShadowEffect
            {
                Color = MediaColor.FromRgb(77, 163, 255),
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.45
            },
            Visibility = Visibility.Collapsed
        };

        dimensionText = new TextBlock
        {
            Foreground = MediaBrushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code, Consolas"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };

        dimensionBadge = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(232, 15, 20, 30)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(180, 77, 163, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = dimensionText,
            Visibility = Visibility.Collapsed
        };

        cornerMarkers =
        [
            CreateCornerMarker(),
            CreateCornerMarker(),
            CreateCornerMarker(),
            CreateCornerMarker()
        ];

        surface.Children.Add(topMask);
        surface.Children.Add(bottomMask);
        surface.Children.Add(leftMask);
        surface.Children.Add(rightMask);
        surface.Children.Add(selectionFrame);
        foreach (Border marker in cornerMarkers)
        {
            surface.Children.Add(marker);
        }

        surface.Children.Add(dimensionBadge);
        surface.Children.Add(CreateHint());
        Content = surface;

        Loaded += (_, _) =>
        {
            UpdateMasks(Rect.Empty);
            Activate();
            Focus();
        };
        SizeChanged += (_, _) => UpdateMasks(GetCurrentSelection());

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonUp += (_, _) => Cancel();
        KeyDown += OnKeyDown;
    }

    public ScreenCaptureBounds? SelectedBounds { get; private set; }

    public void Cancel()
    {
        DialogResult = false;
        Close();
    }

    private static WpfRectangle CreateMask(System.Windows.Media.Brush brush)
    {
        return new WpfRectangle
        {
            Fill = brush,
            IsHitTestVisible = false
        };
    }

    private static Border CreateCornerMarker()
    {
        return new Border
        {
            Width = CornerMarkerSize,
            Height = CornerMarkerSize,
            Background = new SolidColorBrush(MediaColor.FromRgb(77, 163, 255)),
            BorderBrush = MediaBrushes.White,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
    }

    private static FrameworkElement CreateHint()
    {
        StackPanel content = new()
        {
            Orientation = System.Windows.Controls.Orientation.Vertical
        };

        TextBlock title = new()
        {
            Text = "选择翻译区域",
            Foreground = MediaBrushes.White,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold
        };

        TextBlock subtitle = new()
        {
            Text = "拖拽框选，松开鼠标确认。Esc 或右键取消。",
            Foreground = new SolidColorBrush(MediaColor.FromArgb(214, 228, 236, 247)),
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0)
        };

        content.Children.Add(title);
        content.Children.Add(subtitle);

        Border border = new()
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(235, 18, 24, 34)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(140, 77, 163, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Child = content,
            Effect = new DropShadowEffect
            {
                Color = MediaColor.FromRgb(0, 0, 0),
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.45
            }
        };

        Canvas.SetLeft(border, 28);
        Canvas.SetTop(border, 28);

        return border;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(surface);
        UpdateSelectionRectangle(dragStart.Value, dragStart.Value);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        UpdateSelectionRectangle(start, e.GetPosition(surface));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (dragStart is not { } start)
        {
            return;
        }

        ReleaseMouseCapture();
        Rect selection = Normalize(start, e.GetPosition(surface));
        dragStart = null;

        if (selection.Width < MinimumSelectionSize || selection.Height < MinimumSelectionSize)
        {
            HideSelectionVisuals();
            UpdateMasks(Rect.Empty);
            return;
        }

        SelectedBounds = ClampToVirtualScreen(ToScreenBounds(selection));
        DialogResult = true;
        Close();
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void UpdateSelectionRectangle(WpfPoint start, WpfPoint end)
    {
        Rect selection = Normalize(start, end);

        Canvas.SetLeft(selectionFrame, selection.Left);
        Canvas.SetTop(selectionFrame, selection.Top);
        selectionFrame.Width = selection.Width;
        selectionFrame.Height = selection.Height;
        selectionFrame.Visibility = Visibility.Visible;

        UpdateMasks(selection);
        UpdateCornerMarkers(selection);
        UpdateDimensionBadge(selection);
    }

    private Rect GetCurrentSelection()
    {
        if (selectionFrame.Visibility != Visibility.Visible)
        {
            return Rect.Empty;
        }

        return new Rect(
            Canvas.GetLeft(selectionFrame),
            Canvas.GetTop(selectionFrame),
            selectionFrame.Width,
            selectionFrame.Height);
    }

    private void UpdateMasks(Rect selection)
    {
        double width = Math.Max(0, ActualWidth > 0 ? ActualWidth : Width);
        double height = Math.Max(0, ActualHeight > 0 ? ActualHeight : Height);

        if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0)
        {
            SetRect(topMask, 0, 0, width, height);
            SetRect(bottomMask, 0, 0, 0, 0);
            SetRect(leftMask, 0, 0, 0, 0);
            SetRect(rightMask, 0, 0, 0, 0);
            return;
        }

        double left = Math.Clamp(selection.Left, 0, width);
        double top = Math.Clamp(selection.Top, 0, height);
        double right = Math.Clamp(selection.Right, 0, width);
        double bottom = Math.Clamp(selection.Bottom, 0, height);

        SetRect(topMask, 0, 0, width, top);
        SetRect(bottomMask, 0, bottom, width, Math.Max(0, height - bottom));
        SetRect(leftMask, 0, top, left, Math.Max(0, bottom - top));
        SetRect(rightMask, right, top, Math.Max(0, width - right), Math.Max(0, bottom - top));
    }

    private void UpdateCornerMarkers(Rect selection)
    {
        if (selection.Width < MinimumSelectionSize || selection.Height < MinimumSelectionSize)
        {
            foreach (Border marker in cornerMarkers)
            {
                marker.Visibility = Visibility.Collapsed;
            }

            return;
        }

        double half = CornerMarkerSize / 2;
        SetCornerMarker(cornerMarkers[0], selection.Left - half, selection.Top - half);
        SetCornerMarker(cornerMarkers[1], selection.Right - half, selection.Top - half);
        SetCornerMarker(cornerMarkers[2], selection.Left - half, selection.Bottom - half);
        SetCornerMarker(cornerMarkers[3], selection.Right - half, selection.Bottom - half);
    }

    private void UpdateDimensionBadge(Rect selection)
    {
        if (selection.Width < MinimumSelectionSize || selection.Height < MinimumSelectionSize)
        {
            dimensionBadge.Visibility = Visibility.Collapsed;
            return;
        }

        dimensionText.Text = $"{Math.Round(selection.Width)} x {Math.Round(selection.Height)}";
        dimensionBadge.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));

        double badgeWidth = dimensionBadge.DesiredSize.Width;
        double badgeHeight = dimensionBadge.DesiredSize.Height;
        double badgeLeft = selection.Left;
        double badgeTop = selection.Top - badgeHeight - 10;

        if (badgeTop < 10)
        {
            badgeTop = Math.Min(selection.Bottom + 10, Math.Max(10, ActualHeight - badgeHeight - 10));
        }

        badgeLeft = Math.Clamp(badgeLeft, 10, Math.Max(10, ActualWidth - badgeWidth - 10));

        Canvas.SetLeft(dimensionBadge, badgeLeft);
        Canvas.SetTop(dimensionBadge, badgeTop);
        dimensionBadge.Visibility = Visibility.Visible;
    }

    private void HideSelectionVisuals()
    {
        selectionFrame.Visibility = Visibility.Collapsed;
        dimensionBadge.Visibility = Visibility.Collapsed;
        foreach (Border marker in cornerMarkers)
        {
            marker.Visibility = Visibility.Collapsed;
        }
    }

    private static void SetRect(FrameworkElement element, double left, double top, double width, double height)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = width;
        element.Height = height;
    }

    private static void SetCornerMarker(Border marker, double left, double top)
    {
        Canvas.SetLeft(marker, left);
        Canvas.SetTop(marker, top);
        marker.Visibility = Visibility.Visible;
    }

    private ScreenCaptureBounds ToScreenBounds(Rect selection)
    {
        WpfPoint topLeft = PointToScreen(new WpfPoint(selection.Left, selection.Top));
        WpfPoint bottomRight = PointToScreen(new WpfPoint(selection.Right, selection.Bottom));

        int left = (int)Math.Floor(Math.Min(topLeft.X, bottomRight.X));
        int top = (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y));
        int right = (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X));
        int bottom = (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y));

        return new ScreenCaptureBounds(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static ScreenCaptureBounds ClampToVirtualScreen(ScreenCaptureBounds bounds)
    {
        Forms.Screen[] screens = Forms.Screen.AllScreens;
        if (screens.Length == 0)
        {
            return bounds;
        }

        int leftLimit = screens.Min(static screen => screen.Bounds.Left);
        int topLimit = screens.Min(static screen => screen.Bounds.Top);
        int rightLimit = screens.Max(static screen => screen.Bounds.Right);
        int bottomLimit = screens.Max(static screen => screen.Bounds.Bottom);

        int left = Math.Clamp(bounds.X, leftLimit, rightLimit);
        int top = Math.Clamp(bounds.Y, topLimit, bottomLimit);
        int right = Math.Clamp(bounds.X + bounds.Width, leftLimit, rightLimit);
        int bottom = Math.Clamp(bounds.Y + bounds.Height, topLimit, bottomLimit);

        return new ScreenCaptureBounds(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static Rect Normalize(WpfPoint start, WpfPoint end)
    {
        return new Rect(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X),
            Math.Abs(end.Y - start.Y));
    }
}
