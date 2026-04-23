using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private const double MinimumSelectionSize = 4;

    private readonly Canvas surface;
    private readonly WpfRectangle selectionRectangle;
    private WpfPoint? dragStart;

    public ScreenRegionSelectionWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(MediaColor.FromArgb(72, 0, 0, 0));
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
            ClipToBounds = true
        };

        selectionRectangle = new WpfRectangle
        {
            Stroke = MediaBrushes.White,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(MediaColor.FromArgb(36, 255, 255, 255)),
            Visibility = Visibility.Collapsed
        };

        surface.Children.Add(selectionRectangle);
        surface.Children.Add(CreateHint());
        Content = surface;

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };

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

    private static FrameworkElement CreateHint()
    {
        TextBlock text = new()
        {
            Text = "Drag to select screenshot area. Esc cancels.",
            Foreground = MediaBrushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        };

        Border border = new()
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(220, 18, 25, 22)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(190, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 9, 14, 9),
            Child = text
        };

        Canvas.SetLeft(border, 24);
        Canvas.SetTop(border, 24);

        return border;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(surface);
        UpdateSelectionRectangle(dragStart.Value, dragStart.Value);
        selectionRectangle.Visibility = Visibility.Visible;
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
            selectionRectangle.Visibility = Visibility.Collapsed;
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

        Canvas.SetLeft(selectionRectangle, selection.Left);
        Canvas.SetTop(selectionRectangle, selection.Top);
        selectionRectangle.Width = selection.Width;
        selectionRectangle.Height = selection.Height;
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
