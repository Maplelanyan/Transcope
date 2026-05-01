# Transcope

Transcope is a Windows desktop OCR translation overlay tool for games and
applications that do not provide the language you need.

Transcope 是一个 Windows 桌面端 OCR 翻译覆盖工具，主要用于没有目标语言的游戏或应用程序。它可以让用户框选屏幕上的文字区域，识别文字，调用 DeepSeek 翻译，并把译文以半透明覆盖层显示回原位置。

## What It Does

Transcope focuses on one workflow:

1. Select a screen region that contains text.
2. Create a movable and resizable translation overlay.
3. Run OCR on that region.
4. Translate recognized text.
5. Render translated text over the original area.
6. Keep the overlay body click-through as much as possible so it does not block the game or application underneath.

核心目标是：把游戏或应用里的外语文字实时辅助翻译，并尽量不影响原程序的鼠标操作。

## Current Features

- Screen capture and region selection.
- Image file OCR.
- Multiple translation overlays at the same time.
- Overlay toolbar placed outside the selected region so it does not cover translated text.
- Overlay controls:
  - drag handle: move the overlay.
  - `X`: close the overlay.
  - `锁` / `解`: lock or unlock moving and resizing.
  - `译`: run one manual OCR and translation pass.
  - `AUTO`: enable automatic OCR scanning and translation.
- Automatic translation mode:
  - scans the selected region about every 1.2 seconds.
  - compares recognized text with the previous pass.
  - skips translation API calls when text is unchanged.
  - translates only when the recognized text changes.
- Translation cache for repeated text.
- DeepSeek API key storage using Windows DPAPI current-user encryption.
- OCR engine selection:
  - Auto
  - PaddleOCR
  - Windows App SDK AI OCR
  - Windows.Media.Ocr
  - Tesseract OCR
- PaddleOCR runtime selection:
  - CPU
  - GPU
- OCR language selection from the GUI.
- Custom application icon.

## Project Structure

```text
Transcope/
  Transcope.slnx
  README.md

  Transcope/
    WPF desktop app.
    Main window, settings storage, overlay windows, app icon.

  Transcope.Capture/
    Screen region selection and bitmap capture.

  Transcope.Ocr/
    OCR abstraction and OCR engine adapters.

  Transcope.Ocr/PaddleOcr/
    PaddleOCR Python bridge.
    Includes persistent server mode.

  Transcope.Translate/
    Translation abstraction and DeepSeek Chat implementation.
```

## Requirements

- Windows 10 2004 or newer.
- .NET 8 SDK.
- Visual Studio 2022 or the `dotnet` CLI.
- Optional: Python 3.10 or 3.11 for PaddleOCR.
- Optional: NVIDIA GPU plus a CUDA-enabled PaddlePaddle environment for PaddleOCR GPU mode.

The main WPF app targets:

```text
net8.0-windows10.0.19041.0
win-x64
```

## Build

From the repository root:

```powershell
dotnet build .\Transcope.slnx
```

Run from the build output:

```powershell
.\Transcope\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Transcope.exe
```

## DeepSeek Setup

Transcope uses DeepSeek Chat for translation.

You can enter the API key in the GUI. Transcope stores it here using Windows DPAPI encryption for the current user:

```text
%LocalAppData%\Transcope\settings.json
```

You can also set an environment variable:

```powershell
[Environment]::SetEnvironmentVariable(
  'DEEPSEEK_API_KEY',
  'your-deepseek-api-key',
  'User')
```

Restart Transcope after changing environment variables.

Default model:

```text
deepseek-chat
```

## PaddleOCR CPU Setup

PaddleOCR is usually the best OCR option for game UI, small text, stylized fonts, and complex backgrounds.

Install PaddleOCR into a Python environment:

```powershell
python -m pip install --upgrade pip
python -m pip install paddleocr paddlepaddle
```

If Transcope should use a specific Python executable:

```powershell
[Environment]::SetEnvironmentVariable(
  'PADDLEOCR_PYTHON',
  'C:\Path\To\python.exe',
  'User')
```

Transcope also auto-detects:

```text
Transcope\.venv-paddleocr\Scripts\python.exe
```

## PaddleOCR GPU Setup

GPU mode uses a separate Python executable so CPU mode can remain stable.

Set the GPU Python executable:

```powershell
[Environment]::SetEnvironmentVariable(
  'PADDLEOCR_GPU_PYTHON',
  'C:\Path\To\gpu-python.exe',
  'User')
```

Transcope also auto-detects:

```text
Transcope\.venv-paddleocr-gpu\Scripts\python.exe
```

For newer NVIDIA cards, use a PaddlePaddle GPU wheel that supports your GPU architecture and CUDA runtime. One tested direction for newer RTX cards is CUDA 12.9:

```powershell
python -m venv .\Transcope\.venv-paddleocr-gpu
.\Transcope\.venv-paddleocr-gpu\Scripts\python.exe -m pip install --upgrade pip
.\Transcope\.venv-paddleocr-gpu\Scripts\python.exe -m pip install `
  "paddlepaddle-gpu==3.3.0" `
  -i https://www.paddlepaddle.org.cn/packages/stable/cu129/ `
  --extra-index-url https://pypi.org/simple
.\Transcope\.venv-paddleocr-gpu\Scripts\python.exe -m pip install paddleocr numpy==2.3.5
```

Then point Transcope to it:

```powershell
[Environment]::SetEnvironmentVariable(
  'PADDLEOCR_GPU_PYTHON',
  'C:\Users\Maple\source\repos\Transcope\Transcope\.venv-paddleocr-gpu\Scripts\python.exe',
  'User')
```

Restart Transcope after changing environment variables.

## Tesseract Setup

The WPF project copies traineddata files from:

```text
Transcope\tessdata\*.traineddata
```

If Tesseract is selected and no traineddata files are available, add the needed files to that folder or configure `TESSDATA_PREFIX`.

## How To Use

### Translate A Game Or App Region

1. Start Transcope.
2. Choose an OCR engine. `Auto` tries PaddleOCR first.
3. Choose the OCR language.
4. Choose `CPU` or `GPU` for PaddleOCR.
5. Enter the DeepSeek API key if it has not been saved.
6. Click `选择翻译区域`.
7. Drag on the screen to select the text area.
8. The overlay appears after selection.
9. Use the dot handle on the toolbar to move the overlay.
10. Use the bottom-right handle to resize it.
11. Click `锁` when the overlay position is correct.
12. Click `译` for one manual translation pass, or turn on `AUTO` for automatic translation.

### OCR And Translate An Image

1. Click `选择图片`.
2. Choose an image file.
3. Choose OCR engine and language.
4. Click `文本识别`.
5. Click `翻译`.

## Overlay Behavior

The overlay is split into two windows:

- A transparent translation surface that displays translated text.
- A separate control toolbar outside the selected region.

The translation surface is configured with Windows extended styles to be click-through where possible. This helps avoid blocking mouse input to the underlying game or application.

The toolbar remains clickable because it is a separate control surface. It contains move, close, lock, manual translate, and auto translate controls.

`AUTO` mode does not translate continuously when nothing changes. It OCRs the region, normalizes the recognized text, compares it with the previous result, and only sends a translation request when the text changes.

## OCR Engine Notes

### Auto

Auto mode tries engines in this order:

1. PaddleOCR
2. Windows App SDK AI OCR
3. Windows.Media.Ocr
4. Tesseract OCR

### PaddleOCR

PaddleOCR runs through a local Python bridge. Transcope keeps a persistent Python server process per language and runtime mode, so the first request can be slower while models load, and later requests are faster.

Transcope forces UTF-8 communication with the PaddleOCR Python process to avoid Chinese OCR output becoming garbled on Windows.

### Windows OCR

Windows OCR engines are easier to run because they do not require Python. Accuracy may be lower for stylized game text or small UI fonts.

### Tesseract

Tesseract is useful as a local fallback for clear printed text. It needs matching traineddata files.

## Troubleshooting

### PaddleOCR is not available

Check that the Python executable has PaddleOCR installed:

```powershell
python -c "from paddleocr import PaddleOCR; print('ok')"
```

If Transcope should use a specific Python:

```powershell
[Environment]::SetEnvironmentVariable(
  'PADDLEOCR_PYTHON',
  'C:\Path\To\python.exe',
  'User')
```

Restart Transcope after setting it.

### GPU mode is not available

Check CUDA support:

```powershell
python -c "import paddle; print(paddle.device.is_compiled_with_cuda()); paddle.device.set_device('gpu:0'); print(paddle.device.get_device())"
```

If this fails, install a CUDA-enabled PaddlePaddle build that matches your GPU and driver, then set `PADDLEOCR_GPU_PYTHON`.

### First PaddleOCR request is slow

The first request starts Python and loads OCR models. Later requests reuse the persistent server process.

### Chinese OCR output is garbled

Make sure you are running the latest build. The PaddleOCR bridge and C# process force UTF-8 communication using `PYTHONIOENCODING=utf-8` and `PYTHONUTF8=1`.

### Overlay does not appear above a game

Some exclusive fullscreen games block normal desktop overlays and screen capture. Try borderless windowed mode or windowed mode.

### Auto mode uses too many resources

`AUTO` mode OCRs about every 1.2 seconds. It skips translation when text is unchanged, but OCR still runs. Use a smaller selected region when possible.

### API key or Python environment changes do not take effect

Environment variables are read when the process starts. Restart Transcope after changing:

```text
DEEPSEEK_API_KEY
PADDLEOCR_PYTHON
PADDLEOCR_GPU_PYTHON
```

## Common Commands

Build:

```powershell
dotnet build .\Transcope.slnx
```

Clean:

```powershell
dotnet clean .\Transcope.slnx
```

Run:

```powershell
.\Transcope\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Transcope.exe
```

## Current Limitations

- OCR quality depends heavily on text size, contrast, font style, and background complexity.
- `AUTO` mode avoids repeated translation calls, but OCR itself still runs on the selected interval.
- Exclusive fullscreen games can interfere with capture and overlay behavior.
- GPU PaddleOCR setup depends on GPU architecture, driver version, CUDA runtime, and the installed PaddlePaddle wheel.
- The click-through overlay behavior depends on Windows desktop composition and may vary across applications.
