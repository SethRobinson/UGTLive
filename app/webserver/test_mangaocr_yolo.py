"""Test script to verify Manga109 YOLO + MangaOCR integration."""

import os
import sys
from pathlib import Path

# Test if we can import the required modules
try:
    from process_image_mangaocr import process_image
    from manga_yolo_detector import detect_regions_from_path, ModelNotFoundError
    print("✓ Successfully imported process_image_mangaocr and manga_yolo_detector")
except ImportError as e:
    print(f"✗ Failed to import required modules: {e}")
    sys.exit(1)

# Test if the model exists
model_path = Path(__file__).resolve().parent / "models" / "manga109_yolo" / "model.pt"
if model_path.exists():
    print(f"✓ Manga109 YOLO model found at: {model_path}")
else:
    print(f"✗ Manga109 YOLO model NOT found at: {model_path}")
    print("  Please rerun SetupServerCondaEnvNVidia.bat to download the model")
    sys.exit(1)

# Test if image_to_process.png exists (the file the socket server expects)
test_image = "image_to_process.png"
if not os.path.exists(test_image):
    print(f"✗ Test image '{test_image}' not found")
    print("  The socket server expects this file to be present")
    print("  Place a manga image in this directory named 'image_to_process.png' to test")
    sys.exit(1)

print(f"✓ Test image found: {test_image}")

# Run the detection
print("\n--- Running Manga109 YOLO + MangaOCR detection ---")
try:
    result = process_image(test_image, lang='japan', char_level=True)
    
    if result.get("status") == "success":
        print(f"✓ Detection successful!")
        print(f"  Results: {len(result.get('results', []))} text items detected")
        print(f"  Processing time: {result.get('processing_time_seconds', 0):.2f} seconds")
        print(f"  Character-level: {result.get('char_level', False)}")
        
        # Show first few results
        results = result.get('results', [])
        if results:
            print("\n--- First 3 detected text items ---")
            for i, item in enumerate(results[:3]):
                text = item.get('text', '')
                confidence = item.get('confidence', 0)
                is_char = item.get('is_character', False)
                print(f"  {i+1}. Text: '{text}' | Confidence: {confidence:.2f} | Char: {is_char}")
        
        # Check for debug output
        debug_dir = Path(__file__).resolve().parent / "debug_outputs"
        debug_files = list(debug_dir.glob("*_mangaocr_debug*")) if debug_dir.exists() else []
        if debug_files:
            print(f"\n✓ Debug image(s) saved to: {debug_dir}")
            for f in debug_files[:3]:
                print(f"  - {f.name}")
        
        print("\n✓ All tests passed! The integration is working correctly.")
        
    elif result.get("status") == "error":
        print(f"✗ Detection failed with error: {result.get('message', 'Unknown error')}")
        sys.exit(1)
    else:
        print(f"✗ Unexpected result status: {result.get('status')}")
        sys.exit(1)
        
except ModelNotFoundError as e:
    print(f"✗ Model not found: {e}")
    print("  Please rerun SetupServerCondaEnvNVidia.bat to download the model")
    sys.exit(1)
    
except Exception as e:
    print(f"✗ Unexpected error: {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)

