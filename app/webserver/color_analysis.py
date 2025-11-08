"""Utilities for extracting foreground/background colors from OCR regions."""

from __future__ import annotations

import math
import threading
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

import numpy as np
from PIL import Image, ImageDraw

try:
    from marearts_xcolor import ColorExtractor  # type: ignore
    try:
        from marearts_xcolor.gpu_utils import get_gpu_info  # type: ignore
    except ImportError:
        get_gpu_info = None  # type: ignore
except ImportError:  # pragma: no cover - handled gracefully at runtime
    ColorExtractor = None  # type: ignore
    get_gpu_info = None  # type: ignore


_COLOR_EXTRACTOR_LOCK = threading.Lock()
_COLOR_EXTRACTOR: Optional["ColorExtractor"] = None


def _get_color_extractor() -> Optional["ColorExtractor"]:
    """Lazily instantiate the global ColorExtractor."""

    global _COLOR_EXTRACTOR

    if ColorExtractor is None:
        return None

    with _COLOR_EXTRACTOR_LOCK:
        if _COLOR_EXTRACTOR is None:
            kwargs = {
                "n_colors": 4,
                "lab_space": True,
                "preprocessing": False,
            }
            
            # Check GPU info before initialization
            gpu_device = None
            cupy_available = False
            
            # First try marearts_xcolor's gpu_utils
            if get_gpu_info is not None:
                try:
                    gpu_info = get_gpu_info()
                    if gpu_info and isinstance(gpu_info, dict):
                        gpu_device = gpu_info.get("device_name") or gpu_info.get("name")
                except Exception:
                    pass
            
            # Try PyTorch to get GPU name (commonly available in this codebase)
            if gpu_device is None:
                try:
                    import torch
                    if torch.cuda.is_available():
                        gpu_device = torch.cuda.get_device_name(0)
                except ImportError:
                    pass
                except Exception:
                    pass
            
            # Check if CuPy is available (required for GPU acceleration)
            try:
                import cupy as cp  # type: ignore
                cupy_available = True
                if gpu_device is None:
                    try:
                        # Try to get GPU device name from CuPy
                        device = cp.cuda.Device(0)
                        device.use()
                        # CuPy doesn't directly expose device name, but we can check if it's working
                        gpu_device = "CUDA Device (via CuPy)"
                    except Exception:
                        gpu_device = "CUDA Device"
            except ImportError:
                cupy_available = False
            
            try:
                _COLOR_EXTRACTOR = ColorExtractor(use_gpu="auto", **kwargs)  # type: ignore[arg-type]
                
                # Try to detect if GPU is actually being used
                is_using_gpu = False
                gpu_status_msg = "CPU"
                
                # Check various ways the ColorExtractor might indicate GPU usage
                if hasattr(_COLOR_EXTRACTOR, 'device'):
                    device = getattr(_COLOR_EXTRACTOR, 'device', None)
                    if device and ('cuda' in str(device).lower() or 'gpu' in str(device).lower()):
                        is_using_gpu = True
                        gpu_status_msg = f"GPU ({gpu_device or 'CUDA'})"
                elif hasattr(_COLOR_EXTRACTOR, '_device'):
                    device = getattr(_COLOR_EXTRACTOR, '_device', None)
                    if device and ('cuda' in str(device).lower() or 'gpu' in str(device).lower()):
                        is_using_gpu = True
                        gpu_status_msg = f"GPU ({gpu_device or 'CUDA'})"
                elif hasattr(_COLOR_EXTRACTOR, 'use_gpu'):
                    if getattr(_COLOR_EXTRACTOR, 'use_gpu', False):
                        is_using_gpu = True
                        gpu_status_msg = f"GPU ({gpu_device or 'CUDA'})"
                elif cupy_available and gpu_device:
                    # CuPy is available, so GPU acceleration is likely enabled
                    gpu_status_msg = f"GPU ({gpu_device})"
                    is_using_gpu = True
                elif gpu_device:
                    # GPU detected but CuPy not available - likely CPU fallback
                    gpu_status_msg = f"CPU (GPU {gpu_device} available but CuPy not installed)"
                else:
                    gpu_status_msg = "CPU"
                
                print(f"MareArts XColor initialized - Using {gpu_status_msg} for color extraction")
                
            except TypeError:
                # Older releases (<0.0.8) do not accept use_gpu; fall back quietly.
                _COLOR_EXTRACTOR = ColorExtractor(**kwargs)
                if gpu_device:
                    print(f"MareArts XColor initialized (old version) - GPU status unknown, GPU available: {gpu_device}")
                else:
                    print("MareArts XColor initialized (old version) - Using CPU (GPU status unknown)")
    return _COLOR_EXTRACTOR


def _format_color_entry(color: Dict[str, object]) -> Dict[str, object]:
    """Normalize the color entry to include hex, rgb, and percentage."""
    rgb = color.get("rgb") or color.get("color") or []
    if isinstance(rgb, Sequence):
        rgb_values = [int(max(0, min(255, int(c)))) for c in rgb]  # clamp to 0-255
        # marearts_xcolor may return colors in BGR format, swap R and B to convert to RGB
        if len(rgb_values) == 3:
            rgb_values = [rgb_values[2], rgb_values[1], rgb_values[0]]  # BGR -> RGB
    else:
        rgb_values = []

    hex_value = color.get("hex")
    if not hex_value and len(rgb_values) == 3:
        hex_value = "#{:02x}{:02x}{:02x}".format(*rgb_values)

    percentage_raw = color.get("percentage", 0.0)
    try:
        percentage_value = float(percentage_raw)
    except (TypeError, ValueError):
        percentage_value = 0.0

    return {
        "rgb": rgb_values,
        "hex": hex_value,
        "percentage": percentage_value,
    }


def extract_foreground_background_colors(
    image: Image.Image,
    polygon: Iterable[Sequence[float]],
    min_contrast: float = 30.0,
    min_pixels: int = 15,
) -> Optional[Dict[str, object]]:
    """Return representative foreground/background colors for a polygonal region.

    Args:
        image: Source PIL image (should remain in RGB for accurate colors).
        polygon: Iterable of (x, y) pairs describing the OCR polygon.
        min_contrast: Minimum Euclidean RGB distance to accept a foreground
            candidate different from the background.
        min_pixels: Minimum number of masked pixels required to attempt
            color extraction.

    Returns:
        Dict containing foreground/background color metadata or ``None`` if
        extraction is unavailable or fails.
    """

    extractor = _get_color_extractor()
    if extractor is None:
        return None

    polygon_list: List[Tuple[float, float]] = [
        (float(point[0]), float(point[1])) for point in polygon
    ]
    if not polygon_list:
        return None

    xs = [p[0] for p in polygon_list]
    ys = [p[1] for p in polygon_list]

    min_x = max(int(math.floor(min(xs))), 0)
    min_y = max(int(math.floor(min(ys))), 0)
    max_x = min(int(math.ceil(max(xs))), image.width)
    max_y = min(int(math.ceil(max(ys))), image.height)

    if max_x - min_x <= 1 or max_y - min_y <= 1:
        return None

    # Ensure we are working in RGB space.
    rgb_image = image.convert("RGB") if image.mode != "RGB" else image
    region = rgb_image.crop((min_x, min_y, max_x, max_y))

    mask_image = Image.new("L", region.size, 0)
    region_width, region_height = region.size
    shifted_polygon = []
    for point in polygon_list:
        shifted_x = max(0.0, min(float(region_width - 1), point[0] - min_x))
        shifted_y = max(0.0, min(float(region_height - 1), point[1] - min_y))
        shifted_polygon.append((shifted_x, shifted_y))
    ImageDraw.Draw(mask_image, "L").polygon(shifted_polygon, fill=255)

    mask_array = np.array(mask_image, dtype=np.uint8)
    if int(mask_array.sum()) < min_pixels * 255:
        return None

    region_array = np.array(region)

    try:
        colors: List[Dict[str, object]] = extractor.extract_colors(
            region_array, mask=mask_array
        )
    except Exception as exc:  # pragma: no cover - defensive
        print(f"[color_analysis] Color extraction failed: {exc}")
        return None

    if not colors:
        return None

    colors = [c for c in colors if c.get("percentage", 0)]
    if not colors:
        return None

    colors.sort(key=lambda c: float(c.get("percentage", 0.0)), reverse=True)
    background = colors[0]
    
    # Simple approach: most common = background, second most common = foreground
    if len(colors) > 1:
        foreground = colors[1]
    else:
        foreground = colors[0]

    foreground_formatted = _format_color_entry(foreground)
    background_formatted = _format_color_entry(background)
    
    return {
        "background_color": background_formatted,
        "foreground_color": foreground_formatted,
        "color_source": "marearts_xcolor",
    }


def attach_color_info(target: Dict[str, object], color_info: Optional[Dict[str, object]]) -> None:
    """Attach normalized color metadata to an OCR result dict."""

    if not color_info:
        return

    if "background_color" in color_info:
        target["background_color"] = dict(color_info["background_color"])  # type: ignore[arg-type]
    if "foreground_color" in color_info:
        target["foreground_color"] = dict(color_info["foreground_color"])  # type: ignore[arg-type]
    if "color_source" in color_info:
        target["color_source"] = color_info["color_source"]

