"""Utilities for extracting foreground/background colors from OCR regions."""

from __future__ import annotations

import math
import threading
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

import numpy as np
from PIL import Image, ImageDraw

try:  # pragma: no cover - executed only when GPU stack available
    import cupy as cp  # type: ignore
    from cupyx.scipy.cluster.vq import kmeans2  # type: ignore

    CUPY_AVAILABLE = True
except ImportError:  # pragma: no cover - handled gracefully at runtime
    cp = None  # type: ignore
    kmeans2 = None  # type: ignore
    CUPY_AVAILABLE = False

try:  # pragma: no cover - OpenCV is optional but recommended
    import cv2  # type: ignore
except ImportError:  # pragma: no cover - fall back to NumPy implementation
    cv2 = None  # type: ignore


_COLOR_EXTRACTOR_LOCK = threading.Lock()
_COLOR_EXTRACTOR: Optional["_GPUColorExtractor"] = None


def _get_gpu_device_name() -> Optional[str]:
    """Best-effort detection of the active GPU name."""

    if CUPY_AVAILABLE:
        try:
            device = cp.cuda.Device()
            props = cp.cuda.runtime.getDeviceProperties(device.id)
            name = props["name"]
            if isinstance(name, bytes):
                return name.decode("utf-8", errors="ignore")
            return str(name)
        except Exception:
            pass

    try:
        import torch

        if torch.cuda.is_available():
            return torch.cuda.get_device_name(0)
    except Exception:
        pass

    return None


def _rgb_to_lab_array(xp, rgb01):
    """Convert RGB values in range [0, 1] to CIE LAB using numpy/cupy backend."""

    white = xp.array([0.95047, 1.0, 1.08883], dtype=xp.float32)

    linear_mask = rgb01 > 0.04045
    rgb_linear = xp.where(
        linear_mask,
        ((rgb01 + 0.055) / 1.055) ** 2.4,
        rgb01 / 12.92,
    )

    xyz_matrix = xp.array(
        [
            [0.4124564, 0.3575761, 0.1804375],
            [0.2126729, 0.7151522, 0.0721750],
            [0.0193339, 0.1191920, 0.9503041],
        ],
        dtype=xp.float32,
    )

    xyz = rgb_linear @ xyz_matrix.T
    xyz = xyz / white

    epsilon = 216.0 / 24389.0
    kappa = 24389.0 / 27.0

    f_xyz = xp.where(
        xyz > epsilon,
        xp.cbrt(xyz),
        (kappa * xyz + 16.0) / 116.0,
    )

    L = 116.0 * f_xyz[:, 1] - 16.0
    a = 500.0 * (f_xyz[:, 0] - f_xyz[:, 1])
    b = 200.0 * (f_xyz[:, 1] - f_xyz[:, 2])

    return xp.stack([L, a, b], axis=1)


class _GPUColorExtractor:
    """GPU-accelerated dominant color extractor with CPU fallback."""

    def __init__(
        self,
        n_colors: int = 4,
        lab_space: bool = True,
        preprocessing: bool = False,
        use_gpu: str = "auto",
        *,
        edge_bias: float = 0.4,
        max_iter: int = 20,
        max_samples_gpu: int = 150_000,
        max_samples_cpu: int = 40_000,
        min_pixels_for_gpu: int = 1_500,
    ) -> None:
        self.n_colors = max(1, int(n_colors))
        self.lab_space = lab_space
        self.preprocessing = preprocessing
        self.use_gpu = use_gpu
        self.edge_bias = float(max(0.05, min(edge_bias, 1.0)))
        self.max_iter = max(5, int(max_iter))
        self.max_samples_gpu = max_samples_gpu
        self.max_samples_cpu = max_samples_cpu
        self.min_pixels_for_gpu = max(1, int(min_pixels_for_gpu))
        self.last_backend = "cpu"
        self.gpu_name = _get_gpu_device_name()

    def _should_use_gpu(self, pixel_count: int) -> bool:
        if not CUPY_AVAILABLE:
            return False

        mode = str(self.use_gpu).lower()
        if mode == "never":
            return False
        if mode == "force":
            return True
        if mode in {"auto", "true", "yes"}:
            return pixel_count >= self.min_pixels_for_gpu
        if isinstance(self.use_gpu, bool):
            return bool(self.use_gpu)
        return False

    def _select_sample_indices(
        self,
        total_pixels: int,
        use_gpu: bool,
        weights: Optional[np.ndarray],
    ) -> Optional[np.ndarray]:
        limit = self.max_samples_gpu if use_gpu else self.max_samples_cpu
        if limit <= 0 or total_pixels <= limit:
            return None

        if weights is not None:
            probabilities = weights / float(weights.sum())
            return np.random.choice(
                total_pixels, limit, replace=False, p=probabilities
            )

        return np.random.choice(total_pixels, limit, replace=False)

    def _prepare_pixels(
        self,
        region_array: np.ndarray,
        mask: Optional[np.ndarray],
    ) -> Optional[Tuple[np.ndarray, np.ndarray, np.ndarray]]:
        if mask is None:
            mask_bool = np.ones(region_array.shape[:2], dtype=bool)
        else:
            mask_bool = mask.astype(np.uint8) > 0

        if not mask_bool.any():
            return None

        flat_mask = mask_bool.reshape(-1)
        pixels = region_array.reshape(-1, 3)[flat_mask]

        if pixels.size == 0:
            return None

        pixels = pixels.astype(np.float32)

        if self.lab_space:
            features = _rgb_to_lab_array(np, (pixels / 255.0).astype(np.float32))
        else:
            features = pixels
        weights = np.ones(pixels.shape[0], dtype=np.float64)

        if mask is not None and cv2 is not None:
            try:
                kernel = np.ones((3, 3), dtype=np.uint8)
                interior = cv2.erode(mask, kernel, iterations=1)
                edge_mask = (mask > 0) & (interior == 0)
                if edge_mask.any():
                    flat_edges = edge_mask.reshape(-1)[flat_mask]
                    if flat_edges.any():
                        weights[flat_edges] *= self.edge_bias

                gray = cv2.cvtColor(region_array, cv2.COLOR_RGB2GRAY)
                gray_masked = gray.copy()
                gray_masked[~mask_bool] = 255

                _, thresh = cv2.threshold(
                    gray_masked, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU
                )

                dark_mask = (thresh == 0) & mask_bool
                light_mask = (thresh == 255) & mask_bool

                text_mask_full = None
                if dark_mask.any() and light_mask.any():
                    if dark_mask.sum() <= light_mask.sum():
                        text_mask_full = dark_mask
                    else:
                        text_mask_full = light_mask

                if text_mask_full is not None and text_mask_full.any():
                    binary = np.zeros_like(mask_bool, dtype=np.uint8)
                    binary[mask_bool] = 1
                    binary[text_mask_full] = 0
                    distance = cv2.distanceTransform(binary, cv2.DIST_L2, 3)
                    distance_flat = distance.reshape(-1)[flat_mask]
                    weights *= 1.0 / (1.0 + distance_flat)
            except Exception:
                pass

        total_weight = weights.sum()
        if total_weight > 0:
            weights *= float(pixels.shape[0]) / total_weight

        return pixels, features, weights

    def _kmeans_numpy(
        self,
        data: np.ndarray,
        cluster_count: int,
    ) -> np.ndarray:
        rng = np.random.default_rng()
        centroids = data[rng.choice(data.shape[0], cluster_count, replace=False)]
        labels = None

        for _ in range(self.max_iter):
            distances = ((data[:, None, :] - centroids[None, :, :]) ** 2).sum(axis=2)
            new_labels = distances.argmin(axis=1)

            if labels is not None and np.array_equal(new_labels, labels):
                break

            labels = new_labels
            for idx in range(cluster_count):
                mask = labels == idx
                if not np.any(mask):
                    centroids[idx] = data[rng.integers(0, data.shape[0])]
                else:
                    centroids[idx] = data[mask].mean(axis=0)

        return centroids.astype(np.float32)

    def _aggregate_results(
        self,
        pixels: np.ndarray,
        labels: np.ndarray,
        cluster_count: int,
        weights: np.ndarray,
    ) -> List[Dict[str, object]]:
        if cluster_count == 0:
            return []

        counts = np.bincount(
            labels, weights=weights, minlength=cluster_count
        ).astype(np.float64)
        total_weight = float(counts.sum())
        if total_weight <= 0.0:
            return []

        accum = np.zeros((cluster_count, 3), dtype=np.float64)
        np.add.at(accum, labels, pixels * weights[:, None])

        safe_counts = np.maximum(counts[:, None], 1e-8)
        avg_rgb = accum / safe_counts
        percentages = counts / total_weight * 100.0

        order = np.argsort(-counts)
        results: List[Dict[str, object]] = []

        for idx in order:
            if counts[idx] <= 0:
                continue

            rgb = np.clip(avg_rgb[idx], 0.0, 255.0).astype(np.float32)
            rgb_list = [int(round(c)) for c in rgb.tolist()]
            hex_value = "#{:02x}{:02x}{:02x}".format(*rgb_list)

            results.append(
                {
                    "rgb": rgb_list,
                    "hex": hex_value,
                    "percentage": round(float(percentages[idx]), 1),
                }
            )

        return results

    def _extract_cpu(
        self,
        pixels: np.ndarray,
        features: np.ndarray,
        sample_features: np.ndarray,
        cluster_count: int,
        weights: np.ndarray,
    ) -> List[Dict[str, object]]:
        if sample_features.shape[0] < cluster_count:
            cluster_count = sample_features.shape[0]

        if cluster_count <= 0:
            return []

        try:
            centroids = self._kmeans_numpy(sample_features, cluster_count)
        except Exception:
            centroids = sample_features[:cluster_count]

        distances = ((features[:, None, :] - centroids[None, :, :]) ** 2).sum(axis=2)
        labels = distances.argmin(axis=1)

        self.last_backend = "cpu"
        return self._aggregate_results(pixels, labels, cluster_count, weights)

    def _extract_gpu(
        self,
        pixels: np.ndarray,
        features: np.ndarray,
        sample_features: np.ndarray,
        cluster_count: int,
        weights: np.ndarray,
    ) -> List[Dict[str, object]]:
        if not CUPY_AVAILABLE:
            raise RuntimeError("CuPy is not available")

        if sample_features.shape[0] < cluster_count:
            cluster_count = sample_features.shape[0]

        if cluster_count <= 0:
            return []

        pixels_gpu = cp.asarray(pixels, dtype=cp.float32)
        features_gpu = cp.asarray(features, dtype=cp.float32)
        sample_features_gpu = cp.asarray(sample_features, dtype=cp.float32)

        try:
            centroids, _ = kmeans2(
                sample_features_gpu,
                cluster_count,
                iter=self.max_iter,
                minit="points",
            )
        except Exception as exc:
            raise RuntimeError("CuPy k-means failed") from exc

        centroids = centroids.astype(cp.float32)
        distances = cp.sum(
            (features_gpu[:, None, :] - centroids[None, :, :]) ** 2, axis=2
        )
        labels = cp.argmin(distances, axis=1).astype(cp.int32)

        weights_gpu = cp.asarray(weights, dtype=cp.float64)
        counts = cp.bincount(labels, weights=weights_gpu, minlength=cluster_count)
        total_weight = float(cp.asnumpy(counts.sum()))
        if total_weight <= 0.0:
            return []

        accum = cp.zeros((cluster_count, 3), dtype=cp.float64)
        cp.add.at(accum, labels, pixels_gpu.astype(cp.float64) * weights_gpu[:, None])

        safe_counts = cp.maximum(counts[:, None], 1e-8)
        avg_rgb = accum / safe_counts
        percentages = counts / total_weight * 100.0

        counts_np = cp.asnumpy(counts)
        avg_rgb_np = cp.asnumpy(avg_rgb)
        percentages_np = cp.asnumpy(percentages)

        order = np.argsort(-counts_np)
        results: List[Dict[str, object]] = []

        for idx in order:
            if counts_np[idx] <= 0:
                continue

            rgb = np.clip(avg_rgb_np[idx], 0.0, 255.0).astype(np.float32)
            rgb_list = [int(round(c)) for c in rgb.tolist()]
            hex_value = "#{:02x}{:02x}{:02x}".format(*rgb_list)

            results.append(
                {
                    "rgb": rgb_list,
                    "hex": hex_value,
                    "percentage": round(float(percentages_np[idx]), 1),
                }
            )

        self.last_backend = "gpu"

        try:
            cp.get_default_memory_pool().free_all_blocks()
        except Exception:  # pragma: no cover - cleanup best effort
            pass

        return results

    def extract_colors(
        self,
        region_array: np.ndarray,
        mask: Optional[np.ndarray] = None,
    ) -> List[Dict[str, object]]:
        prepared = self._prepare_pixels(region_array, mask)
        if prepared is None:
            return []

        pixels, features, weights = prepared
        total_pixels = pixels.shape[0]

        use_gpu = self._should_use_gpu(total_pixels)
        sample_indices = self._select_sample_indices(
            total_pixels,
            use_gpu,
            weights,
        )

        if sample_indices is not None:
            sample_pixels = pixels[sample_indices]
            sample_features = features[sample_indices]
        else:
            sample_pixels = pixels
            sample_features = features

        unique_colors = np.unique(sample_pixels.astype(np.uint8), axis=0)
        cluster_count = min(self.n_colors, unique_colors.shape[0])

        if cluster_count <= 0:
            return []

        try:
            if use_gpu:
                return self._extract_gpu(
                    pixels,
                    features,
                    sample_features,
                    cluster_count,
                    weights,
                )
        except Exception:
            use_gpu = False

        return self._extract_cpu(
            pixels,
            features,
            sample_features,
            cluster_count,
            weights,
        )

    def backend_status(self) -> str:
        if self.last_backend == "gpu" and CUPY_AVAILABLE:
            return f"GPU ({self.gpu_name or 'CUDA'})"
        if self.last_backend == "gpu":
            return "GPU (CuPy unavailable)"
        if not CUPY_AVAILABLE:
            return "CPU (CuPy not available)"
        return "CPU"


def _choose_background_foreground(
    colors: List[Dict[str, object]]
) -> Tuple[Optional[Dict[str, object]], Optional[Dict[str, object]]]:
    if not colors:
        return None, None

    metrics: List[Dict[str, object]] = []
    for idx, entry in enumerate(colors):
        rgb = entry.get("rgb") or entry.get("color")
        if not isinstance(rgb, Sequence) or len(rgb) < 3:
            continue
        rgb_arr = np.array(
            [float(rgb[0]), float(rgb[1]), float(rgb[2])], dtype=np.float32
        )
        brightness = float(
            0.2126 * rgb_arr[0] + 0.7152 * rgb_arr[1] + 0.0722 * rgb_arr[2]
        )
        saturation = float(np.max(rgb_arr) - np.min(rgb_arr))
        metrics.append(
            {
                "idx": idx,
                "brightness": brightness,
                "saturation": saturation,
                "percentage": float(entry.get("percentage", 0.0)),
                "rgb": rgb_arr,
            }
        )

    if not metrics:
        return colors[0], colors[0]

    metrics_sorted = sorted(metrics, key=lambda m: m["percentage"], reverse=True)
    background_idx = metrics_sorted[0]["idx"]

    bright_candidates = [
        m
        for m in metrics
        if m["percentage"] >= 12.0 and m["brightness"] >= 200.0 and m["saturation"] <= 45.0
    ]
    if bright_candidates:
        background_idx = max(
            bright_candidates, key=lambda m: (m["percentage"], m["brightness"])
        )["idx"]
    else:
        top = metrics_sorted[0]
        alt_candidates = [
            m
            for m in metrics
            if m["idx"] != top["idx"]
            and m["percentage"] >= 15.0
            and abs(m["brightness"] - top["brightness"]) >= 60.0
        ]
        if top["brightness"] < 130.0 and alt_candidates:
            background_idx = max(
                alt_candidates, key=lambda m: (m["brightness"], m["percentage"])
            )["idx"]

    background = colors[background_idx]

    background_rgb = np.array(
        background.get("rgb") or background.get("color") or [0.0, 0.0, 0.0],
        dtype=np.float32,
    )
    foreground_idx = background_idx
    max_distance = -1.0

    for metric in metrics:
        if metric["idx"] == background_idx:
            continue
        distance = float(np.linalg.norm(metric["rgb"] - background_rgb))
        if distance > max_distance:
            max_distance = distance
            foreground_idx = metric["idx"]

    if foreground_idx == background_idx and len(colors) > 1:
        for idx in range(len(colors)):
            if idx != background_idx:
                foreground_idx = idx
                break

    foreground = colors[foreground_idx]
    return background, foreground


def _get_color_extractor() -> Optional["_GPUColorExtractor"]:
    """Lazily instantiate the global ColorExtractor."""

    global _COLOR_EXTRACTOR

    with _COLOR_EXTRACTOR_LOCK:
        if _COLOR_EXTRACTOR is None:
            _COLOR_EXTRACTOR = _GPUColorExtractor(
                n_colors=4,
                lab_space=True,
                preprocessing=False,
                use_gpu="auto",
            )

            # Check actual CuPy availability for initialization message
            if CUPY_AVAILABLE:
                gpu_name = _COLOR_EXTRACTOR.gpu_name
                if gpu_name:
                    backend_msg = f"GPU ({gpu_name})"
                else:
                    backend_msg = "GPU (CUDA)"
            else:
                backend_msg = "CPU (CuPy not available)"
            print(
                f"UGT CuPy Color Extractor initialized - {backend_msg}"
            )
    return _COLOR_EXTRACTOR


def _format_color_entry(color: Dict[str, object]) -> Dict[str, object]:
    """Normalize the color entry to include hex, rgb, and percentage."""

    rgb_values: List[int] = []
    raw_rgb = color.get("rgb")
    raw_bgr = color.get("bgr") or color.get("color")

    if isinstance(raw_rgb, Sequence):
        rgb_values = [int(max(0, min(255, int(c)))) for c in raw_rgb]
    elif isinstance(raw_bgr, Sequence):
        bgr_values = [int(max(0, min(255, int(c)))) for c in raw_bgr]
        if len(bgr_values) == 3:
            rgb_values = [bgr_values[2], bgr_values[1], bgr_values[0]]
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
    background, foreground = _choose_background_foreground(colors)
    if background is None:
        return None
    if foreground is None:
        foreground = background

    foreground_formatted = _format_color_entry(foreground)
    background_formatted = _format_color_entry(background)
    
    return {
        "background_color": background_formatted,
        "foreground_color": foreground_formatted,
        "color_source": "ugtlive_gpu_kmeans",
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

