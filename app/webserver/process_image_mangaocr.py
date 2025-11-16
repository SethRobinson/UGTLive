import os
import json
import time
import numpy as np
from pathlib import Path
from PIL import Image, ImageEnhance, ImageFilter
import torch
import tempfile

from color_analysis import (
    attach_color_info,
    extract_foreground_background_colors,
)

# Import the shared manga YOLO detector
from manga_yolo_detector import detect_regions_from_path, render_debug_image, ModelNotFoundError

# Debug flag: Set to True to enable YOLO debug image output to debug_outputs directory
ENABLE_YOLO_DEBUG_OUTPUT = False

# Global variables to manage OCR engines
MANGA_OCR_ENGINE = None

def initialize_manga_ocr():
    """
    Initialize or return the existing Manga OCR engine.
    Manga OCR is specifically designed for Japanese manga text recognition.
    
    Returns:
        MangaOcr: Initialized OCR engine
    """
    global MANGA_OCR_ENGINE

    if MANGA_OCR_ENGINE is None:
        # Check for GPU availability using PyTorch
        if torch.cuda.is_available():
            device_name = torch.cuda.get_device_name(0)
            print(f"GPU is available: {device_name}. Using GPU for Manga OCR.")
        else:
            print("GPU is not available. Manga OCR will use CPU.")

        print(f"Initializing Manga OCR engine...")
        start_time = time.time()

        # Import and initialize manga_ocr
        from manga_ocr import MangaOcr
        MANGA_OCR_ENGINE = MangaOcr()
        
        initialization_time = time.time() - start_time
        print(f"Manga OCR initialization completed in {initialization_time:.2f} seconds")
    else:
        print(f"Using existing Manga OCR engine")

    return MANGA_OCR_ENGINE


def preprocess_image(image):
    """
    Preprocess the image to improve OCR performance.
    This includes converting to grayscale, enhancing contrast, and reducing noise.
    
    Args:
        image (PIL.Image): Input image.
    
    Returns:
        PIL.Image: Preprocessed image.
    """
    # Convert image to grayscale
    image = image.convert('L')
    
    # Enhance contrast
    enhancer = ImageEnhance.Contrast(image)
    image = enhancer.enhance(2.0)
    
    # Apply a median filter for noise reduction
    image = image.filter(ImageFilter.MedianFilter(size=3))
    
    # Convert back to RGB (Manga OCR expects RGB)
    return image.convert('RGB')

def upscale_image(image, min_width=1024, min_height=768):
    """
    Upscale the image if its dimensions are below a specified threshold.
    
    Args:
        image (PIL.Image): Input image.
        min_width (int): Minimum desired width.
        min_height (int): Minimum desired height.
    
    Returns:
        tuple: (PIL.Image, float) Upscaled image if necessary, else original image, and the scale factor.
    """
    width, height = image.size
    scale = 1.0
    if width < min_width or height < min_height:
        scale = max(min_width / width, min_height / height)
        new_size = (int(width * scale), int(height * scale))
        print(f"Upscaling image from ({width}, {height}) to {new_size}")
        # Use LANCZOS for high-quality upscaling
        image = image.resize(new_size, Image.LANCZOS)
    return image, scale

def process_image(image_path, lang='japan', font_path='./fonts/NotoSansJP-Regular.ttf',
                  preprocess_images=False, upscale_if_needed=False, char_level=True,
                  min_region_width=10, min_region_height=10, overlap_allowed_percent=50.0):
    """
    Process an image using a hybrid approach:
    1. Use Manga109 YOLO model to detect text regions (bounding boxes)
    2. Use Manga OCR to recognize text in each detected region
    
    This gives us the best of both worlds: accurate text region detection from YOLO
    and excellent Japanese manga text recognition from Manga OCR.
    
    Args:
        image_path (str): Path to the image to process.
        lang (str): Language to use for OCR (Manga OCR only supports Japanese).
        font_path (str): Path to font file (not used by Manga OCR).
        preprocess_images (bool): Flag to determine whether to preprocess the image.
        upscale_if_needed (bool): Flag to determine whether to upscale the image if it's low resolution.
        char_level (bool): If True, split text into characters with estimated positions.
        min_region_width (int): Minimum width in pixels for YOLO-detected text regions. Smaller regions will be filtered out.
        min_region_height (int): Minimum height in pixels for YOLO-detected text regions. Smaller regions will be filtered out.
        overlap_allowed_percent (float): Maximum overlap percentage allowed between regions. If two regions overlap more than this, the smaller one will be removed.
    
    Returns:
        dict: JSON-serializable dictionary with OCR results.
    """
    # Check if image exists
    if not os.path.exists(image_path):
        return {"error": f"Image file not found: {image_path}"}

    try:
        # Start timing the OCR process
        start_time = time.time()
        
        # Open the image once for both OCR processing and color analysis
        color_image = Image.open(image_path).convert('RGB')
        image = color_image
        
        # Store original size for coordinate scaling later
        original_width, original_height = image.size
        
        # Step 1: Use Manga109 YOLO to detect text regions
        print("Step 1: Detecting text regions with Manga109 YOLO...")
        
        # Check if image is too small for YOLO detection
        # YOLO models typically need at least 320x320 pixels, but 640x640+ is better for small text
        # We'll add padding (borders) if either dimension is below 640 pixels
        MIN_DIMENSION_FOR_YOLO = 640
        yolo_image_path = image_path
        padding_x = 0  # Horizontal padding offset (left padding)
        padding_y = 0  # Vertical padding offset (top padding)
        temp_padded_file = None
        
        if original_width < MIN_DIMENSION_FOR_YOLO or original_height < MIN_DIMENSION_FOR_YOLO:
            print(f"Image is small ({original_width}x{original_height}), adding padding to meet minimum size...")
            # Calculate target dimensions (at least MIN_DIMENSION_FOR_YOLO)
            target_width = max(original_width, MIN_DIMENSION_FOR_YOLO)
            target_height = max(original_height, MIN_DIMENSION_FOR_YOLO)
            
            # Calculate padding to center the image
            padding_x = (target_width - original_width) // 2
            padding_y = (target_height - original_height) // 2
            
            print(f"Adding padding: ({original_width}x{original_height}) -> ({target_width}x{target_height}) (padding: {padding_x}px horizontal, {padding_y}px vertical)")
            
            # Create a new image with the target size, filled with white
            # Using white padding as it's neutral and won't interfere with text detection
            padded_image = Image.new('RGB', (target_width, target_height), color=(255, 255, 255))
            
            # Paste the original image centered on the padded canvas
            padded_image.paste(image, (padding_x, padding_y))
            
            # Save padded image to temporary file for YOLO
            temp_padded_file = tempfile.NamedTemporaryFile(delete=False, suffix='.png')
            padded_image.save(temp_padded_file.name, 'PNG')
            temp_padded_file.close()
            yolo_image_path = temp_padded_file.name
            print(f"Saved padded image to temporary file: {yolo_image_path}")
        
        # Set up debug output path
        debug_output_path = None
        if ENABLE_YOLO_DEBUG_OUTPUT:
            debug_dir = Path(__file__).resolve().parent / "debug_outputs"
            debug_dir.mkdir(parents=True, exist_ok=True)
            image_stem = Path(image_path).stem
            image_suffix = Path(image_path).suffix or ".png"
            debug_output_path = debug_dir / f"{image_stem}_mangaocr_debug{image_suffix}"
        
        # Run YOLO detection on (possibly padded) image
        try:
            detection_result = detect_regions_from_path(
                yolo_image_path,
                conf_threshold=0.25,
                iou_threshold=0.45,
                debug_output_path=debug_output_path
            )
        finally:
            # Clean up temporary padded file if we created one
            if temp_padded_file is not None and os.path.exists(temp_padded_file.name):
                try:
                    os.unlink(temp_padded_file.name)
                except Exception as e:
                    print(f"Warning: Failed to delete temporary padded image: {e}")
        
        detections = detection_result.get("detections", [])
        print(f"Found {len(detections)} text regions")
        
        # Step 2: Filter detections by size and prepare for overlap checking
        filtered_detections = []
        for idx, detection in enumerate(detections):
            # Get the bounding box and polygon
            bbox = detection.get("bbox")
            label = detection.get("label", "unknown")
            
            # Only process "text" regions (ignore body, face, frame)
            if label != "text":
                continue
            
            if not bbox:
                continue
            
            # Extract bounding box coordinates
            x_min = int(bbox["x_min"])
            y_min = int(bbox["y_min"])
            x_max = int(bbox["x_max"])
            y_max = int(bbox["y_max"])
            
            # Adjust coordinates to account for padding offset if we added padding
            if padding_x > 0 or padding_y > 0:
                x_min = x_min - padding_x
                y_min = y_min - padding_y
                x_max = x_max - padding_x
                y_max = y_max - padding_y
            
            # Skip regions that are too small (likely noise or furigana)
            if (x_max - x_min) < min_region_width or (y_max - y_min) < min_region_height:
                print(f"Region {idx+1}: Skipping (too small: {x_max - x_min}x{y_max - y_min} < {min_region_width}x{min_region_height})")
                continue
            
            # Ensure coordinates are within image bounds
            x_min = max(0, x_min)
            y_min = max(0, y_min)
            x_max = min(original_width, x_max)
            y_max = min(original_height, y_max)
            
            # Store detection with adjusted coordinates for overlap checking
            filtered_detections.append({
                "detection": detection,
                "x_min": x_min,
                "y_min": y_min,
                "x_max": x_max,
                "y_max": y_max,
                "width": x_max - x_min,
                "height": y_max - y_min,
                "area": (x_max - x_min) * (y_max - y_min)
            })
        
        print(f"After size filtering: {len(filtered_detections)} regions")
        
        # Step 3: Filter overlapping regions
        def calculate_overlap_percent(bbox1, bbox2):
            """Calculate overlap percentage based on the smaller region's area."""
            # Calculate intersection
            x_overlap = max(0, min(bbox1["x_max"], bbox2["x_max"]) - max(bbox1["x_min"], bbox2["x_min"]))
            y_overlap = max(0, min(bbox1["y_max"], bbox2["y_max"]) - max(bbox1["y_min"], bbox2["y_min"]))
            intersection_area = x_overlap * y_overlap
            
            if intersection_area == 0:
                return 0.0
            
            # Use the smaller region's area as the denominator
            smaller_area = min(bbox1["area"], bbox2["area"])
            if smaller_area == 0:
                return 0.0
            
            overlap_percent = (intersection_area / smaller_area) * 100.0
            return overlap_percent
        
        # Mark regions to remove based on overlap
        regions_to_remove = set()
        for i in range(len(filtered_detections)):
            if i in regions_to_remove:
                continue
            bbox1 = filtered_detections[i]
            
            for j in range(i + 1, len(filtered_detections)):
                if j in regions_to_remove:
                    continue
                bbox2 = filtered_detections[j]
                
                overlap_percent = calculate_overlap_percent(bbox1, bbox2)
                
                if overlap_percent > overlap_allowed_percent:
                    # Remove the smaller region
                    if bbox1["area"] < bbox2["area"]:
                        regions_to_remove.add(i)
                        print(f"Removing region {i+1} (overlap: {overlap_percent:.1f}%, smaller: {bbox1['width']}x{bbox1['height']})")
                        break  # bbox1 is removed, no need to check more overlaps for it
                    else:
                        regions_to_remove.add(j)
                        print(f"Removing region {j+1} (overlap: {overlap_percent:.1f}%, smaller: {bbox2['width']}x{bbox2['height']})")
        
        # Create final list of detections to process
        final_detections = []
        for idx, filtered_det in enumerate(filtered_detections):
            if idx not in regions_to_remove:
                final_detections.append(filtered_det)
        
        print(f"After overlap filtering: {len(final_detections)} regions (removed {len(regions_to_remove)} overlapping regions)")
        
        # Step 4: Initialize Manga OCR for recognition
        manga_ocr = initialize_manga_ocr()
        
        # Step 5: Process each detected region with Manga OCR
        ocr_results = []
        
        # Track color detection timing
        total_color_time_ms = 0.0
        color_detection_count = 0
        
        for idx, filtered_det in enumerate(final_detections):
            detection = filtered_det["detection"]
            x_min = filtered_det["x_min"]
            y_min = filtered_det["y_min"]
            x_max = filtered_det["x_max"]
            y_max = filtered_det["y_max"]
            
            # Get the polygon and detection confidence (bbox coordinates already extracted)
            polygon = detection.get("polygon")
            detection_confidence = detection.get("confidence", 1.0)
            
            # Crop the region from the image
            try:
                cropped_region = image.crop((x_min, y_min, x_max, y_max))
                
                # Use Manga OCR to recognize text in this region
                manga_text = manga_ocr(cropped_region)
                text = manga_text.strip()
                
                # Skip if no text detected
                if not text:
                    print(f"Region {idx+1}: No text detected by MangaOCR")
                    continue
                
                print(f"Region {idx+1}: MangaOCR='{text}' (confidence: {detection_confidence:.2f})")
                
            except Exception as e:
                print(f"Error processing region {idx+1} with Manga OCR: {e}")
                continue
            
            # Use the polygon if available, otherwise create from bbox (truncate to integers)
            if polygon:
                # Adjust polygon coordinates to account for padding offset if we added padding
                if padding_x > 0 or padding_y > 0:
                    box_native = [
                        [int(point[0]) - padding_x, int(point[1]) - padding_y] for point in polygon
                    ]
                else:
                    box_native = [
                        [int(point[0]), int(point[1])] for point in polygon
                    ]
            else:
                box_native = [
                    [int(x_min), int(y_min)],
                    [int(x_max), int(y_min)],
                    [int(x_max), int(y_max)],
                    [int(x_min), int(y_max)]
                ]

            # Time color detection
            color_start_time = time.perf_counter()
            color_info = extract_foreground_background_colors(color_image, box_native)
            color_time_ms = (time.perf_counter() - color_start_time) * 1000
            if color_info:
                total_color_time_ms += color_time_ms
                color_detection_count += 1
            
            # Split into characters if requested
            if char_level and len(text) > 1:
                char_results = split_into_characters(text, box_native, detection_confidence)
                for char_entry in char_results:
                    attach_color_info(char_entry, color_info)
                ocr_results.extend(char_results)
            else:
                result_entry = {
                    "rect": box_native,
                    "text": text,
                    "confidence": detection_confidence,
                    "is_character": False,
                    "text_orientation": "vertical"
                }
                attach_color_info(result_entry, color_info)
                ocr_results.append(result_entry)
        
        # Calculate processing time
        processing_time = time.time() - start_time
        
        # Print color detection summary
        if color_detection_count > 0:
            total_color_time_sec = total_color_time_ms / 1000.0
            print(f"MareArts XColor VX - {color_detection_count} objects - {total_color_time_sec:.1f} seconds")
        
        print(f"Manga OCR completed: {len(ocr_results)} results in {processing_time:.2f}s")
        if debug_output_path:
            print(f"Debug image saved to: {debug_output_path}")
        
        return {
            "status": "success",
            "results": ocr_results,
            "processing_time_seconds": float(processing_time),
            "char_level": char_level
        }
    
    except ModelNotFoundError as e:
        print(f"Manga109 YOLO model not found: {e}")
        return {
            "status": "error",
            "message": (
                "Manga109 YOLO model not found. Please rerun SetupServerCondaEnvNVidia.bat "
                f"to download the required assets. Error: {str(e)}"
            )
        }
    
    except Exception as e:
        import traceback
        traceback.print_exc()
        return {
            "status": "error",
            "message": str(e)
        }

def split_into_characters(text, box, confidence):
    """
    Split a text string into individual characters with estimated bounding boxes.
    
    Args:
        text (str): The text to split.
        box (list): Bounding box for the entire text as [[x1,y1],[x2,y2],[x3,y3],[x4,y4]].
        confidence (float): Confidence score for the text.
    
    Returns:
        list: List of dictionary items for each character with its estimated position.
    """
    if not text or len(text) <= 1:
        return [{
            "rect": box,
            "text": text,
            "confidence": confidence,
            "is_character": True,
            "text_orientation": "vertical"
        }]
    
    char_results = []
    
    # Extract coordinates from the box
    tl = box[0]  # top-left
    tr = box[1]  # top-right
    br = box[2]  # bottom-right
    bl = box[3]  # bottom-left
    
    # Calculate width and height
    width = ((tr[0] - tl[0]) + (br[0] - bl[0])) / 2
    height = ((bl[1] - tl[1]) + (br[1] - tr[1])) / 2
    
    # Calculate average character width
    char_width = width / len(text)
    
    # Calculate starting positions for left-to-right text
    # This is a simplification assuming text is horizontal and left-to-right
    start_x_top = tl[0]
    start_x_bottom = bl[0]
    
    # Calculate x-increments between characters
    x_increment_top = (tr[0] - tl[0]) / len(text)
    x_increment_bottom = (br[0] - bl[0]) / len(text)
    
    # Generate character boxes
    for i, char in enumerate(text):
        # Calculate the four corners of this character's bounding box
        x1_top = start_x_top + (i * x_increment_top)
        x2_top = start_x_top + ((i + 1) * x_increment_top)
        x1_bottom = start_x_bottom + (i * x_increment_bottom)
        x2_bottom = start_x_bottom + ((i + 1) * x_increment_bottom)
        
        # Interpolate y-coordinates (handle any slope)
        y_top_left = tl[1] + ((tr[1] - tl[1]) * (i / len(text)))
        y_top_right = tl[1] + ((tr[1] - tl[1]) * ((i + 1) / len(text)))
        y_bottom_left = bl[1] + ((br[1] - bl[1]) * (i / len(text)))
        y_bottom_right = bl[1] + ((br[1] - bl[1]) * ((i + 1) / len(text)))
        
        # Create the character bounding box (truncate to integers)
        char_box = [
            [int(x1_top), int(y_top_left)],       # top-left
            [int(x2_top), int(y_top_right)],      # top-right
            [int(x2_bottom), int(y_bottom_right)], # bottom-right
            [int(x1_bottom), int(y_bottom_left)]   # bottom-left
        ]
        
        # Add to results
        char_results.append({
            "rect": char_box,
            "text": char,
            "confidence": confidence,
            "is_character": True,
            "text_orientation": "vertical"
        })
    
    return char_results