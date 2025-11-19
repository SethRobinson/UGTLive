# Text Orientation Detection Update

## Problem
MangaOCR (and other OCR services) were not sending `text_orientation` information in their responses, causing vertical Japanese text in manga to be rendered horizontally.

## Solution
Added automatic text orientation detection to all OCR services based on bounding box dimensions.

## Changes Made

### 1. Updated Response Model (`app/services/shared/response_models.py`)
Added `text_orientation` field to `TextObject` model:
```python
class TextObject(BaseModel):
    ...
    text_orientation: Optional[str] = None
```

### 2. Added Orientation Detection Function
Implemented `detect_text_orientation()` function in all OCR services:

```python
def detect_text_orientation(width: int, height: int, aspect_ratio_threshold: float = 1.5) -> str:
    """
    Detect text orientation based on bounding box dimensions.
    
    Returns:
        "vertical" if height > width * threshold, "horizontal" otherwise
    """
    if width == 0:
        return "vertical"
    
    aspect_ratio = height / width
    
    # If height is significantly greater than width, it's likely vertical text
    if aspect_ratio > aspect_ratio_threshold:
        return "vertical"
    else:
        return "horizontal"
```

### 3. Updated OCR Services

#### MangaOCR (`app/services/MangaOCR/server.py`)
- Added `detect_text_orientation()` function
- Updated `process_manga_ocr()` to detect and include orientation for each text region
- Orientation is added to both character-level and block-level text objects

#### EasyOCR (`app/services/EasyOCR/server.py`)
- Added `detect_text_orientation()` function
- Updated `process_ocr_results()` to include orientation for each detection
- Orientation is included in all text objects

#### docTR (`app/services/docTR/server.py`)
- Added `detect_text_orientation()` function
- Updated `process_doctr_results()` to detect and include orientation
- Orientation is added to both character-level and word-level text objects

## How It Works

1. **Detection Logic**: 
   - Compares height-to-width aspect ratio of text bounding boxes
   - If `height / width > 1.5`, text is classified as "vertical"
   - Otherwise, text is classified as "horizontal"

2. **Processing Flow**:
   ```
   OCR Service → Detect Text → Calculate Bounding Box → 
   Detect Orientation → Add to Response → C# App Renders
   ```

3. **Rendering**:
   - The C# application already had logic to handle `text_orientation` (in `BlockDetectionManager.cs` and rendering code)
   - It applies CSS `writing-mode: vertical-rl` for vertical text
   - Vertical text is preserved for source language, but may be converted to horizontal for target languages that don't support vertical rendering

## Testing Instructions

1. **Start/Restart MangaOCR Service**:
   ```
   From Services tab in the app, restart MangaOCR service
   ```

2. **Test with Manga**:
   - Open a manga page with vertical Japanese text
   - Use MangaOCR as the OCR service
   - Switch overlay to "Source" mode
   - Verify that vertical text columns are rendered vertically

3. **Check Orientation Detection**:
   - Enable "Log extra debug stuff" in settings
   - Check console output for text_orientation values
   - Should see "vertical" for tall, narrow text boxes
   - Should see "horizontal" for wide, short text boxes

## Expected Behavior

### Before Fix
- All text rendered horizontally, regardless of actual orientation
- Vertical manga text appeared sideways or incorrectly

### After Fix
- Vertical text (height > 1.5 × width) renders with `writing-mode: vertical-rl`
- Horizontal text renders normally
- Text orientation preserved from OCR detection through to final rendering

## Configuration

The aspect ratio threshold (default: 1.5) can be adjusted in each service's `detect_text_orientation()` function if needed:
- **Lower threshold (e.g., 1.2)**: More text classified as vertical
- **Higher threshold (e.g., 2.0)**: More conservative vertical detection

## Compatibility

- All changes are backward compatible
- Services without `text_orientation` will default to "unknown" in the C# app
- Block detection manager handles missing orientation gracefully

## Files Modified

1. `app/services/shared/response_models.py` - Added text_orientation field
2. `app/services/MangaOCR/server.py` - Added detection and field to responses
3. `app/services/EasyOCR/server.py` - Added detection and field to responses  
4. `app/services/docTR/server.py` - Added detection and field to responses

## Notes

- The C# application already had rendering support for vertical text
- This fix ensures OCR services provide the necessary orientation information
- Detection is automatic and requires no configuration
- Works with all character-level and block-level OCR modes

