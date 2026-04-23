using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Transcope.Ocr;
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
        private CancellationTokenSource? recognitionCancellation;
        private string? selectedImagePath;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            EngineSelectionBox.SelectedIndex = 0;
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

            selectedImagePath = dialog.FileName;
            ImagePathText.Text = selectedImagePath;
            PreviewImage.Source = CreatePreviewImage(selectedImagePath);
            EmptyPreviewText.Visibility = Visibility.Collapsed;
            RunOcrButton.IsEnabled = true;
            ResultTextBox.Text = "点击“开始 OCR”运行识别。";
            StatusText.Text = "图片已载入";
            EngineBadgeText.Text = "ENGINE: READY";
            MetricsText.Text = "Lines: 0 · Words: 0";
        }

        private async void RunOcrButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedImagePath is null)
            {
                StatusText.Text = "请先选择图片";
                return;
            }

            recognitionCancellation?.Dispose();
            recognitionCancellation = new CancellationTokenSource();

            SetRecognitionState(isRunning: true, "识别中...");

            try
            {
                using SoftwareBitmap bitmap = await LoadSoftwareBitmapAsync(
                    selectedImagePath,
                    recognitionCancellation.Token);

                OcrRecognitionResult result = await RecognizeTextAsync(
                    bitmap,
                    recognitionCancellation.Token);

                ResultTextBox.Text = string.IsNullOrWhiteSpace(result.Text)
                    ? "没有识别到文本。"
                    : result.Text;

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
                StatusText.Text = "识别失败";
                EngineBadgeText.Text = "ENGINE: ERROR";
            }
            finally
            {
                SetRecognitionState(isRunning: false, selectedImagePath is null ? "等待图片" : StatusText.Text);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            recognitionCancellation?.Cancel();
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
            RunOcrButton.IsEnabled = !isRunning && selectedImagePath is not null;
            CancelButton.IsEnabled = isRunning;
            StatusText.Text = status;
        }
    }
}
