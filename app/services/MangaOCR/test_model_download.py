#!/usr/bin/env python3
"""Test script to verify YOLO model auto-download functionality."""

import sys
from pathlib import Path

# Add the MangaOCR directory to the Python path
manga_ocr_dir = Path(__file__).parent
sys.path.insert(0, str(manga_ocr_dir))

from manga_yolo_detector import _ensure_model, _MODEL_PATH, ModelNotFoundError


def test_model_download():
    """Test that the model downloads automatically when missing."""
    print(f"Testing YOLO model auto-download...")
    print(f"Model path: {_MODEL_PATH}")
    
    try:
        # This should trigger download if model doesn't exist
        model = _ensure_model()
        print("[OK] Model loaded successfully!")
        print(f"[OK] Model file exists at: {_MODEL_PATH}")
        return True
    except ModelNotFoundError as e:
        print(f"[FAIL] Model download failed: {e}")
        return False
    except Exception as e:
        print(f"[FAIL] Unexpected error: {e}")
        return False


if __name__ == "__main__":
    success = test_model_download()
    sys.exit(0 if success else 1)
