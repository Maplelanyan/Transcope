# PaddleOCR setup

Transcope can use PaddleOCR when a local Python environment provides the
`paddleocr` and `paddlepaddle` packages.

Install Python 3.10 or 3.11, then run:

```powershell
python -m pip install --upgrade pip
python -m pip install paddleocr paddlepaddle
```

If Python is not available as `python`, set `PADDLEOCR_PYTHON` to the full path
of the Python executable:

```powershell
[Environment]::SetEnvironmentVariable(
  'PADDLEOCR_PYTHON',
  'C:\Path\To\python.exe',
  'User')
```

When PaddleOCR is available, Transcope's `Auto` OCR mode uses it before falling
back to Windows OCR and Tesseract.
