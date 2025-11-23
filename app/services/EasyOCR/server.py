"""FastAPI server for EasyOCR service."""

import sys
import os
import time
import asyncio
from pathlib import Path
from io import BytesIO
from typing import List, Dict, Optional

import uvicorn
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
import numpy as np
from PIL import Image
import torch
import easyocr

# Add shared folder to path
shared_dir = Path(__file__).parent.parent / "shared"
print(f"[DEBUG] Script location: {Path(__file__).absolute()}")
print(f"[DEBUG] Shared directory: {shared_dir.absolute()}")
print(f"[DEBUG] Shared directory exists: {shared_dir.exists()}")
if shared_dir.exists():
    print(f"[DEBUG] Files in shared: {list(shared_dir.glob('*.py'))}")
sys.path.insert(0, str(shared_dir))
print(f"[DEBUG] sys.path[0]: {sys.path[0]}")

from config_parser import parse_service_config, get_config_value
from response_models import OCRResponse, ErrorResponse, ServiceInfo, ShutdownResponse, TextObject, ColorInfo
from color_analysis import extract_foreground_background_colors, attach_color_info

# Load service configuration
config_path = Path(__file__).parent / "service_config.txt"
SERVICE_CONFIG = parse_service_config(str(config_path))

# Get service settings
SERVICE_NAME = get_config_value(SERVICE_CONFIG, 'service_name', 'EasyOCR')
SERVICE_PORT = int(get_config_value(SERVICE_CONFIG, 'port', '5000'))
SERVICE_VERSION = get_config_value(SERVICE_CONFIG, 'version', '1.7.2')

# Initialize FastAPI app
app = FastAPI(title=SERVICE_NAME, version=SERVICE_VERSION)

# Global OCR engine
OCR_ENGINE = None
CURRENT_LANG = None


def initialize_ocr_engine(lang: str = 'japan'):
    """Initialize or reinitialize the OCR engine with the specified language."""
    global OCR_ENGINE, CURRENT_LANG

    # Map language codes to EasyOCR language codes
    lang_map = {
        'japan': 'ja',
        'korean': 'ko',
        'chinese': 'ch_sim',
        'english': 'en',
        'vietnamese': 'vi'
    }

    # Use mapped language or default to input if not in map
    easy_lang = lang_map.get(lang, lang)

    # Only reinitialize if language has changed
    if OCR_ENGINE is None or CURRENT_LANG != lang:
        # Check for GPU availability
        if torch.cuda.is_available():
            device_name = torch.cuda.get_device_name(0)
            print(f"GPU is available: {device_name}. Using GPU for OCR.")
        else:
            print("GPU is not available. EasyOCR will use CPU.")

        print(f"Initializing EasyOCR engine with language: {easy_lang}...")
        start_time = time.time()

        # For Japanese, we also add English as a secondary language
        languages = [easy_lang]
        if easy_lang == 'ja':
            languages.append('en')

        OCR_ENGINE = easyocr.Reader(languages, gpu=True)
        CURRENT_LANG = lang
        initialization_time = time.time() - start_time
        print(f"EasyOCR initialization completed in {initialization_time:.2f} seconds")
    else:
        print(f"Using existing EasyOCR engine with language: {easy_lang}")

    return OCR_ENGINE


def detect_text_orientation() -> str:
    """
    Return text orientation.
    
    Returns:
        "horizontal" - Always returns horizontal. Vertical text should only be detected
        by OCR systems with built-in vertical detection (like MangaOCR).
        Width/height aspect ratio comparison is unreliable.
    """
    return "horizontal"


def process_ocr_results(image: Image.Image, results: list) -> List[Dict]:
    """Process EasyOCR results into standardized format."""
    text_objects = []
    
    for detection in results:
        if not detection or len(detection) < 2:
            continue
            
        bbox = detection[0]
        text = detection[1]
        confidence = detection[2] if len(detection) > 2 else None
        
        # Calculate bounding box
        xs = [point[0] for point in bbox]
        ys = [point[1] for point in bbox]
        
        x = int(min(xs))
        y = int(min(ys))
        width = int(max(xs) - min(xs))
        height = int(max(ys) - min(ys))
        
        # Convert vertices to integer lists
        vertices = [[int(p[0]), int(p[1])] for p in bbox]
        
        # Extract color information
        color_info = None
        try:
            color_data = extract_foreground_background_colors(image, bbox)
            if color_data:
                color_info = color_data
        except Exception as e:
            print(f"Color extraction failed: {e}")
        
        # Detect text orientation
        text_orientation = detect_text_orientation()
        
        # Build text object
        text_obj = {
            "text": text,
            "x": x,
            "y": y,
            "width": width,
            "height": height,
            "vertices": vertices,
            "confidence": confidence,
            "text_orientation": text_orientation
        }
        
        # Attach color information if available
        if color_info:
            attach_color_info(text_obj, color_info)
        
        text_objects.append(text_obj)
    
    return text_objects


@app.post("/process")
async def process_image(request: Request):
    """
    Process an image for OCR.
    
    Expects binary image data in the request body.
    Query parameters:
    - lang: Language code (default: 'japan')
    """
    try:
        start_time = time.time()
        
        # Get query parameters
        lang = request.query_params.get('lang', 'japan')
        
        # Read binary image data
        image_bytes = await request.body()
        if not image_bytes:
            raise HTTPException(status_code=400, detail="No image data provided")
        
        # Load image from binary data
        image = Image.open(BytesIO(image_bytes)).convert('RGB')
        
        # Initialize OCR engine
        reader = initialize_ocr_engine(lang)
        
        # Perform OCR with stricter parameters to reduce merging
        # height_ths: Maximum difference in box height. Low value (0.1) prevents merging lines of different sizes.
        # ycenter_ths: Maximum shift in y-direction. Low value (0.1) prevents merging lines at different vertical positions.
        # paragraph: False disables paragraph grouping, keeping lines separate.
        results = reader.readtext(
            np.array(image),
            paragraph=False,
            height_ths=0.1,
            ycenter_ths=0.1
        )
        
        # Process results
        text_objects = process_ocr_results(image, results)
        
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


@app.post("/analyze_color")
async def analyze_color(request: Request):
    """
    Analyze image for foreground/background colors.
    
    Expects binary image data in the request body.
    """
    try:
        # Read binary image data
        image_bytes = await request.body()
        if not image_bytes:
            raise HTTPException(status_code=400, detail="No image data provided")
        
        # Load image from binary data
        image = Image.open(BytesIO(image_bytes)).convert('RGB')
        
        # Use the whole image as the region
        width, height = image.size
        bbox = [[0, 0], [width, 0], [width, height], [0, height]]
        
        # Extract colors
        color_info = extract_foreground_background_colors(image, bbox)
        
        if not color_info:
             return JSONResponse(content={
                "status": "success", 
                "color_info": None
            })
            
        return JSONResponse(content={
            "status": "success",
            "color_info": color_info
        })
        
    except Exception as e:
        print(f"Error analyzing color: {e}")
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
        "service_name": get_config_value(SERVICE_CONFIG, 'service_name', 'EasyOCR'),
        "description": get_config_value(SERVICE_CONFIG, 'description', ''),
        "version": get_config_value(SERVICE_CONFIG, 'version', '1.7.2'),
        "conda_env_name": get_config_value(SERVICE_CONFIG, 'conda_env_name', 'ugt_easyocr'),
        "port": int(get_config_value(SERVICE_CONFIG, 'port', '5000')),
        "local_only": get_config_value(SERVICE_CONFIG, 'local_only', 'true') == 'true',
        "github_url": get_config_value(SERVICE_CONFIG, 'github_url', ''),
        "author": get_config_value(SERVICE_CONFIG, 'author', '')
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
    """Pre-load OCR model at startup to avoid delay on first request."""
    print("=" * 60)
    print("PRE-LOADING OCR MODEL AT STARTUP")
    print("=" * 60)
    
    # Pre-load OCR engine with English (fastest and most universal)
    # Will reinitialize on first request if different language is needed
    try:
        initialize_ocr_engine('english')
        print("✓ OCR model pre-loaded successfully (English)")
        print("  Note: Model will reinitialize if different language is requested")
    except Exception as e:
        print(f"✗ Failed to pre-load OCR model: {e}")
        print("Model will be loaded on first request instead.")
    
    # Pre-load color extractor
    try:
        from color_analysis import _get_color_extractor
        _get_color_extractor()
        print("✓ Color extractor pre-loaded successfully")
    except Exception as e:
        print(f"✗ Failed to pre-load color extractor: {e}")
    
    print("✓ All models ready - service is ready for requests!")
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

