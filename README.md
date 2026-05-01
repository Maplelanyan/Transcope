# Transcope

Transcope is a Windows desktop OCR and translation overlay tool for games and
applications that do not provide the language you need.

Transcope 是一个 Windows 桌面端 OCR + 翻译覆盖工具，适合用于没有目标语言的游戏或应用。它可以框选屏幕区域，识别文字，调用 DeepSeek 翻译，并把译文透明覆盖回原来的位置。

It can capture a selected screen region, recognize text with OCR, translate it
with DeepSeek Chat, and draw translated text back over the original area in a
transparent topmost overlay. Overlay text is designed to stay out of the target
application's way, with separate controls for translation, locking, resizing,
and closing.

它支持对选区截图、OCR 识别、DeepSeek Chat 翻译，以及透明置顶覆盖显示。覆盖层主体尽量点击穿透，不影响下面的游戏或程序；控制按钮则独立显示，用于翻译、锁定、调整大小和关闭。

## Features / 功能

- Region-based screen capture.
- 基于屏幕区域的截图。
- Image-file OCR for PNG, JPG, BMP, TIFF, and related formats.
- 支持 PNG、JPG、BMP、TIFF 等图片文件 OCR。
- Manual translation overlays for game or application text areas.
- 可为游戏或应用中的文字区域创建手动翻译覆盖框。
- Multiple overlay boxes at the same time.
- 支持同时存在多个翻译覆盖框。
- Overlay controls:
- 覆盖框控制：
  - `Translate` runs one OCR + translation pass for that overlay.
  - `Translate`：对当前覆盖框执行一次 OCR + 翻译。
  - `Lock` / `Unlock` toggles moving and resizing.
  - `Lock` / `Unlock`：锁定或解锁位置和大小。
  - `Close` closes only that overlay.
  - `Close`：只关闭当前覆盖框。
- Transparent overlay body with translated text rendered on top.
- 透明覆盖层显示译文。
- Translation cache for repeated text.
- 对重复文本做翻译缓存，减少重复请求。
- DeepSeek API key persistence using Windows DPAPI current-user encryption.
- DeepSeek API Key 使用 Windows DPAPI 当前用户加密后本地保存。
- OCR engine selection:
- OCR 引擎选择：
  - Auto
  - PaddleOCR
  - Windows App SDK AI OCR
  - Windows.Media.Ocr
  - Tesseract OCR
- PaddleOCR runtime selection:
- PaddleOCR 运行模式：
  - CPU
  - GPU, for CUDA-enabled PaddlePaddle environments.
  - GPU，需要 CUDA 版 PaddlePaddle 环境。

## Project Structure / 项目结构

```text
Transcope/
  Transcope.slnx
  Transcope/
    WPF desktop app, main window, app settings, translation overlay windows.
    WPF 主程序、主窗口、本地设置、翻译覆盖窗口。
  Transcope.Capture/
    Screen-region selection and bitmap capture services.
    屏幕区域选择和截图服务。
  Transcope.Ocr/
    OCR abstraction and OCR engine adapters.
    OCR 抽象层和各 OCR 引擎适配器。
  Transcope.Ocr/PaddleOcr/
    Python bridge used by PaddleOCR, including persistent server mode.
    PaddleOCR Python 桥接脚本，包含常驻进程模式。
  Transcope.Translate/
    Translation abstraction and DeepSeek Chat implementation.
    翻译抽象层和 DeepSeek Chat 实现。
```

## Requirements / 环境要求

- Windows 10 2004 or newer.
- Windows 10 2004 或更新版本。
- .NET 8 SDK.
- .NET 8 SDK。
- Visual Studio 2022 or `dotnet` CLI.
- Visual Studio 2022 或 `dotnet` 命令行。
- Optional: Python 3.10 or 3.11 for PaddleOCR.
- 可选：Python 3.10 或 3.11，用于 PaddleOCR。
- Optional: NVIDIA GPU plus a CUDA-enabled PaddlePaddle environment for
  PaddleOCR GPU mode.
- 可选：NVIDIA 显卡和 CUDA 版 PaddlePaddle 环境，用于 PaddleOCR GPU 模式。

The main app targets:

主程序目标框架：

```text
net8.0-windows10.0.19041.0
win-x64
```

## Build / 构建

From the repository root:

在仓库根目录运行：

```powershell
dotnet build .\Transcope.slnx
```

Run the WPF app from Visual Studio, or from the build output:

可以从 Visual Studio 启动，也可以运行构建输出：

```powershell
.\Transcope\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Transcope.exe
```

## DeepSeek Setup / DeepSeek 配置

Transcope uses DeepSeek Chat for translation.

Transcope 使用 DeepSeek Chat 进行翻译。

You can either paste the API key into the GUI or set it as an environment
variable:

你可以在 GUI 中输入 API Key，也可以设置环境变量：

```powershell
[Environment]::SetEnvironmentVariable(
  'DEEPSEEK_API_KEY',
  'your-deepseek-api-key',
  'User')
```

If you enter the key in the GUI, Transcope stores it under the current Windows
user profile using DPAPI current-user encryption. The stored settings file is
under:

如果在 GUI 中输入 Key，Transcope 会使用 Windows DPAPI 当前用户加密保存。配置文件位置：

```text
%LocalAppData%\Transcope\settings.json
```

The default DeepSeek model is:

默认 DeepSeek 模型：

```text
deepseek-chat
```

## PaddleOCR CPU Setup / PaddleOCR CPU 配置

PaddleOCR gives better results on complex game or app UI than traditional OCR
engines in many cases. It runs through a local Python bridge.

在复杂游戏 UI、低分辨率小字、非标准字体等场景下，PaddleOCR 通常比传统 OCR 更稳。Transcope 通过本地 Python 桥接脚本调用 PaddleOCR。

Install Python 3.10 or 3.11, then:

安装 Python 3.10 或 3.11，然后运行：

```powershell
python -m pip install --upgrade pip
python -m pip install paddleocr paddlepaddle
```

If `python` is not the executable you want Transcope to use, set:

如果你希望 Transcope 使用指定 Python，可设置：

```powershell
[Environment]::SetEnvironmentVariable(
  'PADDLEOCR_PYTHON',
  'C:\Path\To\python.exe',
  'User')
```

Transcope also auto-detects a local virtual environment at:

Transcope 也会自动查找本地虚拟环境：

```text
Transcope\.venv-paddleocr\Scripts\python.exe
```

## PaddleOCR GPU Setup / PaddleOCR GPU 配置

GPU mode uses a separate Python executable so CPU mode can remain stable.

GPU 模式使用独立 Python 环境，这样 CPU 模式可以保持稳定，不会互相污染依赖。

Set the GPU Python environment with:

设置 GPU Python 环境：

```powershell
[Environment]::SetEnvironmentVariable(
  'PADDLEOCR_GPU_PYTHON',
  'C:\Path\To\gpu-python.exe',
  'User')
```

Transcope also auto-detects:

Transcope 也会自动查找：

```text
Transcope\.venv-paddleocr-gpu\Scripts\python.exe
```

For NVIDIA RTX 50-series cards, use a PaddlePaddle GPU wheel that supports the
GPU architecture. Older CUDA wheels can initialize CUDA but fail during model
startup with errors like:

对于 NVIDIA RTX 50 系显卡，需要使用支持对应 GPU 架构的 PaddlePaddle GPU wheel。较旧 CUDA wheel 可能能初始化 CUDA，但模型启动时报错，例如：

```text
Unsupported GPU architecture
```

One tested direction for newer NVIDIA cards is a CUDA 12.9 PaddlePaddle GPU
environment, for example:

较新的 NVIDIA 显卡可以尝试 CUDA 12.9 方向的 PaddlePaddle GPU 环境，例如：

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

然后让 Transcope 指向这个 Python：

```powershell
[Environment]::SetEnvironmentVariable(
  'PADDLEOCR_GPU_PYTHON',
  'C:\Users\Maple\source\repos\Transcope\Transcope\.venv-paddleocr-gpu\Scripts\python.exe',
  'User')
```

Restart Transcope after changing environment variables.

修改环境变量后，需要重启 Transcope。

## Tesseract Setup / Tesseract 配置

The WPF project copies traineddata files from:

WPF 项目会复制以下目录中的 traineddata 文件：

```text
Transcope\tessdata\*.traineddata
```

If Tesseract is selected and no traineddata files are available, add the needed
files to that folder or configure `TESSDATA_PREFIX` for your environment.

如果选择 Tesseract 但没有可用 traineddata，请把需要的文件放入该目录，或配置 `TESSDATA_PREFIX`。

## How To Use / 使用方法

### Translate A Game Or App Region / 翻译游戏或应用区域

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

中文步骤：

1. 启动 Transcope。
2. 选择 OCR 引擎。`Auto` 会优先尝试 PaddleOCR，然后回退到 Windows OCR / Tesseract。
3. 在 PaddleOCR 模式中选择 `CPU` 或 `GPU`。
4. 输入或加载 DeepSeek API Key。
5. 点击 `New Overlay` 或区域选择按钮。
6. 在屏幕上框选文字区域。
7. 框选完成后覆盖框会立即出现。
8. 拖动覆盖框顶部区域可以移动位置。
9. 拖动右下角手柄可以调整大小。
10. 位置和大小合适后点击 `Lock`。
11. 点击 `Translate` 执行一次 OCR + 翻译。

You can create more than one overlay by repeating the selection flow.

重复框选流程可以创建多个覆盖框。

### OCR And Translate An Image / OCR 并翻译图片

1. Click the image selection button.
2. Choose an image file.
3. Click `Start OCR`.
4. Click `Translate`.

中文步骤：

1. 点击图片选择按钮。
2. 选择图片文件。
3. 点击 `Start OCR`。
4. 点击 `Translate`。

## Overlay Behavior / 覆盖层行为

The overlay is split into two concepts:

覆盖层分为两部分：

- A click-through transparent translation surface.
- 点击穿透的透明译文显示层。
- A small control surface for `Close`, `Lock`, and `Translate`.
- 用于 `Close`、`Lock`、`Translate` 的控制层。

The transparent surface is configured with Windows extended styles so it does
not block mouse input to the game or application underneath. The controls are
separate so they can still receive clicks.

透明显示层使用 Windows 扩展窗口样式，尽量不拦截底层游戏或应用的鼠标输入。控制按钮独立存在，所以仍然可以点击操作。

During OCR capture, Transcope hides the control surface so button text is not
recognized as part of the target text.

截图 OCR 前，Transcope 会隐藏控制层，避免把按钮文字识别进目标文本。

## OCR Engine Notes / OCR 引擎说明

### Auto / 自动模式

Auto mode tries engines in this order:

自动模式按以下顺序尝试：

1. PaddleOCR
2. Windows App SDK AI OCR
3. Windows.Media.Ocr
4. Tesseract OCR

### PaddleOCR

PaddleOCR is the preferred engine for many game UI scenarios because it handles
complex backgrounds, small text, and non-document layouts better than older OCR
engines.

PaddleOCR 更适合游戏 UI、复杂背景、小字、非文档布局等场景，通常比传统 OCR 更稳。

The PaddleOCR integration uses a persistent Python server process per language
and runtime mode. The first request can still be slower because it loads OCR
models; later requests reuse the process.

Transcope 会按语言和运行模式复用 PaddleOCR 常驻 Python 进程。第一次请求需要加载模型，可能较慢；后续请求会复用进程，速度更快。

### Windows OCR

Windows OCR engines are easier to run because they do not require Python, but
accuracy can be lower for stylized game text.

Windows OCR 不需要 Python，部署简单，但面对游戏字体、描边字、小字时准确率可能较低。

### Tesseract

Tesseract is useful as a local fallback, especially for clear printed text, but
it usually needs good traineddata files and clean images.

Tesseract 适合作为本地备用 OCR，尤其是清晰印刷体文本；但它依赖 traineddata 文件和较干净的图像。

## Troubleshooting / 故障排查

### PaddleOCR is not available / PaddleOCR 不可用

Check that the correct Python executable has PaddleOCR installed:

确认当前 Python 已安装 PaddleOCR：

```powershell
python -c "from paddleocr import PaddleOCR; print('ok')"
```

If Transcope should use a specific Python:

如果 Transcope 应使用指定 Python：

```powershell
[Environment]::SetEnvironmentVariable(
  'PADDLEOCR_PYTHON',
  'C:\Path\To\python.exe',
  'User')
```

Restart Transcope after setting it.

设置后重启 Transcope。

### GPU mode is not available / GPU 模式不可用

Check that Paddle was compiled with CUDA:

检查 Paddle 是否支持 CUDA：

```powershell
python -c "import paddle; print(paddle.device.is_compiled_with_cuda()); paddle.device.set_device('gpu:0'); print(paddle.device.get_device())"
```

If this fails, install a CUDA-enabled PaddlePaddle build that matches your GPU
and driver, then set `PADDLEOCR_GPU_PYTHON`.

如果失败，请安装与你的 GPU 和驱动匹配的 CUDA 版 PaddlePaddle，然后设置 `PADDLEOCR_GPU_PYTHON`。

### First PaddleOCR request is slow / 第一次 PaddleOCR 很慢

The first request starts Python and loads OCR models. Later requests should be
faster because Transcope keeps the PaddleOCR server process alive.

第一次请求会启动 Python 并加载 OCR 模型，所以会慢一些。之后 Transcope 会复用 PaddleOCR 常驻进程，速度会更快。

### Overlay does not appear above a game / 覆盖层没有显示在游戏上方

Some exclusive fullscreen games block normal desktop overlays and screen
capture. Try borderless windowed or windowed mode.

部分独占全屏游戏会阻止普通桌面覆盖层或截图。建议尝试无边框窗口化或窗口模式。

### OCR sees overlay controls / OCR 识别到了覆盖层按钮

Transcope hides the control surface during capture. If you still see control
text in OCR output, make sure you are running the latest build and restart the
application.

Transcope 会在截图时隐藏控制层。如果仍然识别到按钮文字，请确认运行的是最新构建，并重启程序。

### API key changes do not take effect / API Key 或环境变量修改后未生效

Environment variables are read when the process starts. Restart Transcope after
changing `DEEPSEEK_API_KEY`, `PADDLEOCR_PYTHON`, or `PADDLEOCR_GPU_PYTHON`.

环境变量在进程启动时读取。修改 `DEEPSEEK_API_KEY`、`PADDLEOCR_PYTHON` 或 `PADDLEOCR_GPU_PYTHON` 后，需要重启 Transcope。

## Development Notes / 开发说明

- Main WPF app: `Transcope`
- 主 WPF 程序：`Transcope`
- Screen capture code: `Transcope.Capture`
- 截图模块：`Transcope.Capture`
- OCR abstraction and engine adapters: `Transcope.Ocr`
- OCR 抽象和引擎适配：`Transcope.Ocr`
- DeepSeek translation code: `Transcope.Translate`
- DeepSeek 翻译模块：`Transcope.Translate`
- PaddleOCR Python bridge:
- PaddleOCR Python 桥接脚本：

```text
Transcope.Ocr\PaddleOcr\paddle_ocr_bridge.py
```

The app copies the PaddleOCR bridge script to the build output under:

构建时会把 PaddleOCR 桥接脚本复制到输出目录：

```text
PaddleOcr\paddle_ocr_bridge.py
```

## Common Commands / 常用命令

Build / 构建：

```powershell
dotnet build .\Transcope.slnx
```

Clean / 清理：

```powershell
dotnet clean .\Transcope.slnx
```

Run from output / 从输出目录运行：

```powershell
.\Transcope\bin\Debug\net8.0-windows10.0.19041.0\win-x64\Transcope.exe
```

## Current Limitations / 当前限制

- Translation is currently manual per overlay; click `Translate` to refresh.
- 当前每个覆盖框需要手动点击 `Translate` 刷新译文。
- Overlay capture and rendering can be affected by exclusive fullscreen games.
- 独占全屏游戏可能影响截图和覆盖层显示。
- GPU PaddleOCR setup depends on GPU architecture, driver version, and the
  installed PaddlePaddle wheel.
- GPU PaddleOCR 依赖显卡架构、驱动版本和安装的 PaddlePaddle wheel。
- OCR quality depends heavily on source text size, contrast, font style, and
  background complexity.
- OCR 效果受文字大小、对比度、字体样式和背景复杂度影响很大。

