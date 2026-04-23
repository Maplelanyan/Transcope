using System.Windows;

namespace Transcope.Capture;

public interface IScreenCaptureService
{
    ValueTask<ScreenCaptureResult?> CaptureRegionAsync(
        Window? owner = null,
        CancellationToken cancellationToken = default);

    ScreenCaptureResult Capture(ScreenCaptureBounds bounds);

    ScreenCaptureResult CapturePrimaryScreen();

    ScreenCaptureResult CaptureVirtualScreen();
}
