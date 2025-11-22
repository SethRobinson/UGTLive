"""Utilities for extracting foreground/background colors from OCR regions."""

from __future__ import annotations

import math
import threading
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

import numpy as np
from PIL import Image, ImageDraw
import cv2
import torch

_COLOR_EXTRACTOR_LOCK = threading.Lock()
_COLOR_EXTRACTOR: Optional["_HybridColorExtractor"] = None


def _get_device() -> torch.device:
    """Get the best available device for PyTorch."""
    if torch.cuda.is_available():
        return torch.device("cuda")
    return torch.device("cpu")


class _HybridColorExtractor:
    """
    Hybrid color extractor using Adaptive Thresholding (OpenCV) and PyTorch K-Means.
    
    Strategy:
    1. Try Adaptive Thresholding (Otsu) to find text mask.
    2. If successful, calculate median colors for text/bg.
    3. If unsuccessful or low confidence, fall back to K-Means clustering (GPU accelerated).
    """

    def __init__(self, device: Optional[str] = None) -> None:
        self.device = torch.device(device) if device else _get_device()
        print(f"[ColorExtractor] Initialized on {self.device}")

    def _get_dominant_colors_kmeans(
        self, pixels: np.ndarray, k: int = 2, num_iterations: int = 10
    ) -> Tuple[np.ndarray, np.ndarray]:
        """
        Perform K-Means clustering using PyTorch.
        Returns (centroids, labels).
        """
        if pixels.size == 0:
            return np.zeros((0, 3)), np.zeros(0)

        # Convert to torch tensor
        data = torch.from_numpy(pixels).float().to(self.device)
        
        # Initialize centroids randomly
        n_samples = data.shape[0]
        
        # Safety check for sample size
        if n_samples < k:
            k = n_samples
            
        indices = torch.randperm(n_samples, device=self.device)[:k]
        centroids = data[indices]

        labels = torch.zeros(n_samples, dtype=torch.long, device=self.device)

        for _ in range(num_iterations):
            # Compute distances: (N, 1, D) - (1, K, D) -> (N, K, D) -> (N, K)
            distances = torch.cdist(data, centroids)
            
            # Assign labels
            labels = torch.argmin(distances, dim=1)

            # Update centroids
            new_centroids = torch.zeros_like(centroids)
            for i in range(k):
                mask = labels == i
                if mask.any():
                    new_centroids[i] = data[mask].mean(dim=0)
                else:
                    # Handle empty cluster by re-initializing
                    new_centroids[i] = data[torch.randint(0, n_samples, (1,), device=self.device)]
            
            # Check convergence
            if torch.allclose(centroids, new_centroids, atol=1e-4):
                break
                
            centroids = new_centroids

        return centroids.cpu().numpy(), labels.cpu().numpy()

    def _extract_adaptive_threshold(
        self, image_array: np.ndarray, mask_array: Optional[np.ndarray]
    ) -> Optional[Tuple[Dict[str, object], Dict[str, object]]]:
        """
        Use OpenCV Adaptive Thresholding to find text/background.
        """
        try:
            # Convert to grayscale for thresholding
            gray = cv2.cvtColor(image_array, cv2.COLOR_RGB2GRAY)
            
            # Apply mask if provided
            if mask_array is not None:
                # Ensure mask matches image size
                if mask_array.shape != gray.shape:
                    mask_array = cv2.resize(mask_array, (gray.shape[1], gray.shape[0]))
                
                # Fill outside mask with median gray to not mess up thresholding too much
                # But actually, adaptive threshold works locally, so we just need to ignore outside later
                pass

            # Apply Otsu's binarization to find optimal global threshold
            # This often works well for high contrast text
            blur = cv2.GaussianBlur(gray, (5, 5), 0)
            _, otsu_mask = cv2.threshold(blur, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
            
            # Alternatively, adaptive thresholding for varying lighting
            adaptive_mask = cv2.adaptiveThreshold(
                gray, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, cv2.THRESH_BINARY, 11, 2
            )
            
            # Combine logic: We need to determine which phase is text (0 or 255).
            # Heuristic: Text is usually thinner and takes up less area than background.
            
            # Let's use the mask to only consider relevant pixels
            valid_mask = mask_array > 0 if mask_array is not None else np.ones_like(gray, dtype=bool)
            
            # Filter masks by valid region
            valid_otsu = otsu_mask[valid_mask]
            valid_adaptive = adaptive_mask[valid_mask]
            
            if valid_otsu.size == 0:
                return None

            # Analyze Otsu results
            otsu_counts = np.bincount(valid_otsu.flatten(), minlength=256)
            black_count = otsu_counts[0]
            white_count = otsu_counts[255]
            
            # Text is usually the minority class
            if black_count < white_count:
                text_mask_val = 0  # Black is text
                bg_mask_val = 255
            else:
                text_mask_val = 255 # White is text
                bg_mask_val = 0
                
            # Create boolean masks for original image
            # We use the Otsu result for color extraction as it's often cleaner for "solid" text
            # Adaptive is better for structure but can be noisy
            
            is_text = (otsu_mask == text_mask_val) & valid_mask
            is_bg = (otsu_mask == bg_mask_val) & valid_mask
            
            if not is_text.any() or not is_bg.any():
                return None

            # Extract colors
            pixels = image_array.reshape(-1, 3)
            flat_is_text = is_text.flatten()
            flat_is_bg = is_bg.flatten()
            
            text_pixels = pixels[flat_is_text]
            bg_pixels = pixels[flat_is_bg]
            
            # Use median for robust color estimation (avoids outliers/noise at edges)
            text_color = np.median(text_pixels, axis=0)
            bg_color = np.median(bg_pixels, axis=0)
            
            # Calculate confidence/percentage
            total_pixels = valid_mask.sum()
            text_pct = (flat_is_text.sum() / total_pixels) * 100
            
            return (
                {"rgb": text_color.tolist(), "percentage": text_pct},
                {"rgb": bg_color.tolist(), "percentage": 100 - text_pct}
            )
            
        except Exception as e:
            print(f"[ColorExtractor] Adaptive threshold failed: {e}")
            return None

    def extract_colors(
        self,
        region_array: np.ndarray,
        mask: Optional[np.ndarray] = None,
    ) -> List[Dict[str, object]]:
        """
        Extract colors from the region.
        Returns list of color dictionaries.
        """
        # 1. Try Adaptive Thresholding First (Fast + Accurate for text)
        adaptive_result = self._extract_adaptive_threshold(region_array, mask)
        
        if adaptive_result:
            fg, bg = adaptive_result
            # Ensure reasonable contrast
            fg_rgb = np.array(fg["rgb"])
            bg_rgb = np.array(bg["rgb"])
            dist = np.linalg.norm(fg_rgb - bg_rgb)
            
            if dist > 30.0: # Good contrast found
                return [bg, fg] # Return background first (convention)

        # 2. Fallback to K-Means (PyTorch GPU)
        try:
            # Prepare pixels
            if mask is None:
                pixels = region_array.reshape(-1, 3).astype(np.float32)
            else:
                valid_mask = mask.astype(bool).flatten()
                pixels = region_array.reshape(-1, 3)[valid_mask].astype(np.float32)
            
            if pixels.shape[0] < 10:
                return []

            centroids, labels = self._get_dominant_colors_kmeans(pixels, k=2)
            
            # Calculate stats
            counts = np.bincount(labels, minlength=len(centroids))
            total = counts.sum()
            
            results = []
            for i, centroid in enumerate(centroids):
                pct = (counts[i] / total) * 100
                results.append({
                    "rgb": centroid.tolist(),
                    "percentage": pct
                })
                
            # Sort by percentage (descending)
            results.sort(key=lambda x: x["percentage"], reverse=True)
            return results
            
        except Exception as e:
            print(f"[ColorExtractor] K-Means fallback failed: {e}")
            return []

    def backend_status(self) -> str:
        return f"Hybrid (OpenCV + PyTorch {self.device})"


def _choose_background_foreground(
    colors: List[Dict[str, object]]
) -> Tuple[Optional[Dict[str, object]], Optional[Dict[str, object]]]:
    """
    Heuristic to choose foreground/background from color list.
    """
    if not colors:
        return None, None

    # If we have exactly 2 colors (from Adaptive Threshold), trust the order
    # Adaptive returns [bg, fg] usually, but let's verify brightness/saturation
    
    # Basic heuristics
    sorted_colors = sorted(colors, key=lambda x: x["percentage"], reverse=True)
    
    # Assume majority color is background
    background = sorted_colors[0]
    
    # Find color with max contrast to background
    bg_rgb = np.array(background["rgb"])
    max_dist = -1.0
    foreground = background # Default to same if no contrast found
    
    for color in sorted_colors[1:]:
        curr_rgb = np.array(color["rgb"])
        dist = np.linalg.norm(curr_rgb - bg_rgb)
        if dist > max_dist:
            max_dist = dist
            foreground = color
            
    return background, foreground


def _get_color_extractor() -> Optional["_HybridColorExtractor"]:
    """Lazily instantiate the global ColorExtractor."""
    global _COLOR_EXTRACTOR

    with _COLOR_EXTRACTOR_LOCK:
        if _COLOR_EXTRACTOR is None:
            try:
                _COLOR_EXTRACTOR = _HybridColorExtractor()
            except Exception as e:
                print(f"[ColorExtractor] Initialization failed: {e}")
                return None
                
    return _COLOR_EXTRACTOR


def _format_color_entry(color: Dict[str, object]) -> Dict[str, object]:
    """Normalize the color entry to include hex, rgb, and percentage."""
    rgb_values: List[int] = []
    raw_rgb = color.get("rgb")
    
    if isinstance(raw_rgb, (list, tuple, np.ndarray)):
        rgb_values = [int(max(0, min(255, round(c)))) for c in raw_rgb]
    else:
        rgb_values = [0, 0, 0]

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
    """Return representative foreground/background colors for a polygonal region."""

    extractor = _get_color_extractor()
    if extractor is None:
        return None

    # Prepare crop and mask
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

    rgb_image = image.convert("RGB") if image.mode != "RGB" else image
    region = rgb_image.crop((min_x, min_y, max_x, max_y))

    # Create mask
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
        colors = extractor.extract_colors(region_array, mask=mask_array)
    except Exception as exc:
        print(f"[color_analysis] Color extraction failed: {exc}")
        return None

    if not colors:
        return None

    background, foreground = _choose_background_foreground(colors)
    
    if background is None:
        return None
    if foreground is None:
        foreground = background

    return {
        "background_color": _format_color_entry(background),
        "foreground_color": _format_color_entry(foreground),
        "color_source": "ugtlive_hybrid_v2",
    }


def attach_color_info(target: Dict[str, object], color_info: Optional[Dict[str, object]]) -> None:
    """Attach normalized color metadata to an OCR result dict."""

    if not color_info:
        return

    if "background_color" in color_info:
        target["background_color"] = dict(color_info["background_color"])  # type: ignore
    if "foreground_color" in color_info:
        target["foreground_color"] = dict(color_info["foreground_color"])  # type: ignore
    if "color_source" in color_info:
        target["color_source"] = color_info["color_source"]
