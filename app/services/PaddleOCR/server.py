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
from PIL import Image, ImageDraw, ImageOps
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
SERVICE_VERSION = get_config_value(SERVICE_CONFIG, 'version', '3.2.2')

# Initialize FastAPI app
app = FastAPI(title=SERVICE_NAME, version=SERVICE_VERSION)

# Global OCR engine
OCR_ENGINE = None
CURRENT_LANG = None
CURRENT_ANGLE_CLS = False

# Debug settings
DEBUG_IMAGES = False

def initialize_ocr_engine(lang: str = 'japan', use_angle_cls: bool = False):
    """Initialize or reinitialize the OCR engine with the specified language."""
    global OCR_ENGINE, CURRENT_LANG, CURRENT_ANGLE_CLS

    # Map language codes to PaddleOCR language codes
    # https://github.com/PaddlePaddle/PaddleOCR?tab=readme-ov-file
    # PaddleOCR 3.2.2 supports 100+ languages
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

        print(f"Initializing PaddleOCR engine with language: {paddle_lang}, use_textline_orientation: {use_angle_cls}...")
        start_time = time.time()

        # Initialize PaddleOCR 3.2.2
        # use_textline_orientation: True to enable orientation classification (replaces use_angle_cls)
        # lang: language code (supports 100+ languages)
        # GPU is auto-detected, no use_gpu parameter needed
        # Logging is controlled via environment variables in 3.2.2
        # text_det_limit_side_len: Limit side length for image resizing. 
        # Increased from default 960 to 1600 to prevent resizing artifacts on larger screens (1080p+).
        # CRITICAL: Disable doc preprocessor to prevent coordinate warping
        OCR_ENGINE = PaddleOCR(
            use_textline_orientation=use_angle_cls, 
            lang=paddle_lang, 
            text_det_limit_side_len=1600,
            use_doc_orientation_classify=False,
            use_doc_unwarping=False
        )
        
        CURRENT_LANG = paddle_lang
        CURRENT_ANGLE_CLS = use_angle_cls
        initialization_time = time.time() - start_time
        print(f"PaddleOCR initialization completed in {initialization_time:.2f} seconds")
    else:
        print(f"Using existing PaddleOCR engine with language: {paddle_lang}, use_textline_orientation: {use_angle_cls}")

    return OCR_ENGINE

def process_ocr_results(results: list) -> List[Dict]:
    """Process PaddleOCR 3.2.2 results into standardized format."""
    text_objects = []
    
    # PaddleOCR 3.2.2 returns OCRResult objects with a different structure
    # Results structure:
    # [
    #   OCRResult {
    #     'rec_texts': ['text1', 'text2', ...],
    #     'rec_scores': [0.95, 0.98, ...],
    #     'rec_polys': [array([[x1,y1], [x2,y2], [x3,y3], [x4,y4]]), ...]
    #   }
    # ]
    
    if not results or len(results) == 0:
        return []
    
    ocr_result = results[0]
    
    # Check if it's an OCRResult object with the expected attributes
    if not hasattr(ocr_result, 'rec_texts') or not hasattr(ocr_result, 'rec_scores') or not hasattr(ocr_result, 'rec_polys'):
        # Try to access as dictionary
        if isinstance(ocr_result, dict):
            rec_texts = ocr_result.get('rec_texts', [])
            rec_scores = ocr_result.get('rec_scores', [])
            rec_polys = ocr_result.get('rec_polys', [])
        else:
            print(f"ERROR: Unexpected result format: {type(ocr_result)}")
            return []
    else:
        # Access as object attributes
        rec_texts = ocr_result.rec_texts
        rec_scores = ocr_result.rec_scores
        rec_polys = ocr_result.rec_polys
    
    # Process each detected text
    for i, (text, score, bbox) in enumerate(zip(rec_texts, rec_scores, rec_polys)):
        # Skip empty text
        if not text or text.strip() == '':
            continue
        
        # Calculate bounding box (x, y, width, height)
        xs = [point[0] for point in bbox]
        ys = [point[1] for point in bbox]
        
        x = int(min(xs))
        y = int(min(ys))
        width = int(max(xs) - min(xs))
        height = int(max(ys) - min(ys))
        
        # Convert vertices to integer lists
        vertices = [[int(round(p[0])), int(round(p[1]))] for p in bbox]
        
        # Text orientation is always horizontal unless the OCR system provides it
        # (like MangaOCR does with its built-in vertical detection)
        text_orientation = "horizontal"
        
        text_obj = {
            "text": text,
            "x": x,
            "y": y,
            "width": width,
            "height": height,
            "vertices": vertices,
            "confidence": float(score),
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
        image = Image.open(BytesIO(image_bytes))
        
        # Handle EXIF orientation
        try:
            image = ImageOps.exif_transpose(image)
        except Exception:
            pass
            
        image = image.convert('RGB')
        
        # Save to temporary file to avoid potential numpy array conversion issues
        # and ensure consistency with PaddleOCR's file-based processing
        temp_file_path = Path(__file__).parent / "temp_processing_image.png"
        image.save(temp_file_path)
        
        # DEBUG: Save received (processed) image
        if DEBUG_IMAGES:
            try:
                debug_received_path = Path(__file__).parent / "debug_received_image.png"
                image.save(debug_received_path)
                print(f"Saved debug received image to {debug_received_path}")
            except Exception as e:
                print(f"Error saving debug received image: {e}")

        # Perform OCR using the file path
        # Orientation classification is already set during initialization via use_textline_orientation
        results = engine.predict(str(temp_file_path))
        
        # Clean up temp file
        try:
            if temp_file_path.exists():
                os.remove(temp_file_path)
        except Exception as e:
            print(f"Warning: Could not remove temp file: {e}")

        # Process results
        text_objects = process_ocr_results(results)
        
        # DEBUG: Save processed image with rects
        if DEBUG_IMAGES:
            try:
                debug_img = image.copy()
                draw = ImageDraw.Draw(debug_img)
                for obj in text_objects:
                    # Draw using x, y, width, height - RED
                    rect = [
                        obj['x'], 
                        obj['y'], 
                        obj['x'] + obj['width'], 
                        obj['y'] + obj['height']
                    ]
                    draw.rectangle(rect, outline="red", width=2)
                    
                    # Draw the polygon vertices - BLUE
                    if 'vertices' in obj:
                        verts = [(p[0], p[1]) for p in obj['vertices']]
                        # Check if we have enough points for a polygon
                        if len(verts) >= 3:
                            draw.polygon(verts, outline="blue", width=2)
                            
                debug_processed_path = Path(__file__).parent / "debug_processed_image.png"
                debug_img.save(debug_processed_path)
                print(f"Saved debug processed image to {debug_processed_path}")
            except Exception as e:
                print(f"Error saving debug processed image: {e}")
        
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
