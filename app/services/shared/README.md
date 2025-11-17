# Shared Utilities

This folder contains shared code and resources used by all OCR services.

## Files

- **config_parser.py**: Utilities for parsing `service_config.txt` files
- **response_models.py**: Pydantic models for standardized API responses
- **color_analysis.py**: GPU-accelerated color extraction for OCR regions
- **test_images/**: Test images for service validation

## Usage

Services can import these modules using:

```python
import sys
from pathlib import Path

# Add shared folder to path
shared_dir = Path(__file__).parent.parent / "shared"
sys.path.insert(0, str(shared_dir))

from config_parser import parse_service_config
from response_models import OCRResponse, ServiceInfo
from color_analysis import extract_foreground_background_colors
```

## Test Images

The `test_images/` folder contains sample images for testing services:
- `test.png`: Standard test image for basic OCR validation

