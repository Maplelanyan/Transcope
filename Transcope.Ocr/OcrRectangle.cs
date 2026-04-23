namespace Transcope.Ocr;

public readonly record struct OcrRectangle(double X, double Y, double Width, double Height)
{
    public static OcrRectangle FromPoints(IEnumerable<OcrPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        using IEnumerator<OcrPoint> enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return default;
        }

        double minX = enumerator.Current.X;
        double minY = enumerator.Current.Y;
        double maxX = minX;
        double maxY = minY;

        while (enumerator.MoveNext())
        {
            OcrPoint point = enumerator.Current;
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new OcrRectangle(minX, minY, maxX - minX, maxY - minY);
    }
}
