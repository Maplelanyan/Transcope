using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Transcope.Capture;

namespace Transcope;

internal sealed class TranslationOverlayWindow : Window
{
    private const int HitTestMessage = 0x0084;
    private const int HitTestTransparent = -1;
    private const int HitTestClient = 1;
    private const int ExtendedWindowStyleIndex = -20;
    private const int WindowExLayered = 0x00080000;
    private const int WindowExToolWindow = 0x00000080;
    private const int WindowExNoActivate = 0x08000000;
    private const int SetWindowPositionNoActivate = 0x0010;
    private const int SetWindowPositionShowWindow = 0x0040;
    private const uint WindowDisplayAffinityExcludeFromCapture = 0x00000011;
    private static readonly nint TopMostWindow = new(-1);

    private readonly ScreenCaptureBounds screenBounds;
    private readonly Canvas surface;
    private readonly Button continueButton;

    public TranslationOverlayWindow(ScreenCaptureBounds screenBounds)
    {
        this.screenBounds = screenBounds;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = screenBounds.X;
        Top = screenBounds.Y;
        Width = screenBounds.Width;
        Height = screenBounds.Height;

        surface = new Canvas
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = true,
            ClipToBounds = false
        };

        Rectangle shade = new()
        {
            Fill = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
            Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 244, 214)),
            StrokeThickness = 1,
            Width = screenBounds.Width,
            Height = screenBounds.Height,
            IsHitTestVisible = false
        };
        surface.Children.Add(shade);

        continueButton = new Button
        {
            Content = "继续翻译",
            MinWidth = 88,
            MinHeight = 32,
            Padding = new Thickness(10, 4, 10, 4),
            Background = new SolidColorBrush(Color.FromArgb(230, 247, 201, 95)),
            Foreground = new SolidColorBrush(Color.FromRgb(18, 25, 22)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = true
        };
        continueButton.Click += (_, _) => ContinueTranslationRequested?.Invoke(this, EventArgs.Empty);
        Canvas.SetLeft(continueButton, 8);
        Canvas.SetTop(continueButton, 8);
        surface.Children.Add(continueButton);

        Content = surface;
        SourceInitialized += (_, _) => ConfigureNativeWindow();
    }

    public event EventHandler? ContinueTranslationRequested;

    public void SetTranslationRunning(bool isRunning)
    {
        continueButton.IsEnabled = !isRunning;
        continueButton.Content = isRunning ? "翻译中..." : "继续翻译";
    }

    public void Render(IReadOnlyList<TranslatedOverlayItem> items)
    {
        RemoveRenderedText();

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        surface.Width = screenBounds.Width / dpi.DpiScaleX;
        surface.Height = screenBounds.Height / dpi.DpiScaleY;

        foreach (TranslatedOverlayItem item in items)
        {
            Rect bounds = Inflate(item.Bounds, horizontal: 3, vertical: 1);
            double left = Math.Max(0, bounds.Left / dpi.DpiScaleX);
            double top = Math.Max(0, bounds.Top / dpi.DpiScaleY);
            double width = Math.Max(24, surface.Width - left - 4);
            double height = Math.Min(surface.Height - top, Math.Max(18, bounds.Height / dpi.DpiScaleY));

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

        Panel.SetZIndex(continueButton, 10);
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

    private void ConfigureNativeWindow()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);

        int style = GetWindowLong(handle, ExtendedWindowStyleIndex);
        _ = SetWindowLong(
            handle,
            ExtendedWindowStyleIndex,
            style | WindowExLayered | WindowExToolWindow | WindowExNoActivate);

        _ = SetWindowPos(
            handle,
            TopMostWindow,
            screenBounds.X,
            screenBounds.Y,
            screenBounds.Width,
            screenBounds.Height,
            SetWindowPositionNoActivate | SetWindowPositionShowWindow);

        _ = SetWindowDisplayAffinity(handle, WindowDisplayAffinityExcludeFromCapture);
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != HitTestMessage)
        {
            return nint.Zero;
        }

        Point screenPoint = new(
            unchecked((short)((long)lParam & 0xFFFF)),
            unchecked((short)(((long)lParam >> 16) & 0xFFFF)));

        Point clientPoint = PointFromScreen(screenPoint);
        Point buttonPoint = continueButton.TranslatePoint(new Point(0, 0), this);
        Rect buttonBounds = new(
            buttonPoint.X,
            buttonPoint.Y,
            continueButton.ActualWidth,
            continueButton.ActualHeight);

        handled = true;
        return buttonBounds.Contains(clientPoint) ? HitTestClient : HitTestTransparent;
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
