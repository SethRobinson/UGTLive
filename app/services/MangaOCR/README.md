# MangaOCR Service

Specialized OCR engine optimized for Japanese manga and comic text recognition, with YOLO-based text region detection.

## Service Information

- **Service Name**: MangaOCR
- **Port**: 5001
- **Conda Environment**: ugt_mangaocr
- **Version**: 0.1.14
- **GitHub**: https://github.com/kha-white/manga-ocr

## Features

- Optimized for Japanese manga text
- YOLO-based text region detection (Manga109 model)
- Character-level text recognition
- Handles vertical and horizontal text
- Configurable region filtering
- GPU-accelerated color extraction

## How It Works

1. **Text Detection**: Uses Manga109 YOLO model to detect text regions (auto-downloads to `../localdata/models/`)
2. **Region Filtering**: Filters out small regions and overlapping regions
3. **Text Recognition**: Uses Manga OCR to recognize Japanese text in each region
4. **Color Extraction**: Extracts foreground/background colors for each region

## Model Download

The YOLO model (~50MB) is automatically downloaded from HuggingFace on first use:
- **Download Location**: `services/localdata/models/manga109_yolo/model.pt`
- **Source**: https://huggingface.co/deepghs/manga109_yolo
- **Auto-download**: Handled by `manga_yolo_detector.py`
- **Test Download**: Run `python test_model_download.py` from this directory

## API Usage

### Process Image

```bash
curl -X POST "http://localhost:5001/process?lang=japan&char_level=true&min_region_width=10&min_region_height=10&overlap_allowed_percent=50" \
     -H "Content-Type: application/octet-stream" \
     --data-binary "@manga.png"
```

**Query Parameters**:
- `lang`: Language code (default: `japan`, only Japanese is supported)
- `char_level`: Split text into characters (default: `true`)
- `min_region_width`: Minimum region width in pixels (default: `10`)
- `min_region_height`: Minimum region height in pixels (default: `10`)
- `overlap_allowed_percent`: Maximum overlap percentage allowed (default: `50.0`)

### Service Info

```bash
curl http://localhost:5001/info
```

## Setup

1. Run `SetupServerCondaEnv.bat` to:
   - Automatically detect your GPU (RTX 30/40/50 series)
   - Create conda environment with appropriate PyTorch version:
     - RTX 30/40: PyTorch 2.6.0 + CUDA 11.8
     - RTX 50: PyTorch nightly + CUDA 12.8
   - Install dependencies
   - Download Manga109 YOLO model
   - Download Manga OCR model
2. Run `DiagnosticTest.bat` to verify installation
3. Run `RunServer.bat` to start the service
4. Run `TestService.bat` to test the running service

## Region Filtering Parameters

### min_region_width / min_region_height

Filter out regions smaller than these dimensions. Useful for:
- Removing furigana (small text above kanji)
- Filtering noise and artifacts
- Focusing on main text

**Example**: Set to `20` to ignore very small text regions

### overlap_allowed_percent

Maximum overlap percentage between regions. When two regions overlap more than this percentage, the smaller region is removed.

- `0`: No overlap allowed (removes many regions)
- `50`: Moderate overlap allowed (default, good balance)
- `90`: High overlap allowed (keeps more regions)

**Example**: Set to `90` for dense text, `30` for well-separated speech bubbles

## Files

**Service Files**:
- `server.py` - FastAPI service implementation
- `manga_yolo_detector.py` - YOLO text detection module
- `test_model_download.py` - Test script for YOLO model download
- `service_config.txt` - Service configuration

**Models** (auto-downloaded to `../localdata/models/`):
- The Manga109 YOLO model is automatically downloaded on first use
- Location: `services/localdata/models/manga109_yolo/model.pt`
- Size: ~50MB

## Performance

- **GPU**: Required for reasonable performance
- **Character-level**: ~200-400ms per image (GPU, depends on text density)
- **YOLO Detection**: ~50-100ms per image (GPU)
- **OCR per region**: ~20-50ms per region (GPU)

## Best For

- Japanese manga
- Comic books
- Stylized Japanese text
- Speech bubbles and text boxes
- Vertical Japanese text

## Limitations

- **Japanese only**: Does not support other languages
- **Requires YOLO model**: Must download ~50MB model file
- **GPU recommended**: CPU performance is very slow

## Notes

- Small images (< 640x640) are automatically padded for better YOLO detection
- YOLO detects text regions, faces, bodies, and frames (only text is processed)
- For best results with dense text, increase `overlap_allowed_percent` to 80-90
- For speech bubbles, default settings work well

