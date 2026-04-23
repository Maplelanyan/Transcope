using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Forms = System.Windows.Forms;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfApplication = System.Windows.Application;
using WpfPixelFormats = System.Windows.Media.PixelFormats;

namespace Transcope.Capture;

public sealed class ScreenCaptureService : IScreenCaptureService
{
    private const int SourceCopyRasterOperation = 0x00CC0020;
    private const int CaptureBltRasterOperation = 0x40000000;

    public async ValueTask<ScreenCaptureResult?> CaptureRegionAsync(
        Window? owner = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Dispatcher? dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            return await dispatcher.InvokeAsync(
                () => CaptureRegionCore(owner, cancellationToken),
                DispatcherPriority.Normal,
                cancellationToken);
        }

        return CaptureRegionCore(owner, cancellationToken);
    }

    public ScreenCaptureResult CapturePrimaryScreen()
    {
        Forms.Screen primaryScreen = Forms.Screen.PrimaryScreen
            ?? throw new InvalidOperationException("No primary screen is available.");

        return Capture(ToBounds(primaryScreen.Bounds));
    }

    public ScreenCaptureResult CaptureVirtualScreen()
    {
        return Capture(ToBounds(Forms.SystemInformation.VirtualScreen));
    }

    public ScreenCaptureResult Capture(ScreenCaptureBounds bounds)
    {
        if (bounds.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Capture bounds must have positive width and height.");
        }

        using Bitmap bitmap = new(bounds.Width, bounds.Height, DrawingPixelFormat.Format32bppPArgb);
        CopyScreenToBitmap(bitmap, bounds);

        return CreateResult(bounds, bitmap);
    }

    private ScreenCaptureResult? CaptureRegionCore(
        Window? owner,
        CancellationToken cancellationToken)
    {
        ScreenRegionSelectionWindow selectionWindow = new();

        if (owner is not null && owner.IsVisible)
        {
            selectionWindow.Owner = owner;
        }

        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
            () => selectionWindow.Dispatcher.BeginInvoke(() => selectionWindow.Cancel()));

        bool? accepted = selectionWindow.ShowDialog();
        cancellationToken.ThrowIfCancellationRequested();

        if (accepted != true || selectionWindow.SelectedBounds is not { } bounds)
        {
            return null;
        }

        return Capture(bounds);
    }

    private static ScreenCaptureResult CreateResult(ScreenCaptureBounds bounds, Bitmap bitmap)
    {
        int bytesPerPixel = Image.GetPixelFormatSize(DrawingPixelFormat.Format32bppPArgb) / 8;
        int destinationStride = bounds.Width * bytesPerPixel;
        byte[] pixelBytes = new byte[destinationStride * bounds.Height];

        BitmapData bitmapData = bitmap.LockBits(
            new DrawingRectangle(0, 0, bounds.Width, bounds.Height),
            ImageLockMode.ReadOnly,
            DrawingPixelFormat.Format32bppPArgb);

        try
        {
            CopyBitmapRows(bitmapData, pixelBytes, destinationStride, bounds.Height);
            EnsureOpaqueAlpha(pixelBytes);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        BitmapSource preview = BitmapSource.Create(
            bounds.Width,
            bounds.Height,
            96,
            96,
            WpfPixelFormats.Pbgra32,
            palette: null,
            pixelBytes,
            destinationStride);
        preview.Freeze();

        SoftwareBitmap softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixelBytes.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            bounds.Width,
            bounds.Height,
            BitmapAlphaMode.Premultiplied);

        return new ScreenCaptureResult(bounds, softwareBitmap, preview);
    }

    private static void EnsureOpaqueAlpha(byte[] pixelBytes)
    {
        for (int index = 3; index < pixelBytes.Length; index += 4)
        {
            pixelBytes[index] = byte.MaxValue;
        }
    }

    private static void CopyScreenToBitmap(Bitmap bitmap, ScreenCaptureBounds bounds)
    {
        nint screenDeviceContext = GetDC(nint.Zero);
        if (screenDeviceContext == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get the screen device context.");
        }

        using Graphics graphics = Graphics.FromImage(bitmap);
        nint destinationDeviceContext = nint.Zero;

        try
        {
            destinationDeviceContext = graphics.GetHdc();
            bool copied = BitBlt(
                destinationDeviceContext,
                0,
                0,
                bounds.Width,
                bounds.Height,
                screenDeviceContext,
                bounds.X,
                bounds.Y,
                SourceCopyRasterOperation | CaptureBltRasterOperation);

            if (!copied)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to capture the selected screen area.");
            }
        }
        finally
        {
            if (destinationDeviceContext != nint.Zero)
            {
                graphics.ReleaseHdc(destinationDeviceContext);
            }

            _ = ReleaseDC(nint.Zero, screenDeviceContext);
        }
    }

    private static void CopyBitmapRows(
        BitmapData bitmapData,
        byte[] destination,
        int destinationStride,
        int height)
    {
        int sourceStride = Math.Abs(bitmapData.Stride);

        for (int y = 0; y < height; y++)
        {
            nint sourceRow = bitmapData.Stride >= 0
                ? bitmapData.Scan0 + y * bitmapData.Stride
                : bitmapData.Scan0 + (height - 1 - y) * sourceStride;

            Marshal.Copy(sourceRow, destination, y * destinationStride, destinationStride);
        }
    }

    private static ScreenCaptureBounds ToBounds(DrawingRectangle rectangle)
    {
        return new ScreenCaptureBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint hdcDestination,
        int xDestination,
        int yDestination,
        int width,
        int height,
        nint hdcSource,
        int xSource,
        int ySource,
        int rasterOperation);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetDC(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(nint windowHandle, nint deviceContext);
}
