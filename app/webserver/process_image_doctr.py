import os
import json
import time
import numpy as np
from pathlib import Path
from PIL import Image
import torch

from color_analysis import (
    attach_color_info,
    extract_foreground_background_colors,
)

# Global variables to manage OCR engine
DOCTR_PREDICTOR = None

def initialize_doctr():
    """
    Initialize or return the existing docTR OCR predictor.
    Uses db_resnet50 for text detection and crnn_mobilenet_v3_large for text recognition.
    
    Returns:
        OCRPredictor: Initialized docTR predictor
    """
    global DOCTR_PREDICTOR

    if DOCTR_PREDICTOR is None:
        # Check for GPU availability using PyTorch
        if torch.cuda.is_available():
            device_name = torch.cuda.get_device_name(0)
            print(f"GPU is available: {device_name}. Using GPU for docTR.")
        else:
            print("GPU is not available. docTR will use CPU.")

        print(f"Initializing docTR OCR predictor...")
        start_time = time.time()

        # Import and initialize docTR
        from doctr.models import ocr_predictor
        
        DOCTR_PREDICTOR = ocr_predictor(
            det_arch='db_resnet50',
            reco_arch='crnn_mobilenet_v3_large',
            pretrained=True
        )
        
        initialization_time = time.time() - start_time
        print(f"docTR initialization completed in {initialization_time:.2f} seconds")
    else:
        print(f"Using existing docTR predictor")

    return DOCTR_PREDICTOR

def process_image(image_path, lang='japan', font_path='./fonts/NotoSansJP-Regular.ttf',
                  preprocess_images=False, upscale_if_needed=False, char_level=True):
    """
    Process an image using docTR and return the OCR results.
    
    Args:
        image_path (str): Path to the image to process.
        lang (str): Language hint (currently not used by docTR - the crnn_mobilenet_v3_large 
                   model is multilingual and automatically supports Japanese, English, and other 
                   languages without explicit language specification).
        font_path (str): Path to font file (not used by docTR).
        preprocess_images (bool): Flag to determine whether to preprocess the image (not used).
        upscale_if_needed (bool): Flag to determine whether to upscale the image (not used).
        char_level (bool): If True, split text into characters with estimated positions.
    
    Returns:
        dict: JSON-serializable dictionary with OCR results.
    
    Note:
        docTR's default crnn_mobilenet_v3_large recognition model is multilingual and supports
        Japanese automatically. No special language mode configuration is needed (unlike EasyOCR
        which requires language specification). The lang parameter is accepted for API consistency
        but does not affect docTR's behavior.
    """
    # Check if image exists
    if not os.path.exists(image_path):
        return {"error": f"Image file not found: {image_path}"}

    try:
        # Start timing the OCR process
        start_time = time.time()
        
        # Open the image in RGB for color analysis
        color_image = Image.open(image_path).convert('RGB')
        
        # Store original size for coordinate conversion
        original_width, original_height = color_image.size
        
        # Initialize docTR predictor
        predictor = initialize_doctr()
        
        # Convert PIL Image to numpy array for docTR
        img_array = np.array(color_image)
        
        # Process image with docTR
        # docTR expects images as numpy arrays
        result_doc = predictor([img_array])
        
        # Calculate processing time
        processing_time = time.time() - start_time
        
        # Extract results from docTR Document structure
        # Use export() method to get structured data with geometry
        exported_data = result_doc.export()
        
        ocr_results = []
        
        # Track color detection timing
        total_color_time_ms = 0.0
        color_detection_count = 0
        
        # docTR export format: pages -> blocks -> lines -> words
        # Use line-level geometry to create separate blocks for each text blob
        # Each line becomes its own block, which works better for manga/comics with separate speech bubbles
        if exported_data and 'pages' in exported_data and len(exported_data['pages']) > 0:
            for page in exported_data['pages']:
                if 'blocks' not in page:
                    continue
                for block in page['blocks']:
                    if 'lines' not in block:
                        continue
                    for line in block['lines']:
                        # Extract line-level geometry - docTR uses normalized coordinates (0-1)
                        line_geometry = line.get('geometry')
                        
                        if not line_geometry:
                            # Skip lines without geometry
                            continue
                        
                        # Combine all words in the line into a single text string
                        words = line.get('words', [])
                        if not words:
                            continue
                        
                        # Combine word texts with spaces
                        line_text_parts = []
                        line_confidences = []
                        for word in words:
                            word_text = word.get('value', '')
                            if word_text:
                                line_text_parts.append(word_text)
                                line_confidences.append(float(word.get('confidence', 1.0)))
                        
                        if not line_text_parts:
                            continue
                        
                        # Join words with spaces to form the line text
                        line_text = ' '.join(line_text_parts)
                        
                        # Use average confidence of words in the line
                        line_confidence = sum(line_confidences) / len(line_confidences) if line_confidences else 1.0
                        
                        # Convert line geometry from normalized coordinates to pixel coordinates
                        try:
                            if isinstance(line_geometry, (tuple, list)) and len(line_geometry) >= 2:
                                # Get normalized coordinates
                                if isinstance(line_geometry[0], (tuple, list)) and len(line_geometry[0]) == 2:
                                    # Format: ((x1, y1), (x2, y2))
                                    norm_x1, norm_y1 = float(line_geometry[0][0]), float(line_geometry[0][1])
                                    norm_x2, norm_y2 = float(line_geometry[1][0]), float(line_geometry[1][1])
                                elif len(line_geometry) == 4:
                                    # Alternative format: (x1, y1, x2, y2)
                                    norm_x1, norm_y1 = float(line_geometry[0]), float(line_geometry[1])
                                    norm_x2, norm_y2 = float(line_geometry[2]), float(line_geometry[3])
                                else:
                                    print(f"Unexpected line geometry format: {line_geometry}")
                                    continue
                                
                                # Convert to pixel coordinates
                                x1 = norm_x1 * original_width
                                y1 = norm_y1 * original_height
                                x2 = norm_x2 * original_width
                                y2 = norm_y2 * original_height
                                
                                # Create 4-point polygon (assuming rectangular box)
                                box_native = [
                                    [x1, y1],  # top-left
                                    [x2, y1],  # top-right
                                    [x2, y2],  # bottom-right
                                    [x1, y2]   # bottom-left
                                ]
                            else:
                                print(f"Unexpected line geometry format: {line_geometry}")
                                continue
                        except Exception as e:
                            print(f"Error extracting line geometry: {e}, geometry: {line_geometry}")
                            continue
                        
                        # Extract color information for the line
                        color_start_time = time.perf_counter()
                        color_info = extract_foreground_background_colors(color_image, box_native)
                        color_time_ms = (time.perf_counter() - color_start_time) * 1000
                        if color_info:
                            total_color_time_ms += color_time_ms
                            color_detection_count += 1
                        
                        # Return line-level block - each line is a separate text blob
                        result_entry = {
                            "rect": box_native,
                            "text": line_text,
                            "confidence": line_confidence,
                            "is_character": False,
                            "text_orientation": "horizontal"
                        }
                        attach_color_info(result_entry, color_info)
                        ocr_results.append(result_entry)
        
        # Print color detection summary
        if color_detection_count > 0:
            total_color_time_sec = total_color_time_ms / 1000.0
            print(f"UGT CuPy Color VX - {color_detection_count} objects - {total_color_time_sec:.1f} seconds")
        
        print(f"docTR completed: {len(ocr_results)} results in {processing_time:.2f}s")
        
        return {
            "status": "success",
            "results": ocr_results,
            "processing_time_seconds": float(processing_time),
            "char_level": char_level
        }
    
    except Exception as e:
        import traceback
        traceback.print_exc()
        return {
            "status": "error",
            "message": str(e)
        }


