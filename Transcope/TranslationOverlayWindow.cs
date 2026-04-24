using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Transcope.Capture;

namespace Transcope;

internal sealed class TranslationOverlayWindow : Window
{
    private const int ExtendedWindowStyleIndex = -20;
    private const int WindowExTransparent = 0x00000020;
    private const int WindowExLayered = 0x00080000;
    private const int WindowExToolWindow = 0x00000080;
    private const int WindowExNoActivate = 0x08000000;
    private const int SetWindowPositionNoActivate = 0x0010;
    private const int SetWindowPositionShowWindow = 0x0040;
    private const uint WindowDisplayAffinityExcludeFromCapture = 0x00000011;
    private static readonly nint TopMostWindow = new(-1);

    private readonly Canvas surface;
    private readonly Rectangle shade;
    private readonly OverlayControlWindow controlWindow;
    private ScreenCaptureBounds bounds;

    public TranslationOverlayWindow(ScreenCaptureBounds initialBounds)
    {
        bounds = NormalizeBounds(initialBounds);
        controlWindow = new OverlayControlWindow(bounds);
        controlWindow.ContinueTranslationRequested += (_, _) =>
            ContinueTranslationRequested?.Invoke(this, EventArgs.Empty);
        controlWindow.CloseRequested += (_, _) => Close();
        controlWindow.BoundsChanged += (_, e) => ApplyBounds(e.Bounds, notify: true);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        surface = new Canvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            ClipToBounds = false
        };

        shade = new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
            Stroke = new SolidColorBrush(Color.FromArgb(190, 134, 192, 255)),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        surface.Children.Add(shade);

        Content = surface;
        SourceInitialized += (_, _) => ConfigureNativeWindow();
        Loaded += (_, _) => controlWindow.Show();
        Closed += (_, _) => controlWindow.Close();

        ApplyBounds(bounds, notify: false);
    }

    public event EventHandler? ContinueTranslationRequested;

    public event EventHandler<OverlayBoundsChangedEventArgs>? BoundsChanged;

    public ScreenCaptureBounds Bounds => bounds;

    public void SetTranslationRunning(bool isRunning)
    {
        controlWindow.SetTranslationRunning(isRunning);
    }

    public void HideControls()
    {
        controlWindow.Hide();
    }

    public void ShowControls()
    {
        controlWindow.Show();
    }

    public void Render(IReadOnlyList<TranslatedOverlayItem> items)
    {
        RemoveRenderedText();

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        surface.Width = bounds.Width / dpi.DpiScaleX;
        surface.Height = bounds.Height / dpi.DpiScaleY;
        shade.Width = surface.Width;
        shade.Height = surface.Height;

        foreach (TranslatedOverlayItem item in items)
        {
            Rect itemBounds = Inflate(item.Bounds, horizontal: 3, vertical: 1);
            double left = Math.Max(0, itemBounds.Left / dpi.DpiScaleX);
            double top = Math.Max(0, itemBounds.Top / dpi.DpiScaleY);
            double width = Math.Max(24, surface.Width - left - 4);
            double height = Math.Min(surface.Height - top, Math.Max(18, itemBounds.Height / dpi.DpiScaleY));

            if (width <= 0 || height <= 0)
            {
                continue;
            }

            TextBlock text = new()
            {
                Text = item.Text,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = Math.Clamp(height * 0.62, 11, 30),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = Math.Clamp(height * 0.74, 13, 34),
                Width = width,
                MaxHeight = Math.Max(height * 2.6, 26),
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 4,
                    ShadowDepth = 1,
                    Opacity = 0.85
                }
            };

            Canvas.SetLeft(text, left);
            Canvas.SetTop(text, top);
            surface.Children.Add(text);
        }
    }

    private void ApplyBounds(ScreenCaptureBounds updatedBounds, bool notify)
    {
        bounds = NormalizeBounds(updatedBounds);
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        surface.Width = bounds.Width;
        surface.Height = bounds.Height;
        shade.Width = bounds.Width;
        shade.Height = bounds.Height;

        controlWindow.SyncBounds(bounds);

        if (new WindowInteropHelper(this).Handle != nint.Zero)
        {
            nint handle = new WindowInteropHelper(this).Handle;
            _ = SetWindowPos(
                handle,
                TopMostWindow,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                SetWindowPositionNoActivate | SetWindowPositionShowWindow);
        }

        if (notify)
        {
            BoundsChanged?.Invoke(this, new OverlayBoundsChangedEventArgs(bounds));
        }
    }

    private void RemoveRenderedText()
    {
        for (int index = surface.Children.Count - 1; index >= 0; index--)
        {
            if (surface.Children[index] is TextBlock)
            {
                surface.Children.RemoveAt(index);
            }
        }
    }

    private static Rect Inflate(Rect rect, double horizontal, double vertical)
    {
        return new Rect(
            rect.X - horizontal,
            rect.Y - vertical,
            rect.Width + horizontal * 2,
            rect.Height + vertical * 2);
    }

    private static ScreenCaptureBounds NormalizeBounds(ScreenCaptureBounds source)
    {
        return new ScreenCaptureBounds(
            source.X,
            source.Y,
            Math.Max(80, source.Width),
            Math.Max(48, source.Height));
    }

    private void ConfigureNativeWindow()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(handle, ExtendedWindowStyleIndex);
        _ = SetWindowLong(
            handle,
            ExtendedWindowStyleIndex,
            style | WindowExTransparent | WindowExLayered | WindowExToolWindow | WindowExNoActivate);

        _ = SetWindowPos(
            handle,
            TopMostWindow,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SetWindowPositionNoActivate | SetWindowPositionShowWindow);

        _ = SetWindowDisplayAffinity(handle, WindowDisplayAffinityExcludeFromCapture);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint windowHandle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint windowHandle, int index, int newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint windowHandleInsertAfter,
        int x,
        int y,
        int width,
        int height,
        int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint windowHandle, uint affinity);
}

internal sealed record TranslatedOverlayItem(string Text, Rect Bounds);

internal sealed class OverlayBoundsChangedEventArgs : EventArgs
{
    public OverlayBoundsChangedEventArgs(ScreenCaptureBounds bounds)
    {
        Bounds = bounds;
    }

    public ScreenCaptureBounds Bounds { get; }
}

internal sealed class OverlayControlWindow : Window
{
    private const int ExtendedWindowStyleIndex = -20;
    private const int WindowExLayered = 0x00080000;
    private const int WindowExToolWindow = 0x00000080;
    private const int WindowExNoActivate = 0x08000000;
    private const int SetWindowPositionNoActivate = 0x0010;
    private const int SetWindowPositionShowWindow = 0x0040;
    private const uint WindowDisplayAffinityExcludeFromCapture = 0x00000011;
    private const double MinimumWidth = 120;
    private const double MinimumHeight = 72;
    private static readonly nint TopMostWindow = new(-1);

    private readonly Border frameBorder;
    private readonly Thumb dragThumb;
    private readonly Thumb resizeThumb;
    private readonly Button continueButton;
    private readonly Button lockButton;
    private ScreenCaptureBounds bounds;
    private bool isLocked;
    private DateTime lastRequestUtc = DateTime.MinValue;

    public OverlayControlWindow(ScreenCaptureBounds initialBounds)
    {
        bounds = initialBounds;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        frameBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(210, 134, 192, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };

        Button closeButton = CreateControlButton("Close");
        closeButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        lockButton = CreateControlButton("Lock");
        lockButton.Click += (_, _) => ToggleLock();

        continueButton = CreateControlButton("Translate");
        continueButton.MinWidth = 74;
        continueButton.PreviewMouseLeftButtonDown += ContinueButton_PreviewMouseLeftButtonDown;
        continueButton.Click += (_, _) => RequestContinueTranslation();

        StackPanel buttonBar = new()
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromArgb(224, 12, 18, 31)),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        buttonBar.Children.Add(closeButton);
        buttonBar.Children.Add(lockButton);
        buttonBar.Children.Add(continueButton);

        dragThumb = new Thumb
        {
            Height = 28,
            Margin = new Thickness(8, 8, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = System.Windows.Input.Cursors.SizeAll,
            Background = Brushes.Transparent,
            Opacity = 0
        };
        dragThumb.DragDelta += DragThumb_DragDelta;

        resizeThumb = new Thumb
        {
            Width = 18,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 4),
            Cursor = System.Windows.Input.Cursors.SizeNWSE,
            Background = new SolidColorBrush(Color.FromArgb(220, 134, 192, 255))
        };
        resizeThumb.DragDelta += ResizeThumb_DragDelta;

        Grid root = new();
        root.Children.Add(frameBorder);
        root.Children.Add(dragThumb);
        root.Children.Add(buttonBar);
        root.Children.Add(resizeThumb);

        Content = root;
        SourceInitialized += (_, _) => ConfigureNativeWindow();
        SyncBounds(initialBounds);
        UpdateLockVisualState();
    }

    public event EventHandler? ContinueTranslationRequested;

    public event EventHandler? CloseRequested;

    public event EventHandler<OverlayBoundsChangedEventArgs>? BoundsChanged;

    public void SetTranslationRunning(bool isRunning)
    {
        continueButton.IsEnabled = !isRunning;
        continueButton.Content = isRunning ? "Working" : "Translate";
    }

    public void SyncBounds(ScreenCaptureBounds updatedBounds)
    {
        bounds = NormalizeBounds(updatedBounds);
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        if (new WindowInteropHelper(this).Handle != nint.Zero)
        {
            nint handle = new WindowInteropHelper(this).Handle;
            _ = SetWindowPos(
                handle,
                TopMostWindow,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                SetWindowPositionNoActivate | SetWindowPositionShowWindow);
        }
    }

    private static Button CreateControlButton(string content)
    {
        return new Button
        {
            Content = content,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(10, 4, 10, 4),
            MinWidth = 56,
            MinHeight = 28,
            Background = new SolidColorBrush(Color.FromArgb(232, 246, 250, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(22, 29, 40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 134, 192, 255)),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Focusable = false,
            IsTabStop = false
        };
    }

    private void ContinueButton_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RequestContinueTranslation();
        e.Handled = true;
    }

    private void RequestContinueTranslation()
    {
        if (!continueButton.IsEnabled)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if ((now - lastRequestUtc).TotalMilliseconds < 250)
        {
            return;
        }

        lastRequestUtc = now;
        ContinueTranslationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleLock()
    {
        isLocked = !isLocked;
        UpdateLockVisualState();
    }

    private void UpdateLockVisualState()
    {
        lockButton.Content = isLocked ? "Unlock" : "Lock";
        dragThumb.Visibility = isLocked ? Visibility.Collapsed : Visibility.Visible;
        resizeThumb.Visibility = isLocked ? Visibility.Collapsed : Visibility.Visible;
        frameBorder.BorderBrush = isLocked
            ? new SolidColorBrush(Color.FromArgb(150, 134, 192, 255))
            : new SolidColorBrush(Color.FromArgb(210, 134, 192, 255));
    }

    private void DragThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (isLocked)
        {
            return;
        }

        EmitBoundsChanged(new ScreenCaptureBounds(
            bounds.X + (int)Math.Round(e.HorizontalChange),
            bounds.Y + (int)Math.Round(e.VerticalChange),
            bounds.Width,
            bounds.Height));
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (isLocked)
        {
            return;
        }

        EmitBoundsChanged(new ScreenCaptureBounds(
            bounds.X,
            bounds.Y,
            (int)Math.Round(bounds.Width + e.HorizontalChange),
            (int)Math.Round(bounds.Height + e.VerticalChange)));
    }

    private void EmitBoundsChanged(ScreenCaptureBounds updatedBounds)
    {
        ScreenCaptureBounds normalized = NormalizeBounds(updatedBounds);
        SyncBounds(normalized);
        BoundsChanged?.Invoke(this, new OverlayBoundsChangedEventArgs(normalized));
    }

    private static ScreenCaptureBounds NormalizeBounds(ScreenCaptureBounds source)
    {
        return new ScreenCaptureBounds(
            source.X,
            source.Y,
            Math.Max((int)MinimumWidth, source.Width),
            Math.Max((int)MinimumHeight, source.Height));
    }

    private void ConfigureNativeWindow()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(handle, ExtendedWindowStyleIndex);
        _ = SetWindowLong(
            handle,
            ExtendedWindowStyleIndex,
            style | WindowExLayered | WindowExToolWindow | WindowExNoActivate);

        _ = SetWindowPos(
            handle,
            TopMostWindow,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SetWindowPositionNoActivate | SetWindowPositionShowWindow);

        _ = SetWindowDisplayAffinity(handle, WindowDisplayAffinityExcludeFromCapture);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint windowHandle, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint windowHandle, int index, int newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint windowHandleInsertAfter,
        int x,
        int y,
        int width,
        int height,
        int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint windowHandle, uint affinity);
}
