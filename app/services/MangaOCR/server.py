"""FastAPI server for MangaOCR service with YOLO text detection."""

import sys
import os
import time
import asyncio
import tempfile
from pathlib import Path
from io import BytesIO
from typing import List, Dict, Optional

# Fix SSL certificate verification for urllib.request
# This is needed because bundled Python may not have access to system certificates
# Must be done BEFORE any imports that might trigger model downloads
import ssl
import certifi
ssl._create_default_https_context = lambda: ssl.create_default_context(cafile=certifi.where())

import uvicorn
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
import numpy as np
from PIL import Image
import torch

# Add local MangaOCR folder to path first (for manga_yolo_detector)
local_dir = Path(__file__).parent
sys.path.insert(0, str(local_dir))

# Add shared folder to path (for common utilities)
shared_dir = Path(__file__).parent.parent / "shared"
print(f"[DEBUG] Script location: {Path(__file__).absolute()}")
print(f"[DEBUG] Shared directory: {shared_dir.absolute()}")
print(f"[DEBUG] Shared directory exists: {shared_dir.exists()}")
if shared_dir.exists():
    print(f"[DEBUG] Files in shared: {list(shared_dir.glob('*.py'))}")
sys.path.insert(1, str(shared_dir))
print(f"[DEBUG] sys.path[0]: {sys.path[0]}")
print(f"[DEBUG] sys.path[1]: {sys.path[1]}")

from config_parser import parse_service_config, get_config_value
from response_models import OCRResponse, ErrorResponse, ServiceInfo, ShutdownResponse, TextObject
from color_analysis import extract_foreground_background_colors, attach_color_info
# Import manga_yolo_detector from local MangaOCR directory
from manga_yolo_detector import detect_regions_from_path, ModelNotFoundError

# Load service configuration
config_path = Path(__file__).parent / "service_config.txt"
SERVICE_CONFIG = parse_service_config(str(config_path))

# Get service settings
SERVICE_NAME = get_config_value(SERVICE_CONFIG, 'service_name', 'MangaOCR')
SERVICE_PORT = int(get_config_value(SERVICE_CONFIG, 'port', '5001'))
SERVICE_INSTALL_VERSION = get_config_value(SERVICE_CONFIG, 'service_install_version', '1')

# Initialize FastAPI app
app = FastAPI(title=SERVICE_NAME, version=SERVICE_INSTALL_VERSION)

# Global OCR engine
MANGA_OCR_ENGINE = None


def initialize_manga_ocr():
    """Initialize or return the existing Manga OCR engine."""
    global MANGA_OCR_ENGINE

    if MANGA_OCR_ENGINE is None:
        # Check for GPU availability
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


def calculate_overlap_percent(bbox1: Dict, bbox2: Dict) -> float:
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


def detect_text_orientation(width: int, height: int, aspect_ratio_threshold: float = 1.2) -> str:
    """
    Detect text orientation based on bounding box dimensions.
    
    Args:
        width: Width of the text bounding box
        height: Height of the text bounding box
        aspect_ratio_threshold: Threshold for determining orientation (default: 1.2)
    
    Returns:
        "vertical" if height > width * threshold, "horizontal" otherwise
    """
    if width == 0:
        return "vertical"
    
    if height == 0:
        return "horizontal"
    
    aspect_ratio = height / width
    
    # For very small regions, use a more lenient threshold
    # Short vertical text might not meet the standard threshold
    area = width * height
    if area < 2000:  # Small regions (roughly < 45x45 pixels)
        # Use a lower threshold for small regions to catch short vertical phrases
        if aspect_ratio > 1.1:
            return "vertical"
        elif aspect_ratio < 0.9:
            return "horizontal"
        # For ambiguous small regions (0.9-1.1), prefer vertical for Japanese manga
        return "vertical"
    
    # Standard detection for larger regions
    # If height is significantly greater than width, it's likely vertical text
    if aspect_ratio > aspect_ratio_threshold:
        return "vertical"
    elif aspect_ratio < (1.0 / aspect_ratio_threshold):
        # If width is significantly greater than height, it's horizontal
        return "horizontal"
    else:
        # Ambiguous case: slightly prefer vertical for Japanese manga context
        # Most manga text is vertical, so default to vertical when uncertain
        return "vertical"


def process_manga_ocr(
    image: Image.Image,
    min_region_width: int = 10,
    min_region_height: int = 10,
    overlap_allowed_percent: float = 50.0,
    yolo_confidence: float = 0.60
) -> List[Dict]:
    """Process image using YOLO detection + Manga OCR."""
    
    # Save image to temporary file for YOLO processing
    with tempfile.NamedTemporaryFile(delete=False, suffix='.png') as temp_file:
        image.save(temp_file.name, 'PNG')
        temp_image_path = temp_file.name
    
    try:
        original_width, original_height = image.size
        
        # Step 1: Detect text regions with YOLO
        MIN_DIMENSION_FOR_YOLO = 640
        yolo_image_path = temp_image_path
        padding_x = 0
        padding_y = 0
        temp_padded_file = None
        
        # Add padding if image is too small
        if original_width < MIN_DIMENSION_FOR_YOLO or original_height < MIN_DIMENSION_FOR_YOLO:
            target_width = max(original_width, MIN_DIMENSION_FOR_YOLO)
            target_height = max(original_height, MIN_DIMENSION_FOR_YOLO)
            
            padding_x = (target_width - original_width) // 2
            padding_y = (target_height - original_height) // 2
            
            padded_image = Image.new('RGB', (target_width, target_height), color=(255, 255, 255))
            padded_image.paste(image, (padding_x, padding_y))
            
            temp_padded_file = tempfile.NamedTemporaryFile(delete=False, suffix='.png')
            padded_image.save(temp_padded_file.name, 'PNG')
            yolo_image_path = temp_padded_file.name
        
        # Run YOLO detection
        try:
            detection_result = detect_regions_from_path(
                yolo_image_path,
                conf_threshold=yolo_confidence,
                iou_threshold=0.45
            )
        finally:
            if temp_padded_file is not None and os.path.exists(temp_padded_file.name):
                try:
                    os.unlink(temp_padded_file.name)
                except:
                    pass
        
        detections = detection_result.get("detections", [])
        print(f"Found {len(detections)} text regions")
        
        # Step 2: Filter detections by size
        filtered_detections = []
        for detection in detections:
            bbox = detection.get("bbox")
            label = detection.get("label", "unknown")
            
            # Only process "text" regions
            if label != "text" or not bbox:
                continue
            
            x_min = int(bbox["x_min"])
            y_min = int(bbox["y_min"])
            x_max = int(bbox["x_max"])
            y_max = int(bbox["y_max"])
            
            # Adjust for padding
            if padding_x > 0 or padding_y > 0:
                x_min -= padding_x
                y_min -= padding_y
                x_max -= padding_x
                y_max -= padding_y
            
            # Skip too-small regions
            if (x_max - x_min) < min_region_width or (y_max - y_min) < min_region_height:
                continue
            
            # Clip to image bounds
            x_min = max(0, x_min)
            y_min = max(0, y_min)
            x_max = min(original_width, x_max)
            y_max = min(original_height, y_max)
            
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
        
        # Step 3: Remove overlapping regions
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
                    # Remove smaller region
                    if bbox1["area"] < bbox2["area"]:
                        regions_to_remove.add(i)
                        break
                    else:
                        regions_to_remove.add(j)
        
        final_detections = [d for i, d in enumerate(filtered_detections) if i not in regions_to_remove]
        print(f"After overlap filtering: {len(final_detections)} regions")
        
        # Step 4: Run Manga OCR on each region
        manga_ocr = initialize_manga_ocr()
        text_objects = []
        
        for detection_info in final_detections:
            x_min = detection_info["x_min"]
            y_min = detection_info["y_min"]
            x_max = detection_info["x_max"]
            y_max = detection_info["y_max"]
            
            # Crop region
            region_img = image.crop((x_min, y_min, x_max, y_max))
            
            # Perform OCR on region
            try:
                text = manga_ocr(region_img)
                
                if text and text.strip():
                    # Extract colors
                    polygon = [
                        [x_min, y_min],
                        [x_max, y_min],
                        [x_max, y_max],
                        [x_min, y_max]
                    ]
                    
                    color_info = None
                    try:
                        color_data = extract_foreground_background_colors(image, polygon)
                        if color_data:
                            color_info = color_data
                    except Exception as e:
                        print(f"Color extraction failed: {e}")
                    
                    # Detect text orientation
                    text_orientation = detect_text_orientation(x_max - x_min, y_max - y_min)
                    
                    # Return as single block
                    text_obj = {
                        "text": text,
                        "x": x_min,
                        "y": y_min,
                        "width": x_max - x_min,
                        "height": y_max - y_min,
                        "vertices": polygon,
                        "confidence": None,
                        "text_orientation": text_orientation
                    }
                    if color_info:
                        attach_color_info(text_obj, color_info)
                    text_objects.append(text_obj)
                        
            except Exception as e:
                print(f"OCR failed for region: {e}")
        
        return text_objects
        
    finally:
        # Cleanup temporary file
        if os.path.exists(temp_image_path):
            try:
                os.unlink(temp_image_path)
            except:
                pass


@app.post("/process")
async def process_image(request: Request):
    """
    Process an image for OCR using YOLO detection + Manga OCR.
    
    Query parameters:
    - lang: Language code (default: 'japan', MangaOCR only supports Japanese)
    - min_region_width: Minimum region width in pixels (default: 10)
    - min_region_height: Minimum region height in pixels (default: 10)
    - overlap_allowed_percent: Maximum overlap percentage allowed (default: 50.0)
    - yolo_confidence: YOLO confidence threshold for text detection (default: 0.60)
    """
    try:
        start_time = time.time()
        
        # Get query parameters
        lang = request.query_params.get('lang', 'japan')
        min_region_width = int(request.query_params.get('min_region_width', '10'))
        min_region_height = int(request.query_params.get('min_region_height', '10'))
        overlap_allowed_percent = float(request.query_params.get('overlap_allowed_percent', '50.0'))
        yolo_confidence = float(request.query_params.get('yolo_confidence', '0.60'))
        
        # Read binary image data
        image_bytes = await request.body()
        if not image_bytes:
            raise HTTPException(status_code=400, detail="No image data provided")
        
        # Load image
        image = Image.open(BytesIO(image_bytes)).convert('RGB')
        
        # Process with MangaOCR
        text_objects = process_manga_ocr(
            image,
            min_region_width=min_region_width,
            min_region_height=min_region_height,
            overlap_allowed_percent=overlap_allowed_percent,
            yolo_confidence=yolo_confidence
        )
        
        # Calculate processing time
        processing_time = time.time() - start_time
        
        # Determine backend
        backend = "gpu" if torch.cuda.is_available() else "cpu"
        
        # Build response
        response = {
            "status": "success",
            "texts": text_objects,
            "processing_time": processing_time,
            "language": lang,
            "char_level": False,
            "backend": backend
        }
        
        return JSONResponse(content=response)
        
    except ModelNotFoundError as e:
        print(f"YOLO model error: {e}")
        return JSONResponse(
            status_code=500,
            content={
                "status": "error",
                "message": str(e),
                "error_type": "ModelNotFoundError"
            }
        )
    except Exception as e:
        print(f"Error processing image: {e}")
        return JSONResponse(
            status_code=500,
            content={
                "status": "error",
                "message": str(e),
                "error_type": type(e).__name__
            }
        )


@app.get("/info")
async def get_info():
    """Get service information."""
    info = {
        "service_name": get_config_value(SERVICE_CONFIG, 'service_name', 'MangaOCR'),
        "description": get_config_value(SERVICE_CONFIG, 'description', ''),
        "service_install_version": get_config_value(SERVICE_CONFIG, 'service_install_version', '1'),
        "venv_name": get_config_value(SERVICE_CONFIG, 'venv_name', 'ugt_mangaocr'),
        "port": int(get_config_value(SERVICE_CONFIG, 'port', '5001')),
        "server_url": get_config_value(SERVICE_CONFIG, 'server_url', 'http://127.0.0.1'),
        "local_only": get_config_value(SERVICE_CONFIG, 'local_only', 'true') == 'true',
        "github_url": get_config_value(SERVICE_CONFIG, 'github_url', ''),
        "service_author": get_config_value(SERVICE_CONFIG, 'service_author', '')
    }
    return JSONResponse(content=info)


@app.post("/shutdown")
async def shutdown():
    """Shutdown the service."""
    print("Shutdown request received...")
    
    async def shutdown_task():
        await asyncio.sleep(1)
        os._exit(0)
    
    asyncio.create_task(shutdown_task())
    
    return JSONResponse(content={
        "status": "success",
        "message": "Service shutting down"
    })



@app.on_event("startup")
async def startup_event():
    """Pre-load OCR and YOLO models at startup to avoid delay on first request."""
    print("=" * 60)
    print("PRE-LOADING MANGA OCR AND YOLO MODELS AT STARTUP")
    print("=" * 60)
    
    # Pre-load Manga OCR engine
    try:
        initialize_manga_ocr()
        print("[OK] Manga OCR model pre-loaded successfully")
    except Exception as e:
        print(f"[FAIL] Failed to pre-load Manga OCR model: {e}")
        print("Model will be loaded on first request instead.")
    
    # Pre-load YOLO detector
    try:
        from manga_yolo_detector import _ensure_model
        _ensure_model()
        print("[OK] YOLO text detection model pre-loaded successfully")
    except Exception as e:
        print(f"[FAIL] Failed to pre-load YOLO model: {e}")
        print("Model will be loaded on first request instead.")
    
    # Pre-load color extractor
    try:
        from color_analysis import _get_color_extractor
        _get_color_extractor()
        print("[OK] Color extractor pre-loaded successfully")
    except Exception as e:
        print(f"[FAIL] Failed to pre-load color extractor: {e}")
    
    print("[OK] All models ready - service is ready for requests!")
    print("=" * 60)


if __name__ == "__main__":
    host = "127.0.0.1" if get_config_value(SERVICE_CONFIG, 'local_only', 'true') == 'true' else "0.0.0.0"
    
    print(f"Starting {SERVICE_NAME} service on {host}:{SERVICE_PORT}")
    print(f"Configuration: {SERVICE_CONFIG}")
    
    uvicorn.run(
        app,
        host=host,
        port=SERVICE_PORT,
        log_level="info",
        access_log=True
    )

