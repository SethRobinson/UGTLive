"""FastAPI server for docTR service."""

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
from response_models import OCRResponse, ErrorResponse, ServiceInfo, ShutdownResponse, TextObject
from color_analysis import extract_foreground_background_colors, attach_color_info

# Load service configuration
config_path = Path(__file__).parent / "service_config.txt"
SERVICE_CONFIG = parse_service_config(str(config_path))

# Get service settings
SERVICE_NAME = get_config_value(SERVICE_CONFIG, 'service_name', 'docTR')
SERVICE_PORT = int(get_config_value(SERVICE_CONFIG, 'port', '5002'))
SERVICE_VERSION = get_config_value(SERVICE_CONFIG, 'version', '0.10.0')

# Initialize FastAPI app
app = FastAPI(title=SERVICE_NAME, version=SERVICE_VERSION)

# Global OCR predictor
DOCTR_PREDICTOR = None


def initialize_doctr():
    """Initialize or return the existing docTR OCR predictor."""
    global DOCTR_PREDICTOR

    if DOCTR_PREDICTOR is None:
        # Determine device
        device = 'cuda' if torch.cuda.is_available() else 'cpu'
        
        if device == 'cuda':
            device_name = torch.cuda.get_device_name(0)
            print(f"GPU is available: {device_name}. Configuring docTR to use GPU.")
        else:
            print("GPU is not available. docTR will use CPU.")

        print(f"Initializing docTR OCR predictor with master recognition model...")
        print("WARNING: docTR does NOT support Japanese text. Use EasyOCR or MangaOCR for Japanese.")
        start_time = time.time()

        try:
            from doctr.models import ocr_predictor
            
            # Use master model for better English recognition
            DOCTR_PREDICTOR = ocr_predictor(
                det_arch='db_resnet50',
                reco_arch='master',
                pretrained=True,
                assume_straight_pages=True
            )
            
            # Move models to GPU if available
            if device == 'cuda':
                det_model = DOCTR_PREDICTOR.det_predictor.model
                reco_model = DOCTR_PREDICTOR.reco_predictor.model
                
                det_model.to('cuda')
                reco_model.to('cuda')
                
                print(f"✓ Models moved to GPU ({device_name})")
            
            initialization_time = time.time() - start_time
            print(f"docTR initialization completed in {initialization_time:.2f} seconds")
            
        except Exception as e:
            print(f"ERROR: Failed to initialize docTR: {e}")
            raise
    else:
        print(f"Using existing docTR predictor")

    return DOCTR_PREDICTOR


def detect_text_orientation(width: int, height: int, aspect_ratio_threshold: float = 1.5) -> str:
    """
    Detect text orientation based on bounding box dimensions.
    
    Args:
        width: Width of the text bounding box
        height: Height of the text bounding box
        aspect_ratio_threshold: Threshold for determining orientation (default: 1.5)
    
    Returns:
        "horizontal" - docTR is primarily for horizontal text (does not support Japanese/vertical text well).
        Vertical text detection via aspect ratio is unreliable.
    """
    return "horizontal"


def process_doctr_results(image: Image.Image, result) -> List[Dict]:
    """Process docTR results into standardized format."""
    text_objects = []
    
    # docTR returns results as a nested structure
    # result.pages[0].blocks[].lines[].words[]
    for page in result.pages:
        for block in page.blocks:
            for line in block.lines:
                for word in line.words:
                    text = word.value
                    confidence = word.confidence
                    
                    # Get geometry (normalized coordinates 0-1)
                    geometry = word.geometry
                    
                    # Convert to pixel coordinates
                    img_width, img_height = image.size
                    
                    # geometry is ((x_min, y_min), (x_max, y_max))
                    x_min = int(geometry[0][0] * img_width)
                    y_min = int(geometry[0][1] * img_height)
                    x_max = int(geometry[1][0] * img_width)
                    y_max = int(geometry[1][1] * img_height)
                    
                    x = x_min
                    y = y_min
                    width = x_max - x_min
                    height = y_max - y_min
                    
                    # Create polygon
                    polygon = [
                        [x_min, y_min],
                        [x_max, y_min],
                        [x_max, y_max],
                        [x_min, y_max]
                    ]
                    
                    # Extract color information
                    color_info = None
                    try:
                        color_data = extract_foreground_background_colors(image, polygon)
                        if color_data:
                            color_info = color_data
                    except Exception as e:
                        print(f"Color extraction failed: {e}")
                    
                    # Detect text orientation
                    text_orientation = detect_text_orientation(width, height)
                    
                    # Return as single word
                    text_obj = {
                        "text": text,
                        "x": x,
                        "y": y,
                        "width": width,
                        "height": height,
                        "vertices": polygon,
                        "confidence": confidence,
                        "text_orientation": text_orientation
                    }
                    if color_info:
                        attach_color_info(text_obj, color_info)
                    text_objects.append(text_obj)
    
    return text_objects


@app.post("/process")
async def process_image(request: Request):
    """
    Process an image for OCR using docTR.
    
    NOTE: docTR does NOT support Japanese text. Use EasyOCR or MangaOCR for Japanese.
    
    Query parameters:
    - lang: Language code (default: 'english', docTR primarily supports Latin-based languages)
    """
    try:
        start_time = time.time()
        
        # Get query parameters
        lang = request.query_params.get('lang', 'english')
        
        # Read binary image data
        image_bytes = await request.body()
        if not image_bytes:
            raise HTTPException(status_code=400, detail="No image data provided")
        
        # Load image
        image = Image.open(BytesIO(image_bytes)).convert('RGB')
        
        # Initialize docTR predictor
        predictor = initialize_doctr()
        
        # Convert to numpy array for docTR
        image_array = np.array(image)
        
        # Perform OCR
        result = predictor([image_array])
        
        # Process results
        text_objects = process_doctr_results(image, result)
        
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


@app.get("/info")
async def get_info():
    """Get service information."""
    info = {
        "service_name": get_config_value(SERVICE_CONFIG, 'service_name', 'docTR'),
        "description": get_config_value(SERVICE_CONFIG, 'description', ''),
        "version": get_config_value(SERVICE_CONFIG, 'version', '0.10.0'),
        "conda_env_name": get_config_value(SERVICE_CONFIG, 'conda_env_name', 'ugt_doctr'),
        "port": int(get_config_value(SERVICE_CONFIG, 'port', '5002')),
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


@app.on_event("startup")
async def startup_event():
    """Pre-load OCR model at startup to avoid delay on first request."""
    print("=" * 60)
    print("PRE-LOADING docTR OCR MODEL AT STARTUP")
    print("=" * 60)
    
    # Pre-load docTR predictor
    try:
        initialize_doctr()
        print("✓ docTR OCR model pre-loaded successfully")
    except Exception as e:
        print(f"✗ Failed to pre-load docTR OCR model: {e}")
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

