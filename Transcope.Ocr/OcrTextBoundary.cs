using Windows.Foundation;

namespace Transcope.Ocr;

public sealed record OcrTextBoundary(
    OcrPoint TopLeft,
    OcrPoint TopRight,
    OcrPoint BottomRight,
    OcrPoint BottomLeft)
{
    public OcrRectangle AxisAlignedBoundingBox =>
        OcrRectangle.FromPoints([TopLeft, TopRight, BottomRight, BottomLeft]);

    public static OcrTextBoundary FromRect(Rect rect)
    {
        OcrPoint topLeft = new(rect.X, rect.Y);
        OcrPoint topRight = new(rect.X + rect.Width, rect.Y);
        OcrPoint bottomRight = new(rect.X + rect.Width, rect.Y + rect.Height);
        OcrPoint bottomLeft = new(rect.X, rect.Y + rect.Height);

        return new OcrTextBoundary(topLeft, topRight, bottomRight, bottomLeft);
    }
}
