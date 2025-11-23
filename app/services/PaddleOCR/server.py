"""FastAPI server for PaddleOCR service."""

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
import paddle
from paddleocr import PaddleOCR

# Add shared folder to path
shared_dir = Path(__file__).parent.parent / "shared"
sys.path.insert(0, str(shared_dir))

from config_parser import parse_service_config, get_config_value

# Load service configuration
config_path = Path(__file__).parent / "service_config.txt"
SERVICE_CONFIG = parse_service_config(str(config_path))

# Get service settings
SERVICE_NAME = get_config_value(SERVICE_CONFIG, 'service_name', 'PaddleOCR')
SERVICE_PORT = int(get_config_value(SERVICE_CONFIG, 'port', '5003'))
SERVICE_VERSION = get_config_value(SERVICE_CONFIG, 'version', '2.9.1')

# Initialize FastAPI app
app = FastAPI(title=SERVICE_NAME, version=SERVICE_VERSION)

# Global OCR engine
OCR_ENGINE = None
CURRENT_LANG = None
CURRENT_ANGLE_CLS = False

def initialize_ocr_engine(lang: str = 'japan', use_angle_cls: bool = False):
    """Initialize or reinitialize the OCR engine with the specified language."""
    global OCR_ENGINE, CURRENT_LANG, CURRENT_ANGLE_CLS

    # Map language codes to PaddleOCR language codes
    # https://github.com/PaddlePaddle/PaddleOCR/blob/release/2.6/doc/doc_en/multi_languages_en.md
    lang_map = {
        'ja': 'japan',
        'japan': 'japan',
        'ko': 'korean',
        'korean': 'korean',
        'ch_sim': 'ch',
        'chinese': 'ch',
        'en': 'en',
        'english': 'en',
        'es': 'es', # spanish
        'fr': 'fr', # french
        'de': 'german',
        'it': 'it',
        'ru': 'ru',
        'pt': 'pt',
        'vi': 'vi',
        'ar': 'ar',
        'hi': 'hi'
    }

    # Use mapped language or default to input if not in map
    paddle_lang = lang_map.get(lang, lang)
    
    # Check if we need to reinitialize
    # Reinitialize if:
    # 1. Engine is None
    # 2. Language changed
    # 3. Angle classification setting changed
    if (OCR_ENGINE is None or 
        CURRENT_LANG != paddle_lang or 
        CURRENT_ANGLE_CLS != use_angle_cls):
        
        # Check for GPU availability
        use_gpu = paddle.device.is_compiled_with_cuda()
        device_name = paddle.device.get_device()
        print(f"Paddle device: {device_name} (GPU available: {use_gpu})")

        print(f"Initializing PaddleOCR engine with language: {paddle_lang}, angle_cls: {use_angle_cls}...")
        start_time = time.time()

        # Initialize PaddleOCR
        # use_angle_cls: True to enable orientation classification
        # lang: language code
        # use_gpu: True/False
        # show_log: False to reduce noise
        OCR_ENGINE = PaddleOCR(
            use_angle_cls=use_angle_cls, 
            lang=paddle_lang, 
            use_gpu=use_gpu,
            show_log=False,
            enable_mkldnn=True # Enable MKLDNN for CPU acceleration if GPU fails or is not used
        )
        
        CURRENT_LANG = paddle_lang
        CURRENT_ANGLE_CLS = use_angle_cls
        initialization_time = time.time() - start_time
        print(f"PaddleOCR initialization completed in {initialization_time:.2f} seconds")
    else:
        print(f"Using existing PaddleOCR engine with language: {paddle_lang}, angle_cls: {use_angle_cls}")

    return OCR_ENGINE

def process_ocr_results(results: list) -> List[Dict]:
    """Process PaddleOCR results into standardized format."""
    text_objects = []
    
    # PaddleOCR result structure: 
    # [ [ [ [x1,y1], [x2,y2], [x3,y3], [x4,y4] ], ("text", confidence) ], ... ]
    # It returns a list of lists (one for each image), since we send one image, we take results[0]
    
    if not results or results[0] is None:
        return []
        
    for line in results[0]:
        bbox = line[0]
        text_info = line[1]
        text = text_info[0]
        confidence = text_info[1]
        
        # Calculate bounding box (x, y, width, height)
        xs = [point[0] for point in bbox]
        ys = [point[1] for point in bbox]
        
        x = int(min(xs))
        y = int(min(ys))
        width = int(max(xs) - min(xs))
        height = int(max(ys) - min(ys))
        
        # Convert vertices to integer lists
        vertices = [[int(p[0]), int(p[1])] for p in bbox]
        
        # Determine orientation based on aspect ratio
        # This is a simple heuristic; PaddleOCR's angle classifier handles image rotation,
        # but this field is for the app's UI to know if it's vertical text.
        # PaddleOCR output doesn't explicitly say "vertical" vs "horizontal" text direction in the same way as EasyOCR sometimes hints,
        # but we can infer from the bounding box aspect ratio.
        # However, for standard horizontal text, width > height.
        text_orientation = "horizontal"
        if height > width * 1.5:
             text_orientation = "vertical"

        text_obj = {
            "text": text,
            "x": x,
            "y": y,
            "width": width,
            "height": height,
            "vertices": vertices,
            "confidence": float(confidence),
            "text_orientation": text_orientation
        }
        
        text_objects.append(text_obj)
    
    return text_objects

@app.post("/process")
async def process_image(request: Request):
    """
    Process an image for OCR.
    
    Expects binary image data in the request body.
    Query parameters:
    - lang: Language code (default: 'japan')
    - use_angle_cls: Enable angle classification (default: 'false')
    """
    try:
        start_time = time.time()
        
        # Get query parameters
        lang = request.query_params.get('lang', 'japan')
        use_angle_cls_param = request.query_params.get('use_angle_cls', 'false').lower()
        use_angle_cls = use_angle_cls_param == 'true'
        
        # Read binary image data
        image_bytes = await request.body()
        if not image_bytes:
            raise HTTPException(status_code=400, detail="No image data provided")
        
        # Initialize OCR engine
        engine = initialize_ocr_engine(lang, use_angle_cls)
        
        # Run OCR
        # PaddleOCR expects path or numpy array
        # We'll convert bytes to numpy array
        image = Image.open(BytesIO(image_bytes)).convert('RGB')
        img_array = np.array(image)
        
        # Perform OCR
        results = engine.ocr(img_array, cls=use_angle_cls)
        
        # Process results
        text_objects = process_ocr_results(results)
        
        # Calculate processing time
        processing_time = time.time() - start_time
        
        # Build response
        response = {
            "status": "success",
            "texts": text_objects,
            "processing_time": processing_time,
            "language": lang,
            "backend": "paddle"
        }
        
        return JSONResponse(content=response)
        
    except Exception as e:
        print(f"Error processing image: {e}")
        import traceback
        traceback.print_exc()
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
        "service_name": SERVICE_NAME,
        "description": get_config_value(SERVICE_CONFIG, 'description', ''),
        "version": SERVICE_VERSION,
        "port": SERVICE_PORT,
        "local_only": get_config_value(SERVICE_CONFIG, 'local_only', 'true') == 'true',
        "framework": "PaddlePaddle",
        "paddle_version": paddle.__version__
    }
    return JSONResponse(content=info)

@app.get("/health")
async def health_check():
    """Health check endpoint."""
    return JSONResponse(content={
        "status": "healthy",
        "service": SERVICE_NAME,
        "version": SERVICE_VERSION
    })

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

if __name__ == "__main__":
    host = "127.0.0.1" if get_config_value(SERVICE_CONFIG, 'local_only', 'true') == 'true' else "0.0.0.0"
    
    print(f"Starting {SERVICE_NAME} service on {host}:{SERVICE_PORT}")
    
    uvicorn.run(
        app,
        host=host,
        port=SERVICE_PORT,
        log_level="info"
    )

