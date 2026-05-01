using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Transcope.Ocr;

internal sealed class PaddleOcrEngine : IOcrEngineAdapter
{
    private const string PythonExecutableEnvironmentVariable = "PADDLEOCR_PYTHON";
    private const string GpuPythonExecutableEnvironmentVariable = "PADDLEOCR_GPU_PYTHON";
    private const int AvailabilityTimeoutMilliseconds = 45_000;
    private const int RecognitionTimeoutMilliseconds = 120_000;
    private const string BridgeScriptRelativePath = "PaddleOcr\\paddle_ocr_bridge.py";

    private static readonly ConcurrentDictionary<string, PaddleServerClient> ServerClients =
        new(StringComparer.OrdinalIgnoreCase);

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

    static PaddleOcrEngine()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (PaddleServerClient client in ServerClients.Values)
            {
                client.Dispose();
            }
        };
    }

    public OcrEngineKind EngineKind => OcrEngineKind.PaddleOcr;

    public async ValueTask<bool> IsAvailableAsync(
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        string language = ResolveLanguage(options.LanguageTag);
        string clientKey = CreateClientKey(language, options.PaddleRuntimeMode);

        if (availabilityCache.TryGetValue(clientKey, out bool cachedAvailability))
        {
            return cachedAvailability;
        }

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(AvailabilityTimeoutMilliseconds);

            PaddleServerClient client = await GetOrCreateClientAsync(
                language,
                options.PaddleRuntimeMode,
                timeout.Token).ConfigureAwait(false);
            bool isAvailable = await client.CheckAsync(timeout.Token).ConfigureAwait(false);
            availabilityCache[clientKey] = isAvailable;
            return isAvailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            availabilityCache[clientKey] = false;
            return false;
        }
    }

    public async ValueTask<OcrRecognitionResult> RecognizeAsync(
        SoftwareBitmap bitmap,
        OcrRecognitionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        string language = ResolveLanguage(options.LanguageTag);
        string imagePath = Path.Combine(Path.GetTempPath(), $"transcope-paddleocr-{Guid.NewGuid():N}.png");

        try
        {
            byte[] pngBytes = await EncodeBitmapAsPngAsync(bitmap, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(imagePath, pngBytes, cancellationToken).ConfigureAwait(false);

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RecognitionTimeoutMilliseconds);

            PaddleServerClient client = await GetOrCreateClientAsync(
                language,
                options.PaddleRuntimeMode,
                timeout.Token).ConfigureAwait(false);
            PaddleServerResponse response = await client.RecognizeAsync(imagePath, timeout.Token).ConfigureAwait(false);

            if (!response.Ok)
            {
                throw new OcrEngineUnavailableException(
                    EngineKind,
                    string.IsNullOrWhiteSpace(response.Error)
                        ? "PaddleOCR failed without diagnostic output."
                        : response.Error.Trim());
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
                language,
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

    private static async Task<PaddleServerClient> GetOrCreateClientAsync(
        string language,
        PaddleRuntimeMode runtimeMode,
        CancellationToken cancellationToken)
    {
        string clientKey = CreateClientKey(language, runtimeMode);
        PaddleServerClient client = ServerClients.GetOrAdd(
            clientKey,
            _ => new PaddleServerClient(language, runtimeMode));

        try
        {
            await client.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            if (ServerClients.TryRemove(new KeyValuePair<string, PaddleServerClient>(clientKey, client)))
            {
                client.Dispose();
            }

            throw;
        }
    }

    private static string CreateClientKey(string language, PaddleRuntimeMode runtimeMode)
    {
        return $"{language}|{runtimeMode}";
    }

    private static string ResolvePythonExecutable(PaddleRuntimeMode runtimeMode)
    {
        if (runtimeMode == PaddleRuntimeMode.Gpu)
        {
            string? configuredGpuPython = ResolveConfiguredPython(GpuPythonExecutableEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredGpuPython))
            {
                return configuredGpuPython;
            }

            string? localGpuVirtualEnvironmentPython = FindAncestorFile(".venv-paddleocr-gpu\\Scripts\\python.exe");
            if (!string.IsNullOrWhiteSpace(localGpuVirtualEnvironmentPython))
            {
                return localGpuVirtualEnvironmentPython;
            }

            throw new FileNotFoundException(
                "PaddleOCR GPU Python was not found. Set PADDLEOCR_GPU_PYTHON or create .venv-paddleocr-gpu.");
        }

        string? configuredPython = ResolveConfiguredPython(PythonExecutableEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPython))
        {
            return configuredPython;
        }

        string? localVirtualEnvironmentPython = FindAncestorFile(".venv-paddleocr\\Scripts\\python.exe");
        return localVirtualEnvironmentPython ?? "python";
    }

    private static string? ResolveConfiguredPython(string environmentVariable)
    {
        string? configuredPython =
            Environment.GetEnvironmentVariable(environmentVariable) ??
            Environment.GetEnvironmentVariable(environmentVariable, EnvironmentVariableTarget.User);

        if (!string.IsNullOrWhiteSpace(configuredPython))
        {
            string candidate = configuredPython.Trim().Trim('"');
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string ResolveBridgeDevice(PaddleRuntimeMode runtimeMode)
    {
        return runtimeMode == PaddleRuntimeMode.Gpu ? "gpu:0" : "cpu";
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

    private sealed class PaddleServerClient : IDisposable
    {
        private readonly string language;
        private readonly PaddleRuntimeMode runtimeMode;
        private readonly SemaphoreSlim gate = new(1, 1);
        private readonly SemaphoreSlim startGate = new(1, 1);
        private Process? process;
        private StreamWriter? standardInput;
        private StreamReader? standardOutput;
        private StreamReader? standardError;
        private bool disposed;

        public PaddleServerClient(string language, PaddleRuntimeMode runtimeMode)
        {
            this.language = language;
            this.runtimeMode = runtimeMode;
        }

        public async Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            if (process is { HasExited: false })
            {
                return;
            }

            await startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (process is { HasExited: false })
                {
                    return;
                }

                DisposeProcessOnly();

                ProcessStartInfo startInfo = new()
                {
                    FileName = ResolvePythonExecutable(runtimeMode),
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardInputEncoding = Encoding.UTF8,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                startInfo.Environment["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True";
                startInfo.Environment["GLOG_minloglevel"] = "2";
                startInfo.Environment["FLAGS_use_mkldnn"] = "0";
                startInfo.Environment["FLAGS_enable_pir_api"] = "0";
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
                startInfo.Environment["PYTHONUTF8"] = "1";

                startInfo.ArgumentList.Add(ResolveBridgeScriptPath());
                startInfo.ArgumentList.Add("--server");
                startInfo.ArgumentList.Add("--lang");
                startInfo.ArgumentList.Add(language);
                startInfo.ArgumentList.Add("--device");
                startInfo.ArgumentList.Add(ResolveBridgeDevice(runtimeMode));

                process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start PaddleOCR server process.");

                standardInput = process.StandardInput;
                standardOutput = process.StandardOutput;
                standardError = process.StandardError;

                string? readyLine = await standardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                PaddleServerResponse ready = DeserializeResponse(readyLine);
                if (!ready.Ok)
                {
                    string stderr = await standardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(ready.Error)
                            ? FilterPaddleLog(stderr)
                            : ready.Error);
                }
            }
            finally
            {
                startGate.Release();
            }
        }

        public async Task<bool> CheckAsync(CancellationToken cancellationToken)
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            return process is { HasExited: false };
        }

        public async Task<PaddleServerResponse> RecognizeAsync(string imagePath, CancellationToken cancellationToken)
        {
            await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (process is null || process.HasExited || standardInput is null || standardOutput is null)
                {
                    throw new InvalidOperationException("PaddleOCR server process is not running.");
                }

                string payload = JsonSerializer.Serialize(
                    new PaddleServerRequest("recognize", imagePath),
                    JsonOptions);

                await standardInput.WriteLineAsync(payload).ConfigureAwait(false);
                await standardInput.FlushAsync().ConfigureAwait(false);

                string? responseLine = await standardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                PaddleServerResponse response = DeserializeResponse(responseLine);
                if (response.Ok)
                {
                    return response;
                }

                string stderr = await TryReadErrorTailAsync(cancellationToken).ConfigureAwait(false);
                return response with
                {
                    Error = string.IsNullOrWhiteSpace(response.Error)
                        ? FilterPaddleLog(stderr)
                        : response.Error
                };
            }
            catch
            {
                DisposeProcessOnly();
                throw;
            }
            finally
            {
                gate.Release();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DisposeProcessOnly();
            gate.Dispose();
            startGate.Dispose();
        }

        private async Task<string> TryReadErrorTailAsync(CancellationToken cancellationToken)
        {
            if (standardError is null || process is null || !process.HasExited)
            {
                return string.Empty;
            }

            try
            {
                return await standardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void DisposeProcessOnly()
        {
            try
            {
                if (process is { HasExited: false } && standardInput is not null)
                {
                    string payload = JsonSerializer.Serialize(new PaddleServerRequest("exit", null), JsonOptions);
                    standardInput.WriteLine(payload);
                    standardInput.Flush();
                }
            }
            catch
            {
            }

            try
            {
                if (process is { HasExited: false })
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
            }
            catch
            {
            }

            standardInput?.Dispose();
            standardOutput?.Dispose();
            standardError?.Dispose();
            process?.Dispose();

            standardInput = null;
            standardOutput = null;
            standardError = null;
            process = null;
        }
    }

    private static PaddleServerResponse DeserializeResponse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException("PaddleOCR server returned no response.");
        }

        return JsonSerializer.Deserialize<PaddleServerResponse>(line, JsonOptions)
            ?? throw new InvalidOperationException("PaddleOCR server returned an invalid response.");
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
            "oneDNN",
            "INFO: Could not find files for the given pattern(s)."
        ];

        string[] lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !noisyFragments.Any(fragment => line.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return string.Join(Environment.NewLine, lines);
    }

    private sealed record PaddleServerRequest(
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("image")] string? Image);

    private sealed record PaddleServerResponse(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("items")] IReadOnlyList<PaddleOcrBridgeItem> Items,
        [property: JsonPropertyName("error")] string? Error = null);

    private sealed record PaddleOcrBridgeItem(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("confidence")] float? Confidence,
        [property: JsonPropertyName("box")] double[][] Box);
}
