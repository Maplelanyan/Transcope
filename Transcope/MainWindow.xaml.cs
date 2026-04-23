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

namespace Transcope
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly IOcrRecognizer ocrRecognizer = new OcrRecognizer();
        private readonly IScreenCaptureService screenCaptureService = new ScreenCaptureService();
        private readonly HttpClient translationHttpClient = new();
        private readonly Dictionary<string, string> realtimeTranslationCache = new(StringComparer.Ordinal);
        private CancellationTokenSource? recognitionCancellation;
        private CancellationTokenSource? translationCancellation;
        private CancellationTokenSource? realtimeCancellation;
        private string? selectedImagePath;
        private ScreenCaptureResult? selectedCapture;
        private TranslationOverlayWindow? translationOverlay;
        private string lastRecognizedText = string.Empty;
        private bool isRecognizing;
        private bool isTranslating;
        private bool isRealtimeRunning;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            EngineSelectionBox.SelectedIndex = 0;
            TargetLanguageBox.SelectedIndex = 0;
            RefreshActionButtons();
        }

        private void PickImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRealtimeRunning)
            {
                StopRealtimeOverlay("实时覆盖已停止");
            }

            OpenFileDialog dialog = new()
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files|*.*",
                Title = "选择用于 OCR 的图片"
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
            RunOcrButton.IsEnabled = true;
            lastRecognizedText = string.Empty;
            TranslateButton.IsEnabled = false;
            TranslationTextBox.Text = "OCR 完成后可翻译结果。";
            ResultTextBox.Text = "点击“开始 OCR”运行识别。";
            StatusText.Text = "图片已载入";
            EngineBadgeText.Text = "ENGINE: READY";
            MetricsText.Text = "Lines: 0 · Words: 0";
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
                    StatusText.Text = HasSelectedInput ? StatusText.Text : "截图已取消";
                    return;
                }

                ApplySelectedCapture(capture);
            }
            catch (Exception ex)
            {
                ResultTextBox.Text = ex.Message;
                StatusText.Text = "截图失败";
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
            if (isRecognizing || isTranslating || isRealtimeRunning)
            {
                return;
            }

            _ = await SelectRealtimeRegionAsync();
        }

        private async void StartRealtimeButton_Click(object sender, RoutedEventArgs e)
        {
            if (isRealtimeRunning)
            {
                StopRealtimeOverlay("实时覆盖已停止");
                return;
            }

            if (isRecognizing || isTranslating)
            {
                return;
            }

            if (selectedCapture is null)
            {
                bool selected = await SelectRealtimeRegionAsync();
                if (!selected || selectedCapture is null)
                {
                    return;
                }
            }

            StartRealtimeOverlay(selectedCapture.Bounds);
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
                    StatusText.Text = "实时区域选择已取消";
                    return false;
                }

                ApplySelectedCapture(capture);
                StatusText.Text = "实时区域已选择，点击“开始实时覆盖”";
                return true;
            }
            catch (Exception ex)
            {
                ResultTextBox.Text = ex.Message;
                StatusText.Text = "实时区域选择失败";
                EngineBadgeText.Text = "ENGINE: ERROR";
                return false;
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private void StartRealtimeOverlay(ScreenCaptureBounds bounds)
        {
            StopRealtimeOverlay(status: null);
            realtimeTranslationCache.Clear();
            translationOverlay = new TranslationOverlayWindow(bounds);
            translationOverlay.ContinueTranslationRequested += Overlay_ContinueTranslationRequested;
            translationOverlay.Show();
            isRealtimeRunning = true;
            StatusText.Text = "覆盖区域已就绪，点击左上角“继续翻译”";
            EngineBadgeText.Text = "ENGINE: MANUAL OVERLAY";
            RefreshActionButtons();
        }

        private async void Overlay_ContinueTranslationRequested(object? sender, EventArgs e)
        {
            if (selectedCapture is null ||
                translationOverlay is null ||
                isTranslating)
            {
                return;
            }

            await RunOverlayTranslationOnceAsync(
                selectedCapture.Bounds,
                translationOverlay);
        }

        private void StopRealtimeOverlay(string? status)
        {
            realtimeCancellation?.Cancel();
            realtimeCancellation?.Dispose();
            realtimeCancellation = null;

            if (translationOverlay is not null)
            {
                translationOverlay.ContinueTranslationRequested -= Overlay_ContinueTranslationRequested;
                translationOverlay.Close();
            }

            translationOverlay = null;
            isRealtimeRunning = false;

            if (!string.IsNullOrWhiteSpace(status))
            {
                StatusText.Text = status;
            }

            RefreshActionButtons();
        }

        private async Task RunOverlayTranslationOnceAsync(
            ScreenCaptureBounds bounds,
            TranslationOverlayWindow overlay)
        {
            realtimeCancellation?.Cancel();
            realtimeCancellation?.Dispose();
            realtimeCancellation = new CancellationTokenSource();
            CancellationTokenSource currentCancellation = realtimeCancellation;
            CancellationToken cancellationToken = currentCancellation.Token;

            overlay.SetTranslationRunning(true);
            SetTranslationState(isRunning: true, "正在翻译覆盖区域...");

            try
            {
                ScreenCaptureResult capture = await Task.Run(
                    () => screenCaptureService.Capture(bounds),
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
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        overlay.Render(overlayItems);
                        ResultTextBox.Text = string.IsNullOrWhiteSpace(ocrResult.Text)
                            ? "没有识别到文本。"
                            : ocrResult.Text;
                        TranslationTextBox.Text = string.Join(
                            Environment.NewLine,
                            overlayItems.Select(static item => item.Text));
                        MetricsText.Text = $"Lines: {ocrResult.Lines.Count} · Words: {wordCount}";
                        StatusText.Text = $"覆盖区域已翻译 · {DateTime.Now:HH:mm:ss}";
                        EngineBadgeText.Text = $"ENGINE: {ocrResult.EngineKind}";
                    });
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                StatusText.Text = "覆盖翻译已取消";
            }
            catch (Exception ex)
            {
                StatusText.Text = "覆盖翻译失败";
                TranslationTextBox.Text = ex.Message;
            }
            finally
            {
                if (ReferenceEquals(realtimeCancellation, currentCancellation))
                {
                    realtimeCancellation.Dispose();
                    realtimeCancellation = null;
                }

                if (ReferenceEquals(translationOverlay, overlay))
                {
                    overlay.SetTranslationRunning(false);
                }

                SetTranslationState(isRunning: false, StatusText.Text);
            }
        }

        private async void RunOcrButton_Click(object sender, RoutedEventArgs e)
        {
            if (!HasSelectedInput)
            {
                StatusText.Text = "请先选择图片";
                return;
            }

            recognitionCancellation?.Dispose();
            recognitionCancellation = new CancellationTokenSource();
            lastRecognizedText = string.Empty;
            TranslationTextBox.Text = "OCR 完成后可翻译结果。";

            SetRecognitionState(isRunning: true, "识别中...");
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
                    ? "没有识别到文本。"
                    : result.Text;
                lastRecognizedText = result.Text;
                TranslationTextBox.Text = string.IsNullOrWhiteSpace(lastRecognizedText)
                    ? "没有可翻译的 OCR 文本。"
                    : "点击“翻译”使用 DeepSeek Chat 翻译 OCR 结果。";

                int wordCount = result.Lines.Sum(static line => line.Words.Count);
                MetricsText.Text = $"Lines: {result.Lines.Count} · Words: {wordCount}";
                EngineBadgeText.Text = $"ENGINE: {result.EngineKind}";
                StatusText.Text = CreateResultStatus(result);
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "已取消";
            }
            catch (Exception ex)
            {
                ResultTextBox.Text = ex.Message;
                lastRecognizedText = string.Empty;
                StatusText.Text = "识别失败";
                EngineBadgeText.Text = "ENGINE: ERROR";
            }
            finally
            {
                loadedBitmap?.Dispose();
                SetRecognitionState(isRunning: false, HasSelectedInput ? StatusText.Text : "等待图片");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            recognitionCancellation?.Cancel();
            translationCancellation?.Cancel();
            if (isRealtimeRunning)
            {
                StopRealtimeOverlay("实时覆盖已停止");
            }
        }

        private async void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            string sourceText = string.IsNullOrWhiteSpace(lastRecognizedText)
                ? ResultTextBox.Text
                : lastRecognizedText;

            if (string.IsNullOrWhiteSpace(sourceText))
            {
                StatusText.Text = "没有可翻译的文本";
                return;
            }

            translationCancellation?.Dispose();
            translationCancellation = new CancellationTokenSource();

            SetTranslationState(isRunning: true, "DeepSeek Chat 翻译中...");

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
                StatusText.Text = $"DeepSeek Chat translated with {result.Model}";
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "翻译已取消";
            }
            catch (Exception ex)
            {
                TranslationTextBox.Text = ex.Message;
                StatusText.Text = "翻译失败";
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
                    realtimeTranslationCache[CreateRealtimeTranslationCacheKey(targetLanguage, texts[index])] = translatedLines[index];
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
            ImagePathText.Text = $"截图 {capture.Width} x {capture.Height}";
            PreviewImage.Source = capture.Preview;
            EmptyPreviewText.Visibility = Visibility.Collapsed;
            RunOcrButton.IsEnabled = true;
            lastRecognizedText = string.Empty;
            TranslateButton.IsEnabled = false;
            TranslationTextBox.Text = "OCR 完成后可翻译结果。";
            ResultTextBox.Text = "点击“开始 OCR”运行识别。";
            StatusText.Text = "截图已载入";
            EngineBadgeText.Text = "ENGINE: READY";
            MetricsText.Text = "Lines: 0 · Words: 0";
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

        private static string CreateResultStatus(OcrRecognitionResult result)
        {
            string language = result.LanguageTag is null
                ? "Auto language"
                : $"Language: {result.LanguageTag}";

            string angle = result.TextAngle is null
                ? "Angle: n/a"
                : $"Angle: {result.TextAngle:0.##}";

            return $"{language} · {angle}";
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

        private void RefreshActionButtons()
        {
            bool isBusy = isRecognizing || isTranslating;
            CaptureScreenButton.IsEnabled = !isBusy && !isRealtimeRunning;
            SelectRealtimeRegionButton.IsEnabled = !isBusy && !isRealtimeRunning;
            RunOcrButton.IsEnabled = !isBusy && !isRealtimeRunning && HasSelectedInput;
            TranslateButton.IsEnabled = !isBusy && !isRealtimeRunning && !string.IsNullOrWhiteSpace(lastRecognizedText);
            StartRealtimeButton.IsEnabled = !isBusy || isRealtimeRunning;
            StartRealtimeButton.Content = isRealtimeRunning ? "关闭翻译框" : "显示翻译框";
            CancelButton.IsEnabled = isBusy || isRealtimeRunning;
        }

        protected override void OnClosed(EventArgs e)
        {
            realtimeCancellation?.Cancel();
            recognitionCancellation?.Dispose();
            translationCancellation?.Dispose();
            realtimeCancellation?.Dispose();
            translationOverlay?.Close();
            DisposeSelectedCapture();
            translationHttpClient.Dispose();
            base.OnClosed(e);
        }
    }
}
