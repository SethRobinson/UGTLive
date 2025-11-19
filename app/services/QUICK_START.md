# Quick Start Guide

Get your OCR services up and running in 5 minutes!

## Prerequisites

- Windows 10/11
- NVIDIA GPU (RTX 30/40/50 series recommended)
  - RTX 30/40 series: Uses PyTorch 2.6.0 with CUDA 11.8
  - RTX 50 series: Uses PyTorch nightly with CUDA 12.8
- Internet connection (for downloading models and packages)

## Step 1: Install Miniconda

If you don't have conda installed:

```batch
cd app\services\util
InstallMiniConda.bat
```

**Important**: After installation, restart your terminal or application for PATH changes to take effect.

## Step 2: Choose and Setup a Service

Pick one service to start with:

### Option A: EasyOCR (Recommended for beginners)

```batch
cd app\services\EasyOCR
SetupServerCondaEnv.bat
```

Wait for setup to complete (~5-10 minutes). It will:
- Detect your GPU (RTX 30/40/50 series)
- Create conda environment `ugt_easyocr` (from venv_name in service_config.txt)
  - Python 3.10 for RTX 30/40 series
  - Python 3.11 for RTX 50 series
- Install PyTorch with appropriate CUDA version
  - PyTorch 2.6.0 + CUDA 11.8 for RTX 30/40
  - PyTorch nightly + CUDA 12.8 for RTX 50
- Install EasyOCR and dependencies
- Download EasyOCR models

### Option B: MangaOCR (For Japanese manga)

```batch
cd app\services\MangaOCR
SetupServerCondaEnv.bat
```

Downloads additional YOLO model (~50MB).

### Option C: docTR (For English documents)

```batch
cd app\services\docTR
SetupServerCondaEnv.bat
```

Good for English text, forms, and documents.

## Step 3: Verify Installation

```batch
DiagnosticTest.bat
```

You should see:
```
[1/5] Checking if conda is installed... [PASS]
[2/5] Checking if environment exists... [PASS]
[3/5] Activating environment... [PASS]
[4/5] Testing Python imports... [PASS]
[5/5] Testing GPU availability... [PASS]
All diagnostic tests passed!
```

## Step 4: Start the Service

```batch
RunServer.bat
```

You should see:
```
Starting EasyOCR service on 127.0.0.1:5000
INFO:     Uvicorn running on http://127.0.0.1:5000
```

Keep this window open.

## Step 5: Test the Service

Open a **new terminal** in the same directory:

```batch
TestService.bat
```

This will:
- Test the health endpoint
- Test the info endpoint
- Process a test image
- Run performance tests

## Step 6: Use the Service

### Test with curl

```batch
# Health check
curl http://localhost:5000/health

# Service info
curl http://localhost:5000/info

# Process an image
curl -X POST http://localhost:5000/process?lang=japan ^
     -H "Content-Type: application/octet-stream" ^
     --data-binary "@your_image.png"
```

### Test with PowerShell

```powershell
# Read image as bytes
$imageBytes = [System.IO.File]::ReadAllBytes("your_image.png")

# Send to service
$response = Invoke-WebRequest -Uri "http://localhost:5000/process?lang=japan" `
    -Method Post `
    -Body $imageBytes `
    -ContentType "application/octet-stream"

# View results
$response.Content | ConvertFrom-Json
```

## Common Issues

### "Conda is not installed"
- Run `InstallMiniConda.bat` from `services\util\`
- Restart your terminal/application after installation

### "GPU not detected"
- Ensure NVIDIA drivers are installed
- Run `nvidia-smi` to verify GPU is visible
- Services will still work on CPU (but slower)

### "Port already in use"
- Check if another service is running on the same port
- Change the port in `service_config.txt`

### Setup fails
- Check your internet connection
- Try running setup again
- Check GPU compatibility (RTX 30/40/50 series)
- For RTX 50 series: Ensure you have the latest NVIDIA drivers
- For CUDA compatibility issues: The setup automatically selects the correct PyTorch version based on your GPU

## Next Steps

### Setup Multiple Services

Each service runs on a different port, so you can run them all simultaneously:

```batch
# Terminal 1
cd app\services\EasyOCR
RunServer.bat

# Terminal 2
cd app\services\MangaOCR
RunServer.bat

# Terminal 3
cd app\services\docTR
RunServer.bat
```

Now you have:
- EasyOCR on port 5000
- MangaOCR on port 5001
- docTR on port 5002

### Integrate with Your Application

The UGTLive C# application automatically discovers and manages services:
- Services are discovered on startup via `PythonServicesManager.Instance.DiscoverServices()`
- Services can be started/stopped via the Python Services Manager UI dialog
- The app checks service status and shows warnings if services aren't running
- See `MIGRATION_GUIDE.md` for detailed C# integration examples

## Service Ports

| Service | Port | Best For |
|---------|------|----------|
| EasyOCR | 5000 | General OCR, multi-language |
| MangaOCR | 5001 | Japanese manga, comics |
| docTR | 5002 | English documents |

## API Quick Reference

### Process Image
```
POST /process?lang={lang}&char_level={true|false}
Content-Type: application/octet-stream
Body: <binary image data>
```

### Get Info
```
GET /info
```

### Health Check
```
GET /health
```

### Shutdown
```
POST /shutdown
```

## Performance Tips

1. **Use GPU**: 5-10x faster than CPU
2. **Keep service running**: First request initializes models (slow)
3. **Reuse HTTP connections**: Use HttpClient with keep-alive
4. **Choose right service**: MangaOCR for Japanese manga, docTR for English

## Getting Help

1. Check the main `README.md` for detailed documentation
2. Check service-specific README in each service folder
3. Run `DiagnosticTest.bat` to identify issues
4. Check the server console output for errors

## Folder Structure Reference

```
services/
├── util/               # InstallMiniConda.bat
├── shared/             # Shared Python code
├── EasyOCR/            # Port 5000
├── MangaOCR/           # Port 5001
└── docTR/              # Port 5002
```

## Success!

If you've completed all steps, you now have a working OCR service! 🎉

Try processing some images and explore the API documentation at:
```
http://localhost:5000/docs
```

FastAPI provides interactive documentation where you can test all endpoints directly in your browser.

