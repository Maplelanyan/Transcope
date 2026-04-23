using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Transcope.Ocr;

internal sealed class PaddleOcrEngine : IOcrEngineAdapter
{
    private const string PythonExecutableEnvironmentVariable = "PADDLEOCR_PYTHON";
    private const int AvailabilityTimeoutMilliseconds = 45_000;
    private const int RecognitionTimeoutMilliseconds = 120_000;
    private const string BridgeScriptRelativePath = "PaddleOcr\\paddle_ocr_bridge.py";

    private readonly Dictionary<string, bool> availabilityCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Dictionary<string, string> LanguageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh"] = "ch",
        ["zh-CN"] = "ch",
        ["zh-Hans"] = "ch",
        ["zh-Hans-CN"] = "ch",
        ["zh-Hant"] = "chinese_cht",
        ["zh-Hant-HK"] = "chinese_cht",
        ["zh-Hant-TW"] = "chinese_cht",
        ["zh-HK"] = "chinese_cht",
        ["zh-TW"] = "chinese_cht",
        ["en"] = "en",
        ["en-US"] = "en",
        ["en-GB"] = "en",
        ["ja"] = "japan",
        ["ja-JP"] = "japan",
        ["ko"] = "korean",
        ["ko-KR"] = "korean",
        ["fr"] = "french",
        ["fr-FR"] = "french",
        ["de"] = "german",
        ["de-DE"] = "german",
        ["es"] = "spanish",
        ["es-ES"] = "spanish",
        ["ru"] = "russian",
        ["ru-RU"] = "russian"
    };

    public OcrEngineKind EngineKind => OcrEngineKind.PaddleOcr;

    public async ValueTask<bool> IsAvailableAsync(
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        string language = ResolveLanguage(options.LanguageTag);
        if (availabilityCache.TryGetValue(language, out bool cachedAvailability))
        {
            return cachedAvailability;
        }

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(AvailabilityTimeoutMilliseconds);

            PaddleProcessResult result = await RunBridgeAsync(
                ["--check", "--lang", language],
                timeout.Token).ConfigureAwait(false);

            bool isAvailable = result.ExitCode == 0;
            availabilityCache[language] = isAvailable;
            return isAvailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            availabilityCache[language] = false;
            return false;
        }
    }

    public async ValueTask<OcrRecognitionResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        string imagePath = Path.Combine(Path.GetTempPath(), $"transcope-paddleocr-{Guid.NewGuid():N}.png");

        try
        {
            byte[] pngBytes = await EncodeBitmapAsPngAsync(bitmap, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(imagePath, pngBytes, cancellationToken).ConfigureAwait(false);

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RecognitionTimeoutMilliseconds);

            PaddleProcessResult processResult = await RunBridgeAsync(
                ["--image", imagePath, "--lang", ResolveLanguage(options.LanguageTag)],
                timeout.Token).ConfigureAwait(false);

            if (processResult.ExitCode != 0)
            {
                string message = string.IsNullOrWhiteSpace(processResult.Error)
                    ? processResult.Output
                    : processResult.Error;

                throw new OcrEngineUnavailableException(
                    EngineKind,
                    string.IsNullOrWhiteSpace(message)
                        ? "PaddleOCR failed without diagnostic output."
                        : message.Trim());
            }

            PaddleOcrBridgeResponse response;
            try
            {
                response = DeserializeBridgeResponse(processResult.Output);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                string details = CreatePaddleDiagnosticMessage(processResult, ex.Message);
                throw new OcrEngineUnavailableException(EngineKind, details);
            }

            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                throw new OcrEngineUnavailableException(EngineKind, response.Error);
            }

            OcrRecognizedLine[] lines = response.Items
                .Where(static item => !string.IsNullOrWhiteSpace(item.Text) && item.Box.Length >= 4)
                .Select(ToLine)
                .ToArray();

            string text = string.Join(Environment.NewLine, lines.Select(static line => line.Text));

            return new OcrRecognitionResult(
                OcrEngineKind.PaddleOcr,
                text,
                lines,
                ResolveLanguage(options.LanguageTag),
                TextAngle: null);
        }
        finally
        {
            TryDeleteFile(imagePath);
        }
    }

    private static OcrRecognizedLine ToLine(PaddleOcrBridgeItem item)
    {
        OcrTextBoundary boundary = ToBoundary(item.Box);
        OcrRecognizedWord word = new(item.Text.Trim(), boundary, item.Confidence);

        return new OcrRecognizedLine(
            item.Text.Trim(),
            boundary,
            [word]);
    }

    private static PaddleOcrBridgeResponse DeserializeBridgeResponse(string output)
    {
        string json = ExtractJsonObject(output);
        return JsonSerializer.Deserialize<PaddleOcrBridgeResponse>(
            json,
            JsonOptions) ?? throw new InvalidOperationException("PaddleOCR returned an empty response.");
    }

    private static string ExtractJsonObject(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("PaddleOCR returned no JSON output.");
        }

        string trimmed = output.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        int start = trimmed.LastIndexOf('{');
        int end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        throw new InvalidOperationException("PaddleOCR returned output without a JSON payload.");
    }

    private static OcrTextBoundary ToBoundary(double[][] box)
    {
        return new OcrTextBoundary(
            ToPoint(box[0]),
            ToPoint(box[1]),
            ToPoint(box[2]),
            ToPoint(box[3]));
    }

    private static OcrPoint ToPoint(double[] point)
    {
        if (point.Length < 2)
        {
            return new OcrPoint(0, 0);
        }

        return new OcrPoint(point[0], point[1]);
    }

    private static async Task<PaddleProcessResult> RunBridgeAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        string pythonExecutable = ResolvePythonExecutable();
        string scriptPath = ResolveBridgeScriptPath();

        ProcessStartInfo startInfo = new()
        {
            FileName = pythonExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True";
        startInfo.Environment["GLOG_minloglevel"] = "2";
        startInfo.Environment["FLAGS_use_mkldnn"] = "0";
        startInfo.Environment["FLAGS_enable_pir_api"] = "0";

        startInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start PaddleOCR Python process.");

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);

        return new PaddleProcessResult(process.ExitCode, output, error);
    }

    private static string CreatePaddleDiagnosticMessage(
        PaddleProcessResult processResult,
        string fallbackMessage)
    {
        if (!string.IsNullOrWhiteSpace(processResult.Output))
        {
            return $"{fallbackMessage} Output: {Truncate(processResult.Output.Trim(), 800)}";
        }

        string filteredError = FilterPaddleLog(processResult.Error);
        return string.IsNullOrWhiteSpace(filteredError)
            ? fallbackMessage
            : $"{fallbackMessage} PaddleOCR log: {filteredError}";
    }

    private static string FilterPaddleLog(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string[] noisyFragments =
        [
            "No ccache found",
            "Creating model:",
            "Model files already exist",
            "WARNING: Logging before InitGoogleLogging",
            "oneDNN"
        ];

        string[] lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !noisyFragments.Any(fragment => line.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return Truncate(string.Join(Environment.NewLine, lines), 800);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private static string ResolvePythonExecutable()
    {
        string? configuredPython =
            Environment.GetEnvironmentVariable(PythonExecutableEnvironmentVariable) ??
            Environment.GetEnvironmentVariable(PythonExecutableEnvironmentVariable, EnvironmentVariableTarget.User);

        if (!string.IsNullOrWhiteSpace(configuredPython))
        {
            string candidate = configuredPython.Trim().Trim('"');
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string? localVirtualEnvironmentPython = FindAncestorFile(".venv-paddleocr\\Scripts\\python.exe");
        return localVirtualEnvironmentPython ?? "python";
    }

    private static string ResolveBridgeScriptPath()
    {
        string baseDirectoryCandidate = Path.Combine(AppContext.BaseDirectory, BridgeScriptRelativePath);
        if (File.Exists(baseDirectoryCandidate))
        {
            return baseDirectoryCandidate;
        }

        string referencedProjectCandidate = Path.Combine(
            AppContext.BaseDirectory,
            "Transcope.Ocr",
            BridgeScriptRelativePath);
        if (File.Exists(referencedProjectCandidate))
        {
            return referencedProjectCandidate;
        }

        string currentDirectoryCandidate = Path.Combine(Environment.CurrentDirectory, BridgeScriptRelativePath);
        if (File.Exists(currentDirectoryCandidate))
        {
            return currentDirectoryCandidate;
        }

        throw new FileNotFoundException("PaddleOCR bridge script was not found.", baseDirectoryCandidate);
    }

    private static string? FindAncestorFile(string relativePath)
    {
        foreach (string startDirectory in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            DirectoryInfo? directory = new(startDirectory);
            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static string ResolveLanguage(string? requestedLanguage)
    {
        if (string.IsNullOrWhiteSpace(requestedLanguage))
        {
            return "ch";
        }

        string trimmed = requestedLanguage.Trim();
        if (LanguageMap.TryGetValue(trimmed, out string? mappedLanguage))
        {
            return mappedLanguage;
        }

        int separatorIndex = trimmed.IndexOf('-');
        if (separatorIndex > 0 &&
            LanguageMap.TryGetValue(trimmed[..separatorIndex], out mappedLanguage))
        {
            return mappedLanguage;
        }

        if (trimmed.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                ? "chinese_cht"
                : "ch";
        }

        return trimmed;
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record PaddleProcessResult(int ExitCode, string Output, string Error);

    private sealed record PaddleOcrBridgeResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<PaddleOcrBridgeItem> Items,
        [property: JsonPropertyName("error")] string? Error = null);

    private sealed record PaddleOcrBridgeItem(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("confidence")] float? Confidence,
        [property: JsonPropertyName("box")] double[][] Box);
}
