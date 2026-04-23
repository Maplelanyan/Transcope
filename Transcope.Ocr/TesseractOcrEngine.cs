using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Tesseract;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Transcope.Ocr;

internal sealed class TesseractOcrEngine : IOcrEngineAdapter
{
    private const string DefaultLanguage = "eng";
    private const string TessdataDirectoryName = "tessdata";
    private const float PreferredPreprocessScale = 2.0f;
    private const int MaxPreprocessedImageDimension = 3200;
    private const int TesseractInputDpi = 300;
    private const float MinimumPreprocessedAverageConfidence = 35;

    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = "deu",
        ["de-DE"] = "deu",
        ["en"] = "eng",
        ["en-GB"] = "eng",
        ["en-US"] = "eng",
        ["es"] = "spa",
        ["es-ES"] = "spa",
        ["fr"] = "fra",
        ["fr-FR"] = "fra",
        ["it"] = "ita",
        ["it-IT"] = "ita",
        ["ja"] = "jpn",
        ["ja-JP"] = "jpn",
        ["ko"] = "kor",
        ["ko-KR"] = "kor",
        ["pt"] = "por",
        ["pt-BR"] = "por",
        ["pt-PT"] = "por",
        ["ru"] = "rus",
        ["ru-RU"] = "rus",
        ["zh"] = "chi_sim",
        ["zh-CN"] = "chi_sim",
        ["zh-Hans"] = "chi_sim",
        ["zh-Hans-CN"] = "chi_sim",
        ["zh-Hant"] = "chi_tra",
        ["zh-Hant-HK"] = "chi_tra",
        ["zh-Hant-TW"] = "chi_tra",
        ["zh-HK"] = "chi_tra",
        ["zh-MO"] = "chi_tra",
        ["zh-TW"] = "chi_tra"
    };

    public OcrEngineKind EngineKind => OcrEngineKind.Tesseract;

    public async ValueTask<bool> IsAvailableAsync(
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        string language = ResolveLanguage(options.LanguageTag) ?? DefaultLanguage;

        return await Task.Run(() =>
        {
            try
            {
                using TesseractEngine engine = CreateEngine(language);
                return true;
            }
            catch (Exception ex) when (IsEngineUnavailableException(ex))
            {
                return false;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OcrRecognitionResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        string language = ResolveLanguage(options.LanguageTag) ?? DefaultLanguage;
        byte[] imageBytes = await EncodeBitmapAsPngAsync(bitmap, cancellationToken).ConfigureAwait(false);

        return await Task.Run(() => RecognizePngBytes(imageBytes, language), cancellationToken)
            .ConfigureAwait(false);
    }

    private static OcrRecognitionResult RecognizePngBytes(byte[] imageBytes, string language)
    {
        using TesseractEngine engine = CreateEngineOrThrow(language);
        engine.SetVariable("user_defined_dpi", TesseractInputDpi);

        using Pix pix = Pix.LoadFromMemory(imageBytes);
        using PreprocessedPix preprocessedPix = PreprocessPix(pix);

        OcrRecognitionResult preprocessedResult = RecognizePix(
            engine,
            preprocessedPix.Image,
            language,
            preprocessedPix.CoordinateScale);

        if (!ShouldRetryOriginalImage(preprocessedResult))
        {
            return preprocessedResult;
        }

        OcrRecognitionResult originalResult = RecognizePix(engine, pix, language, coordinateScale: 1);
        return ScoreResult(originalResult) > ScoreResult(preprocessedResult)
            ? originalResult
            : preprocessedResult;
    }

    private static OcrRecognitionResult RecognizePix(
        TesseractEngine engine,
        Pix pix,
        string language,
        double coordinateScale)
    {
        using Page page = engine.Process(pix, PageSegMode.Auto);

        string tsv = page.GetTsvText(0);
        return ParseTsv(tsv, language, coordinateScale);
    }

    private static PreprocessedPix PreprocessPix(Pix source)
    {
        List<Pix> ownedPixes = [];

        try
        {
            Pix current = source;
            double coordinateScale = 1;

            float scaleFactor = ResolvePreprocessScale(source.Width, source.Height);
            if (scaleFactor > 1.01f)
            {
                current = AddOwned(ownedPixes, source.Scale(scaleFactor, scaleFactor));
                coordinateScale = 1 / scaleFactor;
            }

            Pix grayscale = current.Depth == 8 && current.Colormap is null
                ? current
                : AddOwned(ownedPixes, ConvertToGrayscale(current));

            Pix binarized = AddOwned(ownedPixes, ApplyLocalContrastBinarization(grayscale));
            binarized.XRes = TesseractInputDpi;
            binarized.YRes = TesseractInputDpi;

            return new PreprocessedPix(binarized, coordinateScale, ownedPixes);
        }
        catch
        {
            foreach (Pix ownedPix in ownedPixes)
            {
                ownedPix.Dispose();
            }

            throw;
        }
    }

    private static Pix AddOwned(List<Pix> ownedPixes, Pix pix)
    {
        ownedPixes.Add(pix);
        return pix;
    }

    private static float ResolvePreprocessScale(int width, int height)
    {
        int largestDimension = Math.Max(width, height);
        if (largestDimension <= 0)
        {
            return 1;
        }

        float cappedScale = MaxPreprocessedImageDimension / (float)largestDimension;
        return Math.Max(1, Math.Min(PreferredPreprocessScale, cappedScale));
    }

    private static Pix ConvertToGrayscale(Pix pix)
    {
        if (pix.Depth is 24 or 32)
        {
            return pix.ConvertRGBToGray();
        }

        return pix.ConvertTo8(cmapflag: 0);
    }

    private static Pix ApplyLocalContrastBinarization(Pix grayscale)
    {
        try
        {
            return grayscale.BinarizeSauvolaTiled(whsize: 25, factor: 0.35f, nx: 1, ny: 1);
        }
        catch (TesseractException)
        {
            return grayscale.BinarizeOtsuAdaptiveThreshold(
                sx: 200,
                sy: 200,
                smoothx: 0,
                smoothy: 0,
                scorefract: 0.1f);
        }
    }

    private static bool ShouldRetryOriginalImage(OcrRecognitionResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return true;
        }

        float? averageConfidence = AverageWordConfidence(result);
        return averageConfidence.HasValue &&
            averageConfidence.Value < MinimumPreprocessedAverageConfidence;
    }

    private static double ScoreResult(OcrRecognitionResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            return 0;
        }

        int wordCount = result.Lines.Sum(static line => line.Words.Count);
        float confidence = AverageWordConfidence(result) ?? MinimumPreprocessedAverageConfidence;

        return confidence * 2 +
            Math.Min(wordCount, 120) +
            Math.Min(result.Text.Length, 600) / 12.0;
    }

    private static float? AverageWordConfidence(OcrRecognitionResult result)
    {
        float[] confidences = result.Lines
            .SelectMany(static line => line.Words)
            .Select(static word => word.Confidence)
            .Where(static confidence => confidence.HasValue)
            .Select(static confidence => confidence.GetValueOrDefault())
            .ToArray();

        return confidences.Length == 0
            ? null
            : confidences.Average();
    }

    private static TesseractEngine CreateEngineOrThrow(string language)
    {
        try
        {
            return CreateEngine(language);
        }
        catch (Exception ex) when (IsEngineUnavailableException(ex))
        {
            throw new OcrEngineUnavailableException(
                OcrEngineKind.Tesseract,
                $"Tesseract OCR is not available for language '{language}'. Add traineddata files under a tessdata directory, or set TESSDATA_PREFIX. {ex.Message}");
        }
    }

    private static TesseractEngine CreateEngine(string language)
    {
        string tessdataPath = ResolveTessdataPath(language);
        return new TesseractEngine(tessdataPath, language, EngineMode.Default);
    }

    private static string ResolveTessdataPath(string language)
    {
        string? firstExistingCandidate = null;

        foreach (string candidate in EnumerateTessdataPathCandidates())
        {
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            firstExistingCandidate ??= candidate;
            if (HasLanguageData(candidate, language))
            {
                return candidate;
            }
        }

        return firstExistingCandidate ?? Path.Combine(AppContext.BaseDirectory, TessdataDirectoryName);
    }

    private static IEnumerable<string> EnumerateTessdataPathCandidates()
    {
        List<string> candidates = [];
        HashSet<string> seenCandidates = new(StringComparer.OrdinalIgnoreCase);

        AddExpandedCandidate(Environment.GetEnvironmentVariable("TESSDATA_PREFIX"), candidates, seenCandidates);
        AddCandidate(AppContext.BaseDirectory, candidates, seenCandidates);
        AddCandidate(Path.Combine(AppContext.BaseDirectory, TessdataDirectoryName), candidates, seenCandidates);
        AddCandidate(Environment.CurrentDirectory, candidates, seenCandidates);
        AddCandidate(Path.Combine(Environment.CurrentDirectory, TessdataDirectoryName), candidates, seenCandidates);

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string directory = segment.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                AddCandidate(Path.Combine(directory, TessdataDirectoryName), candidates, seenCandidates);
            }
        }

        AddProgramFilesCandidate("ProgramW6432", candidates, seenCandidates);
        AddProgramFilesCandidate("ProgramFiles", candidates, seenCandidates);
        AddProgramFilesCandidate("ProgramFiles(x86)", candidates, seenCandidates);

        return candidates;
    }

    private static void AddProgramFilesCandidate(
        string environmentVariable,
        List<string> candidates,
        HashSet<string> seenCandidates)
    {
        string? programFiles = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return;
        }

        AddCandidate(
            Path.Combine(programFiles, "Tesseract-OCR", TessdataDirectoryName),
            candidates,
            seenCandidates);
    }

    private static void AddExpandedCandidate(
        string? path,
        List<string> candidates,
        HashSet<string> seenCandidates)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string trimmed = path.Trim().Trim('"');
        AddCandidate(trimmed, candidates, seenCandidates);

        if (!string.Equals(
                Path.GetFileName(trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                TessdataDirectoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(Path.Combine(trimmed, TessdataDirectoryName), candidates, seenCandidates);
        }
    }

    private static void AddCandidate(
        string path,
        List<string> candidates,
        HashSet<string> seenCandidates)
    {
        if (!string.IsNullOrWhiteSpace(path) && seenCandidates.Add(path))
        {
            candidates.Add(path);
        }
    }

    private static bool IsEngineUnavailableException(Exception ex)
    {
        return ex is TesseractException or
            LoadLibraryException or
            DllNotFoundException or
            BadImageFormatException or
            FileNotFoundException or
            DirectoryNotFoundException or
            UnauthorizedAccessException or
            InvalidOperationException or
            TypeInitializationException;
    }

    private static bool HasLanguageData(string tessdataPath, string language)
    {
        string[] languages = language.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return languages.Length > 0 &&
            languages.All(lang => File.Exists(Path.Combine(tessdataPath, $"{lang}.traineddata")));
    }

    private static async Task<byte[]> EncodeBitmapAsPngAsync(
        SoftwareBitmap bitmap,
        CancellationToken cancellationToken)
    {
        SoftwareBitmap encodingBitmap = bitmap;
        bool disposeEncodingBitmap = false;

        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            encodingBitmap = SoftwareBitmap.Convert(
                bitmap,
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
            disposeEncodingBitmap = true;
        }

        try
        {
            using MemoryStream memoryStream = new();
            using IRandomAccessStream randomAccessStream = memoryStream.AsRandomAccessStream();

            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(
                    BitmapEncoder.PngEncoderId,
                    randomAccessStream)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            encoder.SetSoftwareBitmap(encodingBitmap);
            await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            await randomAccessStream.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);

            return memoryStream.ToArray();
        }
        finally
        {
            if (disposeEncodingBitmap)
            {
                encodingBitmap.Dispose();
            }
        }
    }

    private static string? ResolveLanguage(string? requestedLanguage)
    {
        if (string.IsNullOrWhiteSpace(requestedLanguage))
        {
            return null;
        }

        string trimmed = requestedLanguage.Trim();
        if (trimmed.Contains('_', StringComparison.Ordinal) ||
            trimmed.Contains('+', StringComparison.Ordinal))
        {
            return trimmed;
        }

        if (LanguageMap.TryGetValue(trimmed, out string? mappedLanguage))
        {
            return mappedLanguage;
        }

        if (trimmed.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("-MO", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                ? "chi_tra"
                : "chi_sim";
        }

        int separatorIndex = trimmed.IndexOf('-');
        if (separatorIndex > 0 &&
            LanguageMap.TryGetValue(trimmed[..separatorIndex], out mappedLanguage))
        {
            return mappedLanguage;
        }

        return trimmed;
    }

    private static OcrRecognitionResult ParseTsv(
        string tsv,
        string language,
        double coordinateScale = 1)
    {
        List<TesseractLineAccumulator> orderedLines = [];
        Dictionary<TesseractLineKey, TesseractLineAccumulator> lineMap = [];

        using StringReader reader = new(tsv);
        while (reader.ReadLine() is { } rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine) ||
                rawLine.StartsWith("level\t", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryParseRow(rawLine, out TesseractTsvRow row))
            {
                continue;
            }

            if (row.Level is not 4 and not 5)
            {
                continue;
            }

            TesseractLineKey key = new(
                row.PageNumber,
                row.BlockNumber,
                row.ParagraphNumber,
                row.LineNumber);

            if (!lineMap.TryGetValue(key, out TesseractLineAccumulator? accumulator))
            {
                accumulator = new TesseractLineAccumulator();
                lineMap.Add(key, accumulator);
                orderedLines.Add(accumulator);
            }

            if (row.Level == 4)
            {
                accumulator.Boundary = ToBoundary(row.Left, row.Top, row.Width, row.Height, coordinateScale);
                if (!string.IsNullOrWhiteSpace(row.Text))
                {
                    accumulator.Text = row.Text;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Text))
            {
                continue;
            }

            accumulator.Words.Add(new OcrRecognizedWord(
                row.Text,
                ToBoundary(row.Left, row.Top, row.Width, row.Height, coordinateScale),
                row.Confidence));
        }

        OcrRecognizedLine[] lines = orderedLines
            .Select(static accumulator => accumulator.ToRecognizedLine())
            .Where(static line => !string.IsNullOrWhiteSpace(line.Text))
            .ToArray();

        string text = string.Join(Environment.NewLine, lines.Select(static line => line.Text));

        return new OcrRecognitionResult(
            OcrEngineKind.Tesseract,
            text,
            lines,
            language,
            TextAngle: null);
    }

    private static bool TryParseRow(string line, out TesseractTsvRow row)
    {
        string[] columns = line.Split('\t');
        if (columns.Length < 11)
        {
            row = default;
            return false;
        }

        string text = columns.Length > 11
            ? string.Join('\t', columns.Skip(11))
            : string.Empty;

        if (!int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) ||
            !int.TryParse(columns[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pageNumber) ||
            !int.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int blockNumber) ||
            !int.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int paragraphNumber) ||
            !int.TryParse(columns[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineNumber) ||
            !int.TryParse(columns[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int wordNumber) ||
            !int.TryParse(columns[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int left) ||
            !int.TryParse(columns[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int top) ||
            !int.TryParse(columns[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(columns[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
        {
            row = default;
            return false;
        }

        float? confidence = null;
        if (float.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedConfidence) &&
            parsedConfidence >= 0)
        {
            confidence = parsedConfidence;
        }

        row = new TesseractTsvRow(
            level,
            pageNumber,
            blockNumber,
            paragraphNumber,
            lineNumber,
            wordNumber,
            left,
            top,
            width,
            height,
            confidence,
            text);
        return true;
    }

    private static OcrTextBoundary ToBoundary(
        int left,
        int top,
        int width,
        int height,
        double coordinateScale)
    {
        return OcrTextBoundary.FromRect(new Windows.Foundation.Rect(
            left * coordinateScale,
            top * coordinateScale,
            width * coordinateScale,
            height * coordinateScale));
    }

    private readonly record struct TesseractLineKey(
        int PageNumber,
        int BlockNumber,
        int ParagraphNumber,
        int LineNumber);

    private readonly record struct TesseractTsvRow(
        int Level,
        int PageNumber,
        int BlockNumber,
        int ParagraphNumber,
        int LineNumber,
        int WordNumber,
        int Left,
        int Top,
        int Width,
        int Height,
        float? Confidence,
        string Text);

    private sealed class PreprocessedPix : IDisposable
    {
        private readonly IReadOnlyList<Pix> ownedPixes;

        public PreprocessedPix(Pix image, double coordinateScale, IReadOnlyList<Pix> ownedPixes)
        {
            Image = image;
            CoordinateScale = coordinateScale;
            this.ownedPixes = ownedPixes;
        }

        public Pix Image { get; }

        public double CoordinateScale { get; }

        public void Dispose()
        {
            foreach (Pix ownedPix in ownedPixes)
            {
                ownedPix.Dispose();
            }
        }
    }

    private sealed class TesseractLineAccumulator
    {
        public OcrTextBoundary? Boundary { get; set; }

        public string? Text { get; set; }

        public List<OcrRecognizedWord> Words { get; } = [];

        public OcrRecognizedLine ToRecognizedLine()
        {
            string lineText = !string.IsNullOrWhiteSpace(Text)
                ? Text
                : string.Join(' ', Words.Select(static word => word.Text));

            OcrTextBoundary? boundary = Boundary;
            if (boundary is null && Words.Count > 0)
            {
                OcrRectangle rectangle = OcrRectangle.FromPoints(
                    Words.SelectMany(static word => new[]
                    {
                        word.Boundary.TopLeft,
                        word.Boundary.TopRight,
                        word.Boundary.BottomRight,
                        word.Boundary.BottomLeft
                    }));

                boundary = OcrTextBoundary.FromRect(new Windows.Foundation.Rect(
                    rectangle.X,
                    rectangle.Y,
                    rectangle.Width,
                    rectangle.Height));
            }

            return new OcrRecognizedLine(
                lineText,
                boundary,
                Words.ToArray());
        }
    }
}
