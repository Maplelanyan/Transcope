using Microsoft.Win32;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Transcope.Capture;
using Transcope.Ocr;
using Transcope.Translate;
using Windows.Graphics.Imaging;
using Windows.Storage;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace Transcope;

public partial class MainWindow : Window
{
    private readonly IOcrRecognizer ocrRecognizer = new OcrRecognizer();
    private readonly IScreenCaptureService screenCaptureService = new ScreenCaptureService();
    private readonly HttpClient translationHttpClient = new();
    private readonly Dictionary<string, string> realtimeTranslationCache = new(StringComparer.Ordinal);
    private readonly Dictionary<TranslationOverlayWindow, OverlaySession> overlaySessions = new();
    private readonly AppSettingsStore appSettingsStore = new();

    private CancellationTokenSource? recognitionCancellation;
    private CancellationTokenSource? translationCancellation;
    private string? selectedImagePath;
    private ScreenCaptureResult? selectedCapture;
    private string lastRecognizedText = string.Empty;
    private bool isRecognizing;
    private bool isTranslating;
    private bool isLoadingSettings;

    public MainWindow()
    {
        InitializeComponent();
        DeepSeekApiKeyBox.PasswordChanged += DeepSeekApiKeyBox_PasswordChanged;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EngineSelectionBox.SelectedIndex = 0;
        PaddleRuntimeModeBox.SelectedIndex = 0;
        TargetLanguageBox.SelectedIndex = 0;
        LoadSavedSettings();
        RefreshActionButtons();
    }

    private void DeepSeekApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (isLoadingSettings)
        {
            return;
        }

        SaveCurrentApiKey();
    }

    private void LoadSavedSettings()
    {
        isLoadingSettings = true;

        try
        {
            AppSettingsStore.AppSettings settings = appSettingsStore.Load();
            DeepSeekApiKeyBox.Password = settings.DeepSeekApiKey ?? string.Empty;
        }
        finally
        {
            isLoadingSettings = false;
        }
    }

    private void SaveCurrentApiKey()
    {
        appSettingsStore.SaveDeepSeekApiKey(
            string.IsNullOrWhiteSpace(DeepSeekApiKeyBox.Password)
                ? null
                : DeepSeekApiKeyBox.Password.Trim());
    }

    private void PickImageButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files|*.*",
            Title = "Select image for OCR"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        DisposeSelectedCapture();
        selectedImagePath = dialog.FileName;
        ImagePathText.Text = selectedImagePath;
        PreviewImage.Source = CreatePreviewImage(selectedImagePath);
        EmptyPreviewText.Visibility = Visibility.Collapsed;
        lastRecognizedText = string.Empty;
        TranslationTextBox.Text = "Run OCR first, then translate.";
        ResultTextBox.Text = "Click Start OCR.";
        StatusText.Text = "Image loaded.";
        EngineBadgeText.Text = "ENGINE: READY";
        MetricsText.Text = "Lines: 0 / Words: 0";
        RefreshActionButtons();
    }

    private async void CaptureScreenButton_Click(object sender, RoutedEventArgs e)
    {
        if (isRecognizing || isTranslating)
        {
            return;
        }

        try
        {
            Hide();
            await Task.Delay(150);

            ScreenCaptureResult? capture = await screenCaptureService.CaptureRegionAsync();
            if (capture is null)
            {
                StatusText.Text = HasSelectedInput ? StatusText.Text : "Screen capture canceled.";
                return;
            }

            ApplySelectedCapture(capture);
        }
        catch (Exception ex)
        {
            ResultTextBox.Text = ex.Message;
            StatusText.Text = "Screen capture failed.";
            EngineBadgeText.Text = "ENGINE: ERROR";
        }
        finally
        {
            Show();
            Activate();
        }
    }

    private async void SelectRealtimeRegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (isRecognizing || isTranslating)
        {
            return;
        }

        _ = await SelectRealtimeRegionAsync();
    }

    private async void StartRealtimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (isRecognizing || isTranslating)
        {
            return;
        }

        _ = await SelectRealtimeRegionAsync();
    }

    private async Task<bool> SelectRealtimeRegionAsync()
    {
        try
        {
            Hide();
            await Task.Delay(150);

            ScreenCaptureResult? capture = await screenCaptureService.CaptureRegionAsync();
            if (capture is null)
            {
                StatusText.Text = "Overlay selection canceled.";
                return false;
            }

            ApplySelectedCapture(capture);
            AddTranslationOverlay(capture.Bounds);
            StatusText.Text = "Overlay created.";
            return true;
        }
        catch (Exception ex)
        {
            ResultTextBox.Text = ex.Message;
            StatusText.Text = "Overlay selection failed.";
            EngineBadgeText.Text = "ENGINE: ERROR";
            return false;
        }
        finally
        {
            Show();
            Activate();
        }
    }

    private void AddTranslationOverlay(ScreenCaptureBounds bounds)
    {
        TranslationOverlayWindow overlay = new(bounds);
        overlay.ContinueTranslationRequested += Overlay_ContinueTranslationRequested;
        overlay.BoundsChanged += Overlay_BoundsChanged;
        overlay.Closed += Overlay_Closed;
        overlay.Show();

        overlaySessions.Add(overlay, new OverlaySession(bounds, overlay));
        StatusText.Text = $"Overlay added. Active overlays: {overlaySessions.Count}";
        EngineBadgeText.Text = "ENGINE: MANUAL OVERLAY";
        RefreshActionButtons();
    }

    private async void Overlay_ContinueTranslationRequested(object? sender, EventArgs e)
    {
        if (sender is not TranslationOverlayWindow overlay ||
            !overlaySessions.TryGetValue(overlay, out OverlaySession? session) ||
            isTranslating)
        {
            return;
        }

        await RunOverlayTranslationOnceAsync(session);
    }

    private void Overlay_BoundsChanged(object? sender, OverlayBoundsChangedEventArgs e)
    {
        if (sender is TranslationOverlayWindow overlay &&
            overlaySessions.TryGetValue(overlay, out OverlaySession? session))
        {
            session.Bounds = e.Bounds;
            StatusText.Text = $"Overlay updated: {e.Bounds.Width} x {e.Bounds.Height}";
        }
    }

    private void Overlay_Closed(object? sender, EventArgs e)
    {
        if (sender is not TranslationOverlayWindow overlay ||
            !overlaySessions.TryGetValue(overlay, out OverlaySession? session))
        {
            return;
        }

        session.Cancellation?.Cancel();
        session.Cancellation?.Dispose();

        overlay.ContinueTranslationRequested -= Overlay_ContinueTranslationRequested;
        overlay.BoundsChanged -= Overlay_BoundsChanged;
        overlay.Closed -= Overlay_Closed;
        overlaySessions.Remove(overlay);

        StatusText.Text = overlaySessions.Count == 0
            ? "All overlays closed."
            : $"Remaining overlays: {overlaySessions.Count}";
        RefreshActionButtons();
    }

    private async Task RunOverlayTranslationOnceAsync(OverlaySession session)
    {
        session.Cancellation?.Cancel();
        session.Cancellation?.Dispose();
        session.Cancellation = new CancellationTokenSource();
        CancellationTokenSource currentCancellation = session.Cancellation;
        CancellationToken cancellationToken = currentCancellation.Token;

        session.Overlay.SetTranslationRunning(true);
        SetTranslationState(isRunning: true, "Translating overlay...");

        try
        {
            await Dispatcher.InvokeAsync(session.Overlay.HideControls);

            ScreenCaptureResult capture = await Task.Run(
                () => screenCaptureService.Capture(session.Bounds),
                cancellationToken);

            using (capture)
            {
                OcrRecognitionResult ocrResult = await RecognizeTextAsync(
                    capture.SoftwareBitmap,
                    cancellationToken);

                IReadOnlyList<TranslatedOverlayItem> overlayItems =
                    await CreateTranslatedOverlayItemsAsync(ocrResult.Lines, cancellationToken);

                int wordCount = ocrResult.Lines.Sum(static line => line.Words.Count);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested ||
                        !overlaySessions.ContainsKey(session.Overlay))
                    {
                        return;
                    }

                    session.Overlay.Render(overlayItems);
                    ResultTextBox.Text = string.IsNullOrWhiteSpace(ocrResult.Text)
                        ? "No text recognized."
                        : ocrResult.Text;
                    TranslationTextBox.Text = string.Join(
                        Environment.NewLine,
                        overlayItems.Select(static item => item.Text));
                    MetricsText.Text = $"Lines: {ocrResult.Lines.Count} / Words: {wordCount}";
                    StatusText.Text = $"Overlay translated at {DateTime.Now:HH:mm:ss}";
                    EngineBadgeText.Text = CreateEngineBadgeText(ocrResult.EngineKind);
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusText.Text = "Overlay translation canceled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Overlay translation failed.";
            TranslationTextBox.Text = ex.Message;
        }
        finally
        {
            if (overlaySessions.ContainsKey(session.Overlay))
            {
                await Dispatcher.InvokeAsync(session.Overlay.ShowControls);
            }

            if (ReferenceEquals(session.Cancellation, currentCancellation))
            {
                session.Cancellation.Dispose();
                session.Cancellation = null;
            }

            if (overlaySessions.ContainsKey(session.Overlay))
            {
                session.Overlay.SetTranslationRunning(false);
            }

            SetTranslationState(isRunning: false, StatusText.Text);
        }
    }

    private void CloseAllOverlays(string? status)
    {
        foreach (OverlaySession session in overlaySessions.Values.ToArray())
        {
            session.Cancellation?.Cancel();
            session.Cancellation?.Dispose();
            session.Cancellation = null;

            session.Overlay.ContinueTranslationRequested -= Overlay_ContinueTranslationRequested;
            session.Overlay.BoundsChanged -= Overlay_BoundsChanged;
            session.Overlay.Closed -= Overlay_Closed;
            session.Overlay.Close();
        }

        overlaySessions.Clear();

        if (!string.IsNullOrWhiteSpace(status))
        {
            StatusText.Text = status;
        }

        RefreshActionButtons();
    }

    private async void RunOcrButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasSelectedInput)
        {
            StatusText.Text = "Select an image or capture first.";
            return;
        }

        recognitionCancellation?.Dispose();
        recognitionCancellation = new CancellationTokenSource();
        lastRecognizedText = string.Empty;
        TranslationTextBox.Text = "Run OCR first, then translate.";

        SetRecognitionState(isRunning: true, "Recognizing...");
        SoftwareBitmap? loadedBitmap = null;

        try
        {
            SoftwareBitmap bitmap;
            if (selectedCapture is not null)
            {
                bitmap = selectedCapture.SoftwareBitmap;
            }
            else
            {
                loadedBitmap = await LoadSoftwareBitmapAsync(
                    selectedImagePath!,
                    recognitionCancellation.Token);
                bitmap = loadedBitmap;
            }

            OcrRecognitionResult result = await RecognizeTextAsync(
                bitmap,
                recognitionCancellation.Token);

            ResultTextBox.Text = string.IsNullOrWhiteSpace(result.Text)
                ? "No text recognized."
                : result.Text;
            lastRecognizedText = result.Text;
            TranslationTextBox.Text = string.IsNullOrWhiteSpace(lastRecognizedText)
                ? "No OCR text available for translation."
                : "Click Translate to translate the OCR result.";

            int wordCount = result.Lines.Sum(static line => line.Words.Count);
            MetricsText.Text = $"Lines: {result.Lines.Count} / Words: {wordCount}";
            EngineBadgeText.Text = CreateEngineBadgeText(result.EngineKind);
            StatusText.Text = CreateResultStatus(result);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Canceled.";
        }
        catch (Exception ex)
        {
            ResultTextBox.Text = ex.Message;
            lastRecognizedText = string.Empty;
            StatusText.Text = "Recognition failed.";
            EngineBadgeText.Text = "ENGINE: ERROR";
        }
        finally
        {
            loadedBitmap?.Dispose();
            SetRecognitionState(isRunning: false, HasSelectedInput ? StatusText.Text : "Waiting for input.");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        recognitionCancellation?.Cancel();
        translationCancellation?.Cancel();
        CloseAllOverlays("All overlays closed.");
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        string sourceText = string.IsNullOrWhiteSpace(lastRecognizedText)
            ? ResultTextBox.Text
            : lastRecognizedText;

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            StatusText.Text = "No text available for translation.";
            return;
        }

        translationCancellation?.Dispose();
        translationCancellation = new CancellationTokenSource();

        SetTranslationState(isRunning: true, "Translating...");

        try
        {
            ITextTranslator translator = CreateDeepSeekTranslator();
            TranslationResult result = await translator.TranslateAsync(
                sourceText,
                new TranslationRequest
                {
                    SourceLanguage = null,
                    TargetLanguage = GetTargetLanguage()
                },
                translationCancellation.Token);

            TranslationTextBox.Text = result.Text;
            StatusText.Text = $"Translated with {result.Model}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Translation canceled.";
        }
        catch (Exception ex)
        {
            TranslationTextBox.Text = ex.Message;
            StatusText.Text = "Translation failed.";
        }
        finally
        {
            SetTranslationState(isRunning: false, StatusText.Text);
        }
    }

    private async Task<IReadOnlyList<TranslatedOverlayItem>> CreateTranslatedOverlayItemsAsync(
        IReadOnlyList<OcrRecognizedLine> lines,
        CancellationToken cancellationToken)
    {
        OcrRecognizedLine[] visibleLines = lines
            .Where(static line => line.Boundary is not null && !string.IsNullOrWhiteSpace(line.Text))
            .Take(32)
            .ToArray();

        string targetLanguage = GetTargetLanguage();
        string[] missingTexts = visibleLines
            .Select(static line => line.Text.Trim())
            .Distinct(StringComparer.Ordinal)
            .Where(text => !realtimeTranslationCache.ContainsKey(CreateRealtimeTranslationCacheKey(targetLanguage, text)))
            .ToArray();

        if (missingTexts.Length > 0)
        {
            await PopulateRealtimeTranslationCacheAsync(missingTexts, targetLanguage, cancellationToken);
        }

        return visibleLines
            .Select(line =>
            {
                string sourceText = line.Text.Trim();
                string cacheKey = CreateRealtimeTranslationCacheKey(targetLanguage, sourceText);
                string translatedText = realtimeTranslationCache.TryGetValue(cacheKey, out string? cachedText)
                    ? cachedText
                    : sourceText;

                OcrRectangle rectangle = line.Boundary!.AxisAlignedBoundingBox;
                return new TranslatedOverlayItem(
                    translatedText,
                    new Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height));
            })
            .ToArray();
    }

    private async Task PopulateRealtimeTranslationCacheAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        ITextTranslator translator = CreateDeepSeekTranslator();
        TranslationRequest request = new()
        {
            SourceLanguage = null,
            TargetLanguage = targetLanguage
        };

        if (texts.Count == 1)
        {
            TranslationResult singleResult = await translator.TranslateAsync(
                texts[0],
                request,
                cancellationToken);

            realtimeTranslationCache[CreateRealtimeTranslationCacheKey(targetLanguage, texts[0])] = singleResult.Text;
            return;
        }

        TranslationResult batchResult = await translator.TranslateAsync(
            string.Join(Environment.NewLine, texts),
            request,
            cancellationToken);

        string[] translatedLines = SplitTranslatedLines(batchResult.Text);
        if (translatedLines.Length == texts.Count)
        {
            for (int index = 0; index < texts.Count; index++)
            {
                realtimeTranslationCache[CreateRealtimeTranslationCacheKey(targetLanguage, texts[index])] =
                    translatedLines[index];
            }

            return;
        }

        foreach (string text in texts)
        {
            TranslationResult lineResult = await translator.TranslateAsync(
                text,
                request,
                cancellationToken);

            realtimeTranslationCache[CreateRealtimeTranslationCacheKey(targetLanguage, text)] = lineResult.Text;
        }
    }

    private static string CreateRealtimeTranslationCacheKey(string targetLanguage, string text)
    {
        return $"{targetLanguage}\u001F{text}";
    }

    private static string[] SplitTranslatedLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private ValueTask<OcrRecognitionResult> RecognizeTextAsync(
        SoftwareBitmap bitmap,
        CancellationToken cancellationToken)
    {
        OcrRecognitionOptions options = new()
        {
            EngineSelection = GetSelectedEngine(),
            PaddleRuntimeMode = GetSelectedPaddleRuntimeMode(),
            LanguageTag = string.IsNullOrWhiteSpace(LanguageTextBox.Text)
                ? null
                : LanguageTextBox.Text.Trim()
        };

        return ocrRecognizer.RecognizeAsync(bitmap, options, cancellationToken);
    }

    private ITextTranslator CreateDeepSeekTranslator()
    {
        string? apiKey = string.IsNullOrWhiteSpace(DeepSeekApiKeyBox.Password)
            ? null
            : DeepSeekApiKeyBox.Password.Trim();

        return new DeepSeekChatTranslator(
            translationHttpClient,
            new DeepSeekChatTranslatorOptions
            {
                ApiKey = apiKey
            });
    }

    private string GetTargetLanguage()
    {
        if (TargetLanguageBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            !string.IsNullOrWhiteSpace(tag))
        {
            return tag.Trim();
        }

        return TranslationRequest.Default.TargetLanguage;
    }

    private static BitmapImage CreatePreviewImage(string imagePath)
    {
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(imagePath);
        image.EndInit();
        image.Freeze();

        return image;
    }

    private void ApplySelectedCapture(ScreenCaptureResult capture)
    {
        DisposeSelectedCapture();
        selectedCapture = capture;
        selectedImagePath = null;
        ImagePathText.Text = $"Capture {capture.Width} x {capture.Height}";
        PreviewImage.Source = capture.Preview;
        EmptyPreviewText.Visibility = Visibility.Collapsed;
        lastRecognizedText = string.Empty;
        TranslationTextBox.Text = "Run OCR first, then translate.";
        ResultTextBox.Text = "Click Start OCR.";
        StatusText.Text = "Capture loaded.";
        EngineBadgeText.Text = "ENGINE: READY";
        MetricsText.Text = "Lines: 0 / Words: 0";
        RefreshActionButtons();
    }

    private void DisposeSelectedCapture()
    {
        selectedCapture?.Dispose();
        selectedCapture = null;
    }

    private static async Task<SoftwareBitmap> LoadSoftwareBitmapAsync(
        string imagePath,
        CancellationToken cancellationToken)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(imagePath)
            .AsTask(cancellationToken);

        using Windows.Storage.Streams.IRandomAccessStream stream = await file
            .OpenAsync(FileAccessMode.Read)
            .AsTask(cancellationToken);

        WinBitmapDecoder decoder = await WinBitmapDecoder.CreateAsync(stream)
            .AsTask(cancellationToken);

        return await decoder.GetSoftwareBitmapAsync()
            .AsTask(cancellationToken);
    }

    private OcrEngineSelection GetSelectedEngine()
    {
        if (EngineSelectionBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            Enum.TryParse(tag, out OcrEngineSelection selection))
        {
            return selection;
        }

        return OcrEngineSelection.Auto;
    }

    private PaddleRuntimeMode GetSelectedPaddleRuntimeMode()
    {
        if (PaddleRuntimeModeBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            Enum.TryParse(tag, out PaddleRuntimeMode runtimeMode))
        {
            return runtimeMode;
        }

        return PaddleRuntimeMode.Cpu;
    }

    private string CreateEngineBadgeText(OcrEngineKind engineKind)
    {
        if (engineKind == OcrEngineKind.PaddleOcr)
        {
            return $"ENGINE: PaddleOcr ({GetSelectedPaddleRuntimeMode().ToString().ToUpperInvariant()})";
        }

        return $"ENGINE: {engineKind}";
    }

    private static string CreateResultStatus(OcrRecognitionResult result)
    {
        string language = result.LanguageTag is null
            ? "Auto language"
            : $"Language: {result.LanguageTag}";

        string angle = result.TextAngle is null
            ? "Angle: n/a"
            : $"Angle: {result.TextAngle:0.##}";

        return $"{language} / {angle}";
    }

    private void SetRecognitionState(bool isRunning, string status)
    {
        isRecognizing = isRunning;
        StatusText.Text = status;
        RefreshActionButtons();
    }

    private void SetTranslationState(bool isRunning, string status)
    {
        isTranslating = isRunning;
        StatusText.Text = status;
        RefreshActionButtons();
    }

    private bool HasSelectedInput => selectedImagePath is not null || selectedCapture is not null;

    private bool HasOverlayWindows => overlaySessions.Count > 0;

    private void RefreshActionButtons()
    {
        bool isBusy = isRecognizing || isTranslating;
        CaptureScreenButton.IsEnabled = !isBusy;
        SelectRealtimeRegionButton.IsEnabled = !isBusy;
        RunOcrButton.IsEnabled = !isBusy && HasSelectedInput;
        TranslateButton.IsEnabled = !isBusy && !string.IsNullOrWhiteSpace(lastRecognizedText);
        StartRealtimeButton.IsEnabled = !isBusy;
        StartRealtimeButton.Content = "新建覆盖框";
        CancelButton.IsEnabled = isBusy || HasOverlayWindows;
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveCurrentApiKey();
        recognitionCancellation?.Dispose();
        translationCancellation?.Dispose();
        CloseAllOverlays(status: null);
        DisposeSelectedCapture();
        translationHttpClient.Dispose();
        base.OnClosed(e);
    }

    private sealed class OverlaySession
    {
        public OverlaySession(ScreenCaptureBounds bounds, TranslationOverlayWindow overlay)
        {
            Bounds = bounds;
            Overlay = overlay;
        }

        public ScreenCaptureBounds Bounds { get; set; }

        public TranslationOverlayWindow Overlay { get; }

        public CancellationTokenSource? Cancellation { get; set; }
    }
}
