# EasyOCR Service

A versatile OCR engine supporting multiple languages including Japanese, Korean, Chinese, Vietnamese, and English.

## Service Information

- **Service Name**: EasyOCR
- **Port**: 5000
- **Conda Environment**: ugt_easyocr
- **Version**: 1.7.2
- **GitHub**: https://github.com/JaidedAI/EasyOCR

## Features

- Multi-language support (Japanese, Korean, Chinese, English, Vietnamese, and more)
- GPU acceleration
- Character-level and word-level OCR
- Automatic language detection
- GPU-accelerated color extraction

## Supported Languages

- `japan` / `ja`: Japanese
- `korean` / `ko`: Korean  
- `chinese` / `ch_sim`: Chinese (Simplified)
- `english` / `en`: English
- `vietnamese` / `vi`: Vietnamese

## API Usage

### Process Image

```bash
curl -X POST "http://localhost:5000/process?lang=japan&char_level=true" \
     -H "Content-Type: application/octet-stream" \
     --data-binary "@image.png"
```

**Query Parameters**:
- `lang`: Language code (default: `japan`)
- `char_level`: Split text into characters (default: `true`)

### Service Info

```bash
curl http://localhost:5000/info
```

## Setup

1. Run `SetupServerCondaEnv.bat` to create the conda environment and install dependencies
   - Automatically detects your GPU (RTX 30/40/50 series)
   - Installs appropriate PyTorch version:
     - RTX 30/40: PyTorch 2.6.0 + CUDA 11.8
     - RTX 50: PyTorch nightly + CUDA 12.8
2. Run `DiagnosticTest.bat` to verify the installation
3. Run `RunServer.bat` to start the service
4. Run `TestService.bat` to test the running service

## Performance

- **GPU**: 5-10x faster than CPU
- **Character-level**: ~100-200ms per image (GPU)
- **Word-level**: ~50-100ms per image (GPU)

## Best For

- General-purpose OCR
- Multi-language documents
- Good balance between speed and accuracy
- When you need flexibility in language support

## Notes

- For Japanese, EasyOCR also loads English as a secondary language
- GPU is highly recommended for good performance
- First request may be slower due to model initialization

