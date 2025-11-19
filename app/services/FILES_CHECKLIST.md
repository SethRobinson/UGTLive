# Services Files Checklist

Use this checklist to verify all required files are in place.

## Shared Folder (`app/services/shared/`)

**Required Python Files:**
- [ ] `config_parser.py` - Configuration file parser
- [ ] `response_models.py` - Pydantic response models
- [ ] `color_analysis.py` - GPU color extraction (CRITICAL!)
- [ ] `README.md` - Documentation

**Note**: `manga_yolo_detector.py` is in MangaOCR directory, not shared

**Required Subdirectories:**
- [ ] `test_images/` - Test images folder
  - [ ] `test.png` - Test image

## EasyOCR Service (`app/services/EasyOCR/`)

- [ ] `service_config.txt` - Service configuration
- [ ] `server.py` - FastAPI server
- [ ] `SetupServerCondaEnv.bat` - Environment setup
- [ ] `RunServer.bat` - Start server
- [ ] `DiagnosticTest.bat` - Verify installation
- [ ] `TestService.bat` - Test running service
- [ ] `README.md` - Service documentation

## MangaOCR Service (`app/services/MangaOCR/`)

- [ ] `service_config.txt` - Service configuration
- [ ] `server.py` - FastAPI server
- [ ] `SetupServerCondaEnv.bat` - Environment setup
- [ ] `RunServer.bat` - Start server
- [ ] `DiagnosticTest.bat` - Verify installation
- [ ] `TestService.bat` - Test running service
- [ ] `README.md` - Service documentation
- [ ] `models/manga109_yolo/` - Models directory (empty until setup)

## docTR Service (`app/services/docTR/`)

- [ ] `service_config.txt` - Service configuration
- [ ] `server.py` - FastAPI server
- [ ] `SetupServerCondaEnv.bat` - Environment setup
- [ ] `RunServer.bat` - Start server
- [ ] `DiagnosticTest.bat` - Verify installation
- [ ] `TestService.bat` - Test running service
- [ ] `README.md` - Service documentation

## Utility Folder (`app/services/util/`)

- [ ] `InstallMiniConda.bat` - Miniconda installer

## Documentation (`app/services/`)

- [ ] `README.md` - Architecture overview
- [ ] `QUICK_START.md` - Getting started guide
- [ ] `MIGRATION_GUIDE.md` - Migration instructions
- [ ] `IMPLEMENTATION_SUMMARY.md` - Technical details
- [ ] `UTF8_COMPATIBILITY.md` - Encoding documentation
- [ ] `FILES_CHECKLIST.md` - This file

## Quick Verification Commands

### Check Shared Python Files
```cmd
dir /b app\services\shared\*.py
```

**Expected output:**
```
color_analysis.py
config_parser.py
response_models.py
```

**Note**: `manga_yolo_detector.py` is in `app\services\MangaOCR\` directory, not shared

### Check All Service Configs
```cmd
dir /s /b app\services\*service_config.txt
```

**Expected output:**
```
...\app\services\docTR\service_config.txt
...\app\services\EasyOCR\service_config.txt
...\app\services\MangaOCR\service_config.txt
```

### Check All Server.py Files
```cmd
dir /s /b app\services\*server.py
```

**Expected output:**
```
...\app\services\docTR\server.py
...\app\services\EasyOCR\server.py
...\app\services\MangaOCR\server.py
```

## Common Issues

### Missing color_analysis.py

**Symptom:** `ModuleNotFoundError: No module named 'color_analysis'`

**Fix:** Ensure `color_analysis.py` exists in `app\services\shared\` directory. This file provides GPU-accelerated color extraction.

**Note**: `manga_yolo_detector.py` should be in `app\services\MangaOCR\` directory, not shared.

### Directory structure issue

The correct structure should be:
```
app/
└── services/
    ├── shared/
    │   ├── config_parser.py
    │   ├── response_models.py
    │   ├── color_analysis.py          ← Must be here!
    │   └── test_images/test.png
    ├── EasyOCR/
    │   └── server.py
    ├── MangaOCR/
    │   └── server.py
    └── docTR/
        └── server.py
```

## File Sizes Reference

To help verify you have the correct files:

- `color_analysis.py` - ~23 KB
- `config_parser.py` - ~2.5 KB
- `response_models.py` - ~1.7 KB

## Total File Count

When everything is in place, you should have approximately:
- **3** Shared Python files (`config_parser.py`, `response_models.py`, `color_analysis.py`)
- **3** Server Python files (`EasyOCR/server.py`, `MangaOCR/server.py`, `docTR/server.py`)
- **1** MangaOCR-specific Python file (`MangaOCR/manga_yolo_detector.py`)
- **12** Service batch files (4 per service: Install.bat, RunServer.bat, DiagnosticTest.bat, Uninstall.bat)
- **1** Utility batch file (`util/InstallMiniConda.bat`)
- **6** Markdown documentation files
- **3** service_config.txt files
- **Multiple** Test images in `shared/test_images/`

**Total: ~30+ files** (not counting downloaded models and venv directories)

