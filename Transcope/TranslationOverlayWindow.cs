using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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
        controlWindow.AutoTranslationChanged += (_, e) =>
            AutoTranslationChanged?.Invoke(this, e);
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

    public event EventHandler<OverlayAutoTranslationChangedEventArgs>? AutoTranslationChanged;

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

internal sealed class OverlayAutoTranslationChangedEventArgs : EventArgs
{
    public OverlayAutoTranslationChangedEventArgs(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }

    public bool IsEnabled { get; }
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
    private const double ToolbarOutsideHeight = 48;
    private const double ToolbarGap = 6;
    private const double ToolbarMinimumWidth = 236;
    private static readonly nint TopMostWindow = new(-1);

    private readonly Canvas root;
    private readonly Border frameBorder;
    private readonly Border toolbarChrome;
    private readonly Border sizeBadge;
    private readonly TextBlock sizeBadgeText;
    private readonly Thumb dragThumb;
    private readonly Thumb resizeThumb;
    private readonly Button continueButton;
    private readonly Button lockButton;
    private readonly ToggleButton autoSwitch;
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
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 77, 163, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(77, 163, 255),
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.22
            },
            IsHitTestVisible = false
        };

        Button closeButton = CreateControlButton("X", "关闭覆盖框");
        closeButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        lockButton = CreateControlButton("锁", "锁定位置和大小");
        lockButton.Click += (_, _) => ToggleLock();

        continueButton = CreateControlButton("译", "继续翻译");
        continueButton.MinWidth = 42;
        continueButton.PreviewMouseLeftButtonDown += ContinueButton_PreviewMouseLeftButtonDown;
        continueButton.Click += (_, _) => RequestContinueTranslation();

        autoSwitch = new ToggleButton
        {
            Content = "AUTO",
            ToolTip = "实时自动翻译",
            Width = 62,
            Height = 30,
            Margin = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(205, 218, 236)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Focusable = false,
            IsTabStop = false,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = CreateAutoSwitchTemplate()
        };
        autoSwitch.Checked += AutoSwitch_Changed;
        autoSwitch.Unchecked += AutoSwitch_Changed;

        dragThumb = new Thumb
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(2, 0, 4, 0),
            Cursor = System.Windows.Input.Cursors.SizeAll,
            Background = Brushes.Transparent,
            ToolTip = "Move overlay",
            Focusable = false,
            Template = CreateDragThumbTemplate()
        };
        dragThumb.DragDelta += DragThumb_DragDelta;

        StackPanel buttonBar = new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(5, 4, 5, 4)
        };
        buttonBar.Children.Add(dragThumb);
        buttonBar.Children.Add(closeButton);
        buttonBar.Children.Add(lockButton);
        buttonBar.Children.Add(continueButton);
        buttonBar.Children.Add(autoSwitch);

        toolbarChrome = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(238, 14, 18, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(130, 122, 180, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Child = buttonBar,
            Margin = new Thickness(0),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 0,
                Opacity = 0.45
            }
        };

        sizeBadgeText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(230, 231, 238, 248)),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };

        sizeBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(214, 14, 18, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(110, 122, 180, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 4, 8, 4),
            Child = sizeBadgeText,
            IsHitTestVisible = false
        };

        resizeThumb = new Thumb
        {
            Width = 26,
            Height = 26,
            Cursor = System.Windows.Input.Cursors.SizeNWSE,
            Background = Brushes.Transparent,
            Template = CreateResizeThumbTemplate()
        };
        resizeThumb.DragDelta += ResizeThumb_DragDelta;

        root = new Canvas
        {
            Background = Brushes.Transparent
        };
        root.Children.Add(frameBorder);
        root.Children.Add(toolbarChrome);
        root.Children.Add(sizeBadge);
        root.Children.Add(resizeThumb);

        Content = root;
        SourceInitialized += (_, _) => ConfigureNativeWindow();
        SyncBounds(initialBounds);
        UpdateLockVisualState();
    }

    public event EventHandler? ContinueTranslationRequested;

    public event EventHandler? CloseRequested;

    public event EventHandler<OverlayAutoTranslationChangedEventArgs>? AutoTranslationChanged;

    public event EventHandler<OverlayBoundsChangedEventArgs>? BoundsChanged;

    public void SetTranslationRunning(bool isRunning)
    {
        continueButton.IsEnabled = !isRunning;
        continueButton.Content = isRunning ? "..." : "译";
        continueButton.ToolTip = isRunning ? "正在翻译" : "继续翻译";
    }

    public void SyncBounds(ScreenCaptureBounds updatedBounds)
    {
        bounds = NormalizeBounds(updatedBounds);
        bool placeToolbarAbove = bounds.Y - ToolbarOutsideHeight >= SystemParameters.VirtualScreenTop;
        double frameTop = placeToolbarAbove ? ToolbarOutsideHeight : 0;
        double toolbarTop = placeToolbarAbove ? 0 : bounds.Height + ToolbarGap;
        double windowWidth = Math.Max(bounds.Width, ToolbarMinimumWidth);
        double windowHeight = bounds.Height + ToolbarOutsideHeight;
        int windowLeft = bounds.X;
        int windowTop = placeToolbarAbove
            ? bounds.Y - (int)Math.Ceiling(ToolbarOutsideHeight)
            : bounds.Y;

        Left = windowLeft;
        Top = windowTop;
        Width = windowWidth;
        Height = windowHeight;
        root.Width = windowWidth;
        root.Height = windowHeight;

        frameBorder.Width = bounds.Width;
        frameBorder.Height = bounds.Height;
        Canvas.SetLeft(frameBorder, 0);
        Canvas.SetTop(frameBorder, frameTop);

        Canvas.SetLeft(toolbarChrome, 0);
        Canvas.SetTop(toolbarChrome, toolbarTop);

        sizeBadgeText.Text = $"{bounds.Width} x {bounds.Height}";
        sizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(sizeBadge, Math.Max(0, bounds.Width - sizeBadge.DesiredSize.Width - 10));
        Canvas.SetTop(sizeBadge, frameTop + Math.Max(0, bounds.Height - sizeBadge.DesiredSize.Height - 10));

        Canvas.SetLeft(resizeThumb, Math.Max(0, bounds.Width - 30));
        Canvas.SetTop(resizeThumb, frameTop + Math.Max(0, bounds.Height - 30));

        if (new WindowInteropHelper(this).Handle != nint.Zero)
        {
            nint handle = new WindowInteropHelper(this).Handle;
            _ = SetWindowPos(
                handle,
                TopMostWindow,
                windowLeft,
                windowTop,
                (int)Math.Ceiling(windowWidth),
                (int)Math.Ceiling(windowHeight),
                SetWindowPositionNoActivate | SetWindowPositionShowWindow);
        }
    }

    private static Button CreateControlButton(string content, string tooltip)
    {
        Button button = new()
        {
            Content = content,
            ToolTip = tooltip,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(0),
            MinWidth = 34,
            MinHeight = 30,
            Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(235, 242, 252)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Focusable = false,
            IsTabStop = false,
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = CreateControlButtonTemplate()
        };

        return button;
    }

    private static ControlTemplate CreateControlButtonTemplate()
    {
        FrameworkElementFactory border = new(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));

        FrameworkElementFactory presenter = new(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        presenter.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        border.AppendChild(presenter);

        ControlTemplate template = new(typeof(Button))
        {
            VisualTree = border
        };

        Trigger hoverTrigger = new()
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
            "Chrome"));
        hoverTrigger.Setters.Add(new Setter(
            Control.BorderBrushProperty,
            new SolidColorBrush(Color.FromArgb(130, 122, 180, 255)),
            "Chrome"));
        template.Triggers.Add(hoverTrigger);

        Trigger pressedTrigger = new()
        {
            Property = ButtonBase.IsPressedProperty,
            Value = true
        };
        pressedTrigger.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(70, 77, 163, 255)),
            "Chrome"));
        template.Triggers.Add(pressedTrigger);

        Trigger disabledTrigger = new()
        {
            Property = UIElement.IsEnabledProperty,
            Value = false
        };
        disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.48, "Chrome"));
        template.Triggers.Add(disabledTrigger);

        return template;
    }

    private static ControlTemplate CreateAutoSwitchTemplate()
    {
        FrameworkElementFactory border = new(typeof(Border));
        border.Name = "Chrome";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));

        FrameworkElementFactory presenter = new(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        border.AppendChild(presenter);

        ControlTemplate template = new(typeof(ToggleButton))
        {
            VisualTree = border
        };

        Trigger checkedTrigger = new()
        {
            Property = ToggleButton.IsCheckedProperty,
            Value = true
        };
        checkedTrigger.Setters.Add(new Setter(
            Control.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(220, 39, 126, 87)),
            "Chrome"));
        checkedTrigger.Setters.Add(new Setter(
            Control.BorderBrushProperty,
            new SolidColorBrush(Color.FromArgb(230, 86, 211, 145)),
            "Chrome"));
        checkedTrigger.Setters.Add(new Setter(
            Control.ForegroundProperty,
            Brushes.White));
        template.Triggers.Add(checkedTrigger);

        Trigger hoverTrigger = new()
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        hoverTrigger.Setters.Add(new Setter(
            Control.BorderBrushProperty,
            new SolidColorBrush(Color.FromArgb(150, 122, 180, 255)),
            "Chrome"));
        template.Triggers.Add(hoverTrigger);

        return template;
    }

    private static ControlTemplate CreateDragThumbTemplate()
    {
        FrameworkElementFactory canvas = new(typeof(Canvas));
        canvas.SetValue(FrameworkElement.WidthProperty, 30.0);
        canvas.SetValue(FrameworkElement.HeightProperty, 30.0);

        for (int column = 0; column < 2; column++)
        {
            for (int row = 0; row < 3; row++)
            {
                FrameworkElementFactory dot = new(typeof(Ellipse));
                dot.SetValue(FrameworkElement.WidthProperty, 3.2);
                dot.SetValue(FrameworkElement.HeightProperty, 3.2);
                dot.SetValue(Shape.FillProperty, new SolidColorBrush(Color.FromArgb(210, 196, 214, 238)));
                dot.SetValue(Canvas.LeftProperty, 10.0 + column * 6);
                dot.SetValue(Canvas.TopProperty, 8.0 + row * 6);
                canvas.AppendChild(dot);
            }
        }

        return new ControlTemplate(typeof(Thumb))
        {
            VisualTree = canvas
        };
    }

    private static ControlTemplate CreateResizeThumbTemplate()
    {
        FrameworkElementFactory canvas = new(typeof(Canvas));
        canvas.SetValue(FrameworkElement.WidthProperty, 26.0);
        canvas.SetValue(FrameworkElement.HeightProperty, 26.0);

        for (int index = 0; index < 3; index++)
        {
            FrameworkElementFactory line = new(typeof(Line));
            double offset = 8 + index * 5;
            line.SetValue(Line.X1Property, offset);
            line.SetValue(Line.Y1Property, 22.0);
            line.SetValue(Line.X2Property, 22.0);
            line.SetValue(Line.Y2Property, offset);
            line.SetValue(Shape.StrokeProperty, new SolidColorBrush(Color.FromArgb(210, 122, 180, 255)));
            line.SetValue(Shape.StrokeThicknessProperty, 2.0);
            line.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            line.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            canvas.AppendChild(line);
        }

        return new ControlTemplate(typeof(Thumb))
        {
            VisualTree = canvas
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

    private void AutoSwitch_Changed(object sender, RoutedEventArgs e)
    {
        AutoTranslationChanged?.Invoke(
            this,
            new OverlayAutoTranslationChangedEventArgs(autoSwitch.IsChecked == true));
    }

    private void ToggleLock()
    {
        isLocked = !isLocked;
        UpdateLockVisualState();
    }

    private void UpdateLockVisualState()
    {
        lockButton.Content = isLocked ? "解" : "锁";
        lockButton.ToolTip = isLocked ? "解除锁定" : "锁定位置和大小";
        dragThumb.Visibility = isLocked ? Visibility.Collapsed : Visibility.Visible;
        resizeThumb.Visibility = isLocked ? Visibility.Collapsed : Visibility.Visible;
        sizeBadge.Visibility = isLocked ? Visibility.Collapsed : Visibility.Visible;
        frameBorder.BorderBrush = isLocked
            ? new SolidColorBrush(Color.FromArgb(125, 122, 180, 255))
            : new SolidColorBrush(Color.FromArgb(220, 77, 163, 255));
        toolbarChrome.Opacity = isLocked ? 0.84 : 1.0;
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

        SyncBounds(bounds);

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
