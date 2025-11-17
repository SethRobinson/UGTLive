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


def process_ocr_results(image: Image.Image, results: list, char_level: bool = True) -> List[Dict]:
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
        
        # Build text object
        text_obj = {
            "text": text,
            "x": x,
            "y": y,
            "width": width,
            "height": height,
            "vertices": vertices,
            "confidence": confidence
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
    - char_level: Whether to use character-level detection (default: true)
    """
    try:
        start_time = time.time()
        
        # Get query parameters
        lang = request.query_params.get('lang', 'japan')
        char_level = request.query_params.get('char_level', 'true').lower() == 'true'
        
        # Read binary image data
        image_bytes = await request.body()
        if not image_bytes:
            raise HTTPException(status_code=400, detail="No image data provided")
        
        # Load image from binary data
        image = Image.open(BytesIO(image_bytes)).convert('RGB')
        
        # Initialize OCR engine
        reader = initialize_ocr_engine(lang)
        
        # Perform OCR
        results = reader.readtext(np.array(image))
        
        # Process results
        text_objects = process_ocr_results(image, results, char_level)
        
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
            "char_level": char_level,
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


@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return JSONResponse(content={
        "status": "healthy",
        "service": SERVICE_NAME,
        "version": SERVICE_VERSION
    })


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

