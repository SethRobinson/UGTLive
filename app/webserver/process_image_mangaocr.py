import os
import json
import time
import numpy as np
from PIL import Image, ImageEnhance, ImageFilter
import torch
import easyocr

# Global variables to manage OCR engines
MANGA_OCR_ENGINE = None
EASYOCR_DETECTOR = None

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

def initialize_easyocr_detector():
    """
    Initialize or return the existing EasyOCR detector for text detection.
    We use EasyOCR's detection capability to find text regions, then use Manga OCR for recognition.
    
    Returns:
        easyocr.Reader: Initialized detector
    """
    global EASYOCR_DETECTOR

    if EASYOCR_DETECTOR is None:
        print(f"Initializing EasyOCR detector for text region detection...")
        start_time = time.time()
        
        # Initialize EasyOCR with Japanese language for detection
        # We'll use it for detection only, not recognition
        EASYOCR_DETECTOR = easyocr.Reader(['ja', 'en'], gpu=torch.cuda.is_available())
        
        initialization_time = time.time() - start_time
        print(f"EasyOCR detector initialization completed in {initialization_time:.2f} seconds")
    else:
        print(f"Using existing EasyOCR detector")

    return EASYOCR_DETECTOR

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
                  preprocess_images=False, upscale_if_needed=False, char_level=True):
    """
    Process an image using a hybrid approach:
    1. Use EasyOCR to detect text regions (bounding boxes)
    2. Use Manga OCR to recognize text in each detected region
    
    This gives us the best of both worlds: reliable text detection from EasyOCR
    and better Japanese manga text recognition from Manga OCR.
    
    Args:
        image_path (str): Path to the image to process.
        lang (str): Language to use for OCR (Manga OCR only supports Japanese).
        font_path (str): Path to font file (not used by Manga OCR).
        preprocess_images (bool): Flag to determine whether to preprocess the image.
        upscale_if_needed (bool): Flag to determine whether to upscale the image if it's low resolution.
        char_level (bool): If True, split text into characters with estimated positions.
    
    Returns:
        dict: JSON-serializable dictionary with OCR results.
    """
    # Check if image exists
    if not os.path.exists(image_path):
        return {"error": f"Image file not found: {image_path}"}

    try:
        # Start timing the OCR process
        start_time = time.time()
        
        # Open the image using PIL
        image = Image.open(image_path)
        
        # Store original size for coordinate scaling later
        original_width, original_height = image.size
        
        # Preprocess image if the flag is set
        if preprocess_images:
            print("Preprocessing image...")
            image = preprocess_image(image)
        
        # Upscale image if the flag is set, and get the scale factor
        scale = 1.0
        if upscale_if_needed:
            print("Checking if upscaling is needed...")
            image, scale = upscale_image(image)
        
        # Step 1: Use EasyOCR to detect text regions
        print("Step 1: Detecting text regions with EasyOCR...")
        detector = initialize_easyocr_detector()
        img_array = np.array(image)
        
        # Use EasyOCR to detect text regions
        # We'll use the detection results but replace the text with Manga OCR
        detection_results = detector.readtext(img_array, detail=1, paragraph=False)
        print(f"Found {len(detection_results)} text regions")
        
        # Step 2: Initialize Manga OCR for recognition
        manga_ocr = initialize_manga_ocr()
        
        # Step 3: Process each detected region with Manga OCR
        ocr_results = []
        
        for idx, detection in enumerate(detection_results):
            # EasyOCR format: [[[x1,y1],[x2,y2],[x3,y3],[x4,y4]], text, confidence]
            box = detection[0]
            easyocr_text = detection[1]  # We'll replace this with manga-ocr result
            detection_confidence = float(detection[2])
            
            # Convert box format if needed
            if isinstance(box[0], (int, float)):
                box = [
                    [box[0], box[1]],
                    [box[2], box[3]],
                    [box[4], box[5]],
                    [box[6], box[7]]
                ]
            
            # Convert coordinates back to the original image scale if upscaled
            if scale != 1.0:
                box = [[coord / scale for coord in point] for point in box]
            
            # Convert all NumPy types to native Python types for JSON serialization
            box_native = [[float(coord) for coord in point] for point in box]
            
            # Extract the bounding box coordinates to crop the region
            # Get min/max coordinates from the four corners
            xs = [point[0] for point in box]
            ys = [point[1] for point in box]
            x_min, x_max = int(min(xs)), int(max(xs))
            y_min, y_max = int(min(ys)), int(max(ys))
            
            # Add some padding to improve recognition (5 pixels on each side)
            padding = 5
            x_min = max(0, x_min - padding)
            y_min = max(0, y_min - padding)
            x_max = min(image.width, x_max + padding)
            y_max = min(image.height, y_max + padding)
            
            # Skip regions that are too small (likely noise)
            if (x_max - x_min) < 10 or (y_max - y_min) < 10:
                print(f"Region {idx+1}: Skipping (too small)")
                continue
            
            # Crop the region from the image
            try:
                cropped_region = image.crop((x_min, y_min, x_max, y_max))
                
                # Use Manga OCR to recognize text in this region
                manga_text = manga_ocr(cropped_region)
                
                # Use manga-ocr result if it's not empty, otherwise fall back to EasyOCR
                text = manga_text.strip() if manga_text.strip() else easyocr_text
                
                # Skip if no text detected
                if not text:
                    print(f"Region {idx+1}: No text detected")
                    continue
                
                print(f"Region {idx+1}: EasyOCR='{easyocr_text}', MangaOCR='{manga_text}', Using='{text}'")
                
            except Exception as e:
                print(f"Error processing region {idx+1} with Manga OCR: {e}")
                # Fall back to EasyOCR text if Manga OCR fails
                text = easyocr_text
            
            # Split into characters if requested
            if char_level and len(text) > 1:
                char_results = split_into_characters(text, box_native, detection_confidence)
                ocr_results.extend(char_results)
            else:
                ocr_results.append({
                    "rect": box_native,
                    "text": text,
                    "confidence": detection_confidence,
                    "is_character": False
                })
        
        # Calculate processing time
        processing_time = time.time() - start_time
        
        print(f"Manga OCR completed: {len(ocr_results)} results in {processing_time:.2f}s")
        
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
            "is_character": True
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
        
        # Create the character bounding box
        char_box = [
            [x1_top, y_top_left],       # top-left
            [x2_top, y_top_right],      # top-right
            [x2_bottom, y_bottom_right], # bottom-right
            [x1_bottom, y_bottom_left]   # bottom-left
        ]
        
        # Add to results
        char_results.append({
            "rect": char_box,
            "text": char,
            "confidence": confidence,
            "is_character": True
        })
    
    return char_results