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
        private CancellationTokenSource? recognitionCancellation;
        private CancellationTokenSource? translationCancellation;
        private string? selectedImagePath;
        private ScreenCaptureResult? selectedCapture;
        private string lastRecognizedText = string.Empty;
        private bool isRecognizing;
        private bool isTranslating;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            EngineSelectionBox.SelectedIndex = 0;
            TargetLanguageBox.SelectedIndex = 0;
        }

        private void PickImageButton_Click(object sender, RoutedEventArgs e)
        {
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
            CaptureScreenButton.IsEnabled = !isBusy;
            RunOcrButton.IsEnabled = !isBusy && HasSelectedInput;
            TranslateButton.IsEnabled = !isBusy && !string.IsNullOrWhiteSpace(lastRecognizedText);
            CancelButton.IsEnabled = isBusy;
        }

        protected override void OnClosed(EventArgs e)
        {
            recognitionCancellation?.Dispose();
            translationCancellation?.Dispose();
            DisposeSelectedCapture();
            translationHttpClient.Dispose();
            base.OnClosed(e);
        }
    }
}
