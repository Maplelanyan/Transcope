# Transcope

Transcope is a Windows desktop OCR and translation overlay tool for games and
applications that do not provide the language you need.

It can capture a selected screen region, recognize text with OCR, translate it
with DeepSeek Chat, and draw translated text back over the original area in a
transparent topmost overlay. Overlay text is designed to stay out of the target
application's way, with separate controls for translation, locking, resizing,
and closing.

## Features

- Region-based screen capture.
- Image-file OCR for PNG, JPG, BMP, TIFF, and related formats.
- Manual translation overlays for game or application text areas.
- Multiple overlay boxes at the same time.
- Overlay controls:
  - `Translate` runs one OCR + translation pass for that overlay.
  - `Lock` / `Unlock` toggles moving and resizing.
  - `Close` closes only that overlay.
- Transparent overlay body with translated text rendered on top.
- Translation cache for repeated text.
- DeepSeek API key persistence using Windows DPAPI current-user encryption.
- OCR engine selection:
  - Auto
  - PaddleOCR
  - Windows App SDK AI OCR
  - Windows.Media.Ocr
  - Tesseract OCR
- PaddleOCR runtime selection:
  - CPU
  - GPU, for CUDA-enabled PaddlePaddle environments.

## Project Structure

```text
Transcope/
  Transcope.slnx
  Transcope/
    WPF desktop app, main window, app settings, translation overlay windows.
  Transcope.Capture/
    Screen-region selection and bitmap capture services.
  Transcope.Ocr/
    OCR abstraction and OCR engine adapters.
  Transcope.Ocr/PaddleOcr/
    Python bridge used by PaddleOCR, including persistent server mode.
  Transcope.Translate/
    Translation abstraction and DeepSeek Chat implementation.
```

## Requirements

- Windows 10 2004 or newer.
- .NET 8 SDK.
- Visual Studio 2022 or `dotnet` CLI.
- Optional: Python 3.10 or 3.11 for PaddleOCR.
- Optional: NVIDIA GPU plus a CUDA-enabled PaddlePaddle environment for
  PaddleOCR GPU mode.

The main app targets:

```text
net8.0-windows10.0.19041.0
win-x64
```

## Build

From the repository root:

```powershell
dotnet build .\Transcope.slnx
```

Run the WPF app from Visual Studio, or from the build output:

```powershell
.\Transcope\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Transcope.exe
```

## DeepSeek Setup

Transcope uses DeepSeek Chat for translation.

You can either paste the API key into the GUI or set it as an environment
variable:

```powershell
[Environment]::SetEnvironmentVariable(
  'DEEPSEEK_API_KEY',
  'your-deepseek-api-key',
  'User')
```

If you enter the key in the GUI, Transcope stores it under the current Windows
user profile using DPAPI current-user encryption. The stored settings file is
under:

```text
%LocalAppData%\Transcope\settings.json
```

The default DeepSeek model is:

```text
deepseek-chat
```

## PaddleOCR CPU Setup

PaddleOCR gives better results on complex game or app UI than traditional OCR
engines in many cases. It runs through a local Python bridge.

Install Python 3.10 or 3.11, then:

```powershell
python -m pip install --upgrade pip
python -m pip install paddleocr paddlepaddle
```

If `python` is not the executable you want Transcope to use, set:

```powershell
[Environment]::SetEnvironmentVariable(
  'PADDLEOCR_PYTHON',
  'C:\Path\To\python.exe',
  'User')
```

Transcope also auto-detects a local virtual environment at:

```text
Transcope\.venv-paddleocr\Scripts\python.exe
```

## PaddleOCR GPU Setup

GPU mode uses a separate Python executable so CPU mode can remain stable.

Set the GPU Python environment with:

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

For NVIDIA RTX 50-series cards, use a PaddlePaddle GPU wheel that supports the
GPU architecture. Older CUDA wheels can initialize CUDA but fail during model
startup with errors like:

```text
Unsupported GPU architecture
```

One tested direction for newer NVIDIA cards is a CUDA 12.9 PaddlePaddle GPU
environment, for example:

```powershell
python -m venv .\.venv-paddleocr-gpu
.\.venv-paddleocr-gpu\Scripts\python.exe -m pip install --upgrade pip
.\.venv-paddleocr-gpu\Scripts\python.exe -m pip install `
  "paddlepaddle-gpu==3.3.0" `
  -i https://www.paddlepaddle.org.cn/packages/stable/cu129/ `
  --extra-index-url https://pypi.org/simple
.\.venv-paddleocr-gpu\Scripts\python.exe -m pip install paddleocr numpy==2.3.5
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

If Tesseract is selected and no traineddata files are available, add the needed
files to that folder or configure `TESSDATA_PREFIX` for your environment.

## How To Use

### Translate A Game Or App Region

1. Start Transcope.
2. Choose the OCR engine. `Auto` tries PaddleOCR first, then falls back to
   Windows OCR and Tesseract.
3. Choose `CPU` or `GPU` in the PaddleOCR mode selector.
4. Enter or load the DeepSeek API key.
5. Click `New Overlay` or the region-selection button.
6. Select the text area on screen.
7. The overlay appears immediately after the selection.
8. Drag the overlay header area to move it.
9. Drag the bottom-right handle to resize it.
10. Click `Lock` when the position and size are correct.
11. Click `Translate` to run one OCR + translation pass.

You can create more than one overlay by repeating the selection flow.

### OCR And Translate An Image

1. Click the image selection button.
2. Choose an image file.
3. Click `Start OCR`.
4. Click `Translate`.

## Overlay Behavior

The overlay is split into two concepts:

- A click-through transparent translation surface.
- A small control surface for `Close`, `Lock`, and `Translate`.

The transparent surface is configured with Windows extended styles so it does
not block mouse input to the game or application underneath. The controls are
separate so they can still receive clicks.

During OCR capture, Transcope hides the control surface so button text is not
recognized as part of the target text.

## OCR Engine Notes

### Auto

Auto mode tries engines in this order:

1. PaddleOCR
2. Windows App SDK AI OCR
3. Windows.Media.Ocr
4. Tesseract OCR

### PaddleOCR

PaddleOCR is the preferred engine for many game UI scenarios because it handles
complex backgrounds, small text, and non-document layouts better than older OCR
engines.

The PaddleOCR integration uses a persistent Python server process per language
and runtime mode. The first request can still be slower because it loads OCR
models; later requests reuse the process.

### Windows OCR

Windows OCR engines are easier to run because they do not require Python, but
accuracy can be lower for stylized game text.

### Tesseract

Tesseract is useful as a local fallback, especially for clear printed text, but
it usually needs good traineddata files and clean images.

## Troubleshooting

### PaddleOCR is not available

Check that the correct Python executable has PaddleOCR installed:

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

Check that Paddle was compiled with CUDA:

```powershell
python -c "import paddle; print(paddle.device.is_compiled_with_cuda()); paddle.device.set_device('gpu:0'); print(paddle.device.get_device())"
```

If this fails, install a CUDA-enabled PaddlePaddle build that matches your GPU
and driver, then set `PADDLEOCR_GPU_PYTHON`.

### First PaddleOCR request is slow

The first request starts Python and loads OCR models. Later requests should be
faster because Transcope keeps the PaddleOCR server process alive.

### Overlay does not appear above a game

Some exclusive fullscreen games block normal desktop overlays and screen
capture. Try borderless windowed or windowed mode.

### OCR sees overlay controls

Transcope hides the control surface during capture. If you still see control
text in OCR output, make sure you are running the latest build and restart the
application.

### API key changes do not take effect

Environment variables are read when the process starts. Restart Transcope after
changing `DEEPSEEK_API_KEY`, `PADDLEOCR_PYTHON`, or `PADDLEOCR_GPU_PYTHON`.

## Development Notes

- Main WPF app: `Transcope`
- Screen capture code: `Transcope.Capture`
- OCR abstraction and engine adapters: `Transcope.Ocr`
- DeepSeek translation code: `Transcope.Translate`
- PaddleOCR Python bridge:

```text
Transcope.Ocr\PaddleOcr\paddle_ocr_bridge.py
```

The app copies the PaddleOCR bridge script to the build output under:

```text
PaddleOcr\paddle_ocr_bridge.py
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

Run from output:

```powershell
.\Transcope\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Transcope.exe
```

## Current Limitations

- Translation is currently manual per overlay; click `Translate` to refresh.
- Overlay capture and rendering can be affected by exclusive fullscreen games.
- GPU PaddleOCR setup depends on GPU architecture, driver version, and the
  installed PaddlePaddle wheel.
- OCR quality depends heavily on source text size, contrast, font style, and
  background complexity.

