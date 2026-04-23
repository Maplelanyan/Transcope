using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;

namespace Transcope.Capture;

public sealed class ScreenCaptureResult : IDisposable
{
    private bool disposed;

    internal ScreenCaptureResult(
        ScreenCaptureBounds bounds,
        SoftwareBitmap softwareBitmap,
        BitmapSource preview)
    {
        Bounds = bounds;
        SoftwareBitmap = softwareBitmap;
        Preview = preview;
    }

    public ScreenCaptureBounds Bounds { get; }

    public SoftwareBitmap SoftwareBitmap { get; }

    public BitmapSource Preview { get; }

    public int Width => Bounds.Width;

    public int Height => Bounds.Height;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        SoftwareBitmap.Dispose();
        disposed = true;
    }
}
