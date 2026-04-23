namespace Transcope.Capture;

public readonly record struct ScreenCaptureBounds(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
