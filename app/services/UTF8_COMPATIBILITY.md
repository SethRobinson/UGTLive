# UTF-8 Compatibility Update

All batch scripts have been updated to support international Windows locales (Japanese, Chinese, Korean, etc.).

## Problem

The original batch scripts used:
- `type "%CONFIG_FILE%" ^| findstr /v "^#"` - encoding-dependent
- No code page setting - relies on system locale
- Could break on non-English Windows systems

## Solution

### 1. Force UTF-8 Encoding

Added to **all batch files**:
```batch
@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION
```

### 2. Improved Parsing

Changed from:
```batch
for /f "tokens=1,2 delims=|" %%a in ('type "%CONFIG_FILE%" ^| findstr /v "^#"') do (
```

To:
```batch
for /f "usebackq tokens=1,2 delims=| eol=#" %%a in ("%CONFIG_FILE%") do (
```

**Benefits**:
- `usebackq` - Proper quoting of file paths
- `eol=#` - Skip comment lines natively (instead of `findstr`)
- More reliable across different locales

### 3. Improved Comments

Changed from:
```batch
REM Remove leading/trailing spaces
for /f "tokens=* delims= " %%x in ("!KEY!") do set "KEY=%%x"
```

To:
```batch
REM Trim leading/trailing spaces
for /f "tokens=*" %%x in ("!KEY!") do set "KEY=%%x"
```

## Files Updated

### Service Batch Files (12 total)

**EasyOCR (4 files)**:
- ✅ SetupServerCondaEnv.bat
- ✅ RunServer.bat
- ✅ DiagnosticTest.bat
- ✅ TestService.bat

**MangaOCR (4 files)**:
- ✅ SetupServerCondaEnv.bat
- ✅ RunServer.bat
- ✅ DiagnosticTest.bat
- ✅ TestService.bat

**docTR (4 files)**:
- ✅ SetupServerCondaEnv.bat
- ✅ RunServer.bat
- ✅ DiagnosticTest.bat
- ✅ TestService.bat

### Utility Files (1 total)

**util**:
- ✅ InstallMiniConda.bat

**Total: 13 files updated**

## Configuration Requirements

### ASCII-Only Fields (Critical)

These fields **must** use ASCII characters only:
- `service_name` - Example: `EasyOCR` ✅, `Easy OCR` ❌ (no spaces), `簡単OCR` ❌ (no Japanese)
- `venv_name` - Example: `ugt_easyocr` ✅, `ugt easycr` ❌ (no spaces), `ugt_日本語` ❌ (no Japanese)
- `port` - Example: `5000` ✅

### Unicode-Allowed Fields

These fields **can** contain Unicode characters:
- `description` - Example: `OCRエンジン for Japanese manga` ✅
- `author` - Example: `山田太郎` ✅
- `github_url` - Example: URLs with special characters ✅

### Why This Restriction?

- **service_name**: Used in echo statements and window titles
- **venv_name**: Conda/virtual environment names should be ASCII-only for compatibility
- **port**: Must be numeric

Descriptions and metadata can safely contain Unicode because they're only displayed, not used in commands.

## Testing Across Locales

To test on different Windows code pages:

```batch
REM Test with Japanese locale (Shift-JIS)
chcp 932
RunServer.bat

REM Test with Western European (Windows-1252)
chcp 1252
RunServer.bat

REM Test with UTF-8 (recommended)
chcp 65001
RunServer.bat
```

With the updates, all three should work identically.

## Example Config File

```
description|A versatile OCR engine supporting multiple languages including 日本語|
github_url|https://github.com/JaidedAI/EasyOCR|
author|JaidedAI (山田太郎)|
service_name|EasyOCR|
venv_name|ugt_easyocr|
port|5000|
local_only|true|
version|1.7.2|
```

**Notes**:
- Description contains Japanese characters ✅
- Author contains Japanese characters ✅
- service_name is ASCII-only ✅
- venv_name is ASCII-only ✅

## Saving Config Files

Save all `service_config.txt` files as:
- **Encoding**: UTF-8 (with or without BOM)
- **Line Endings**: Windows (CRLF) or Unix (LF) - both work

In most text editors:
- **VS Code**: Bottom-right corner → UTF-8
- **Notepad++**: Encoding → UTF-8
- **Notepad**: Save As → Encoding: UTF-8

## Verification

To verify the updates are working:

1. **Run on English Windows**: Should work as before
2. **Run on Japanese Windows**: Should work identically
3. **Add Unicode to description**: Should display correctly
4. **Keep service_name ASCII**: Everything should work

## Backward Compatibility

✅ **Fully backward compatible**
- Existing config files work without changes
- ASCII-only configs continue to work
- No breaking changes to functionality

## Benefits

1. ✅ Works on Japanese Windows (CP932/Shift-JIS)
2. ✅ Works on Chinese Windows (CP936/GBK)
3. ✅ Works on Korean Windows (CP949)
4. ✅ Works on any Windows locale
5. ✅ Consistent behavior everywhere
6. ✅ Supports Unicode in descriptions
7. ✅ No manual code page switching needed

## Technical Details

### chcp 65001

- Forces UTF-8 code page (65001)
- Applied at the start of each batch file
- Output redirected to nul (silent)
- Ensures consistent text encoding

### usebackq

- Allows quoted file paths in for loops
- More robust than unquoted paths
- Handles paths with spaces correctly

### eol=#

- Treats lines starting with # as comments
- Native for loop feature (faster than findstr)
- More reliable across locales

## Related Documentation

- **README.md**: Configuration requirements
- **IMPLEMENTATION_SUMMARY.md**: Complete technical details
- **QUICK_START.md**: Getting started guide

## Summary

All batch scripts now:
- ✅ Force UTF-8 encoding
- ✅ Use improved parsing
- ✅ Work on any Windows locale
- ✅ Support Unicode in descriptions
- ✅ Require ASCII for critical fields
- ✅ Are fully backward compatible

**No action required for existing installations** - updates are automatic when you run the scripts.

