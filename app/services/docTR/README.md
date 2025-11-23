# docTR Service

Document OCR engine with strong English text recognition capabilities. **Does NOT support Japanese text.**

## Service Information

- **Service Name**: docTR
- **Port**: 5002
- **Conda Environment**: ugt_doctr
- **Version**: 0.10.0
- **GitHub**: https://github.com/mindee/doctr

## Features

- Optimized for document and English text
- Uses db_resnet50 for text detection
- Uses master model for text recognition
- Word-level and character-level output
- GPU acceleration
- GPU-accelerated color extraction

## ⚠️ Important Limitation

**docTR does NOT support Japanese text recognition.**

For Japanese OCR, use:
- **EasyOCR** (general Japanese text)
- **MangaOCR** (Japanese manga/comics)

## Supported Languages

docTR primarily supports:
- English
- French
- German
- Spanish
- Portuguese
- Other Latin-alphabet languages

## API Usage

### Process Image

```bash
curl -X POST "http://localhost:5002/process?lang=english&char_level=true" \
     -H "Content-Type: application/octet-stream" \
     --data-binary "@document.png"
```

**Query Parameters**:
- `lang`: Language code (default: `english`, primarily for API consistency)
- `char_level`: Split text into characters (default: `true`)

**Note**: The `lang` parameter is accepted for API consistency but does not change docTR's behavior. docTR's master model is language-agnostic for Latin-alphabet text.

### Service Info

```bash
curl http://localhost:5002/info
```


## Setup

1. Run `SetupServerCondaEnv.bat` to:
   - Automatically detect your GPU (RTX 30/40/50 series)
   - Create conda environment with appropriate PyTorch version:
     - RTX 30/40: PyTorch 2.6.0 + CUDA 11.8
     - RTX 50: PyTorch nightly + CUDA 12.8
   - Install dependencies
   - Download docTR models (db_resnet50 + master)
2. Run `DiagnosticTest.bat` to verify installation
3. Run `RunServer.bat` to start the service
4. Run `TestService.bat` to test the running service

## Models

docTR automatically downloads two models on first use:
- **db_resnet50**: Text detection model (~100MB)
- **master**: Text recognition model (~50MB)

Models are cached by the docTR library.

## Performance

- **GPU**: 5-10x faster than CPU
- **Character-level**: ~100-150ms per image (GPU)
- **Word-level**: ~50-80ms per image (GPU)
- **Detection**: ~30-50ms per image (GPU)
- **Recognition**: ~20-30ms per word (GPU)

## Best For

- English documents
- Forms and invoices
- Printed text
- Latin-alphabet languages
- Document scanning applications

## NOT Suitable For

- ❌ Japanese text (use EasyOCR or MangaOCR)
- ❌ Manga or comics (use MangaOCR)
- ❌ Handwritten text (limited support)
- ❌ Heavily stylized fonts

## Technical Details

### Text Detection (db_resnet50)

Uses a Differentiable Binarization approach with ResNet-50 backbone:
- Detects text regions at arbitrary orientations
- Handles multi-scale text
- Returns normalized coordinates (0-1)

### Text Recognition (master)

MASTER (Multi-Aspect Non-local Network for Scene Text Recognition):
- Attention-based recognition
- Good for complex layouts
- Handles rotated text
- Language-agnostic for Latin alphabets

## Notes

- First request initializes models and may take longer
- GPU is highly recommended for document processing
- Models are automatically moved to GPU if available
- Character-level output estimates positions (not as accurate as word-level bounding boxes)

