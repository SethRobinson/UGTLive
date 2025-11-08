"""Utilities for running region detection using the Manga109 YOLO model."""

from __future__ import annotations

import importlib
import os
from pathlib import Path
from tempfile import NamedTemporaryFile
from types import ModuleType
from typing import Any, Dict, List, Optional, Type, Union

from PIL import Image, ImageDraw, ImageFont


_DEBUG_TAG = "[manga_yolo_detector v0.3]"


def _debug(msg: str) -> None:
    print(f"{_DEBUG_TAG} {msg}")


_ULTRA_BLOCK: Optional[ModuleType] = None
_YOLO_CLASS: Optional[Type[Any]] = None
_MODEL_HANDLE: Optional[Any] = None
_UPGRADE_ATTEMPTED = False
_MODEL_PATH = Path(__file__).resolve().parent / "models" / "manga109_yolo" / "model.pt"


class ModelNotFoundError(FileNotFoundError):
    """Raised when the expected YOLO model file is missing."""


class _GitWrapper:
    """Minimal helper that mimics the parts of ultralytics.utils.GIT we rely on."""

    def __init__(self) -> None:
        self.is_repo = False

    def __getattr__(self, item: str) -> Any:  # pragma: no cover - defensive
        if item == "is_repo":
            return False
        raise AttributeError(item)


def _ensure_utils_git() -> None:
    """Ensure ultralytics.utils exposes the GIT attribute expected by newer callbacks."""

    try:
        utils_mod = importlib.import_module("ultralytics.utils")
    except ImportError:  # pragma: no cover - defensive guard
        return

    if not hasattr(utils_mod, "GIT"):
        _debug("Injecting stub GIT metadata into ultralytics.utils")
        setattr(utils_mod, "GIT", _GitWrapper())
        all_attr = getattr(utils_mod, "__all__", None)
        if isinstance(all_attr, list) and "GIT" not in all_attr:
            all_attr.append("GIT")


def _inject_block_fallbacks(block: ModuleType) -> None:
    """Provide lightweight stand-ins for modules missing in older ultralytics builds."""

    if not hasattr(block, "C3k") and hasattr(block, "C3"):
        _debug("Injecting fallback implementation for C3k using base C3")

        class C3k(block.C3):  # type: ignore[attr-defined]
            def __init__(
                self,
                c1: int,
                c2: int,
                n: int = 1,
                shortcut: bool = False,
                g: int = 1,
                e: float = 0.5,
                k: int = 2,
            ) -> None:
                super().__init__(c1, c2, n=n, shortcut=shortcut, g=g, e=e)

        setattr(block, "C3k", C3k)
        if hasattr(block, "__all__") and isinstance(block.__all__, list):
            block.__all__.append("C3k")

    # C3k2 fallback for older ultralytics versions (< 8.3.0)
    if not hasattr(block, "C3k2"):
        _debug("Injecting fallback implementation for C3k2")
        # For ultralytics < 8.3.0, C3k2 might not exist
        # Just use C3 as a fallback since they're similar
        if hasattr(block, "C3"):
            setattr(block, "C3k2", block.C3)
            if hasattr(block, "__all__") and isinstance(block.__all__, list):
                block.__all__.append("C3k2")

    if not hasattr(block, "PSABlock"):
        try:
            import torch.nn as nn  # type: ignore
        except ImportError:  # pragma: no cover - defensive
            nn = None

        if nn is not None and hasattr(block, "PSA"):
            _debug("Injecting fallback implementation for PSABlock")

            class PSABlock(nn.Module):  # type: ignore[assignment]
                def __init__(
                    self,
                    c: int,
                    attn_ratio: float = 0.5,
                    num_heads: int = 4,
                    shortcut: bool = True,
                ) -> None:
                    super().__init__()
                    self.attn = block.PSA(c, c, e=attn_ratio)
                    self.shortcut = shortcut

                def forward(self, x):  # type: ignore[override]
                    y = self.attn(x)
                    return x + y if self.shortcut else y

            setattr(block, "PSABlock", PSABlock)
            if hasattr(block, "__all__") and isinstance(block.__all__, list):
                block.__all__.append("PSABlock")
        else:
            _debug("PSABlock fallback unavailable (missing torch.nn or block.PSA)")

    if not hasattr(block, "C2PSA") and hasattr(block, "C2f") and hasattr(block, "PSA"):
        _debug("Injecting fallback implementation for C2PSA")

        class C2PSA(block.C2f):  # type: ignore[attr-defined]
            def __init__(self, c1: int, c2: int, n: int = 1, shortcut: bool = False, g: int = 1, e: float = 0.5):
                super().__init__(c1, c2, n=n, shortcut=shortcut, g=g, e=e)
                self.attn = block.PSA(c2)

            def forward(self, x):
                y = super().forward(x)
                return self.attn(y)

        setattr(block, "C2PSA", C2PSA)
        if hasattr(block, "__all__") and isinstance(block.__all__, list):
            block.__all__.append("C2PSA")


def _import_ultralytics() -> None:
    """Ensure ultralytics is available and patched with required modules."""

    global _ULTRA_BLOCK, _YOLO_CLASS, _UPGRADE_ATTEMPTED

    if _ULTRA_BLOCK is not None and _YOLO_CLASS is not None:
        return

    ultralytics = importlib.import_module("ultralytics")  # type: ignore
    _ensure_utils_git()

    block_mod = importlib.import_module("ultralytics.nn.modules.block")
    block = block_mod
    _inject_block_fallbacks(block)
    _debug(f"Loaded detector helper from: {__file__}")
    _debug(f"ultralytics version: {getattr(ultralytics, '__version__', 'unknown')}")
    _debug(f"block module path: {getattr(block, '__file__', 'n/a')}")
    _debug(f"Initial block exports: {[name for name in dir(block) if name.startswith('C3')]}")

    needs_upgrade = not hasattr(block, "C3k") or not hasattr(block, "PSABlock")
    if needs_upgrade and not _UPGRADE_ATTEMPTED:
        _UPGRADE_ATTEMPTED = True
        _debug("Required Ultralytics modules still missing; consider upgrading Ultralytics manually if issues persist.")

    _debug(
        "Post-shim C3k? %s | C3k2? %s | C2PSA? %s | PSABlock? %s"
        % (
            hasattr(block, "C3k"),
            hasattr(block, "C3k2"),
            hasattr(block, "C2PSA"),
            hasattr(block, "PSABlock"),
        )
    )

    _ULTRA_BLOCK = block
    from ultralytics import YOLO  # type: ignore

    _YOLO_CLASS = YOLO


def _ensure_model() -> Any:
    """Load the YOLO model lazily and cache the handle for reuse."""

    global _MODEL_HANDLE

    if _MODEL_HANDLE is None:
        _import_ultralytics()
        assert _YOLO_CLASS is not None

        block = _ULTRA_BLOCK
        _debug(
            "Ensuring model. Block has C3k? %s | C3k2? %s"
            % (hasattr(block, "C3k") if block else False, hasattr(block, "C3k2") if block else False)
        )

        if not _MODEL_PATH.exists():
            raise ModelNotFoundError(
                "Could not find Manga109 YOLO model at "
                f"'{_MODEL_PATH}'. Please rerun SetupServerCondaEnvNVidia.bat to download the assets."
            )

        _debug(f"About to load model from {_MODEL_PATH}")
        _MODEL_HANDLE = _YOLO_CLASS(str(_MODEL_PATH))
        _debug("Model loaded successfully")

    return _MODEL_HANDLE


def detect_regions_from_path(
    image_path: str,
    *,
    conf_threshold: float = 0.25,
    iou_threshold: float = 0.45,
    debug_output_path: Optional[Path] = None,
) -> Dict[str, Any]:
    """Run detection on an image located on disk."""

    model = _ensure_model()

    results = model.predict(
        source=image_path,
        conf=conf_threshold,
        iou=iou_threshold,
        imgsz=1024,
        verbose=False,
    )

    if not results:
        return {
            "image_size": None,
            "detections": [],
        }

    formatted = _format_result(results[0])

    if debug_output_path is not None:
        try:
            render_debug_image(image_path, formatted["detections"], debug_output_path)
        except Exception as exc:  # pragma: no cover - best-effort debug output
            _debug(f"Failed to write debug image: {exc}")

    return formatted


def detect_regions_from_bytes(
    image_bytes: bytes,
    *,
    source_name: Optional[str] = None,
    conf_threshold: float = 0.25,
    iou_threshold: float = 0.45,
    debug_output_path: Optional[Union[str, Path]] = None,
) -> Dict[str, Any]:
    """Run detection on bytes representing an image."""

    if not image_bytes:
        raise ValueError("No image data provided for detection")

    suffix = _guess_extension(source_name)

    with NamedTemporaryFile(delete=False, suffix=suffix) as temp_file:
        temp_file.write(image_bytes)
        temp_path = temp_file.name

    try:
        debug_path = Path(debug_output_path) if debug_output_path is not None else None
        return detect_regions_from_path(
            temp_path,
            conf_threshold=conf_threshold,
            iou_threshold=iou_threshold,
            debug_output_path=debug_path,
        )
    finally:
        try:
            os.remove(temp_path)
        except OSError:
            _debug(f"Failed to remove temporary file: {temp_path}")


def render_debug_image(image_path: str, detections: List[Dict[str, Any]], output_path: Path) -> None:
    """Render detection boxes for debugging purposes."""

    base_image = Image.open(image_path).convert("RGB")
    draw = ImageDraw.Draw(base_image)

    for idx, detection in enumerate(detections):
        polygon = detection.get("polygon")
        if polygon:
            draw.polygon([tuple(point) for point in polygon], outline="red", width=3)
        else:
            bbox = detection.get("bbox")
            if bbox:
                draw.rectangle([
                    (bbox["x_min"], bbox["y_min"]),
                    (bbox["x_max"], bbox["y_max"])
                ], outline="red", width=3)
        label = detection.get("label", "text")
        confidence = detection.get("confidence", 0.0)
        draw.text((detection.get("bbox", {}).get("x_min", 0), detection.get("bbox", {}).get("y_min", 0) - 12),
                  f"{label}: {confidence:.2f}", fill="yellow")

    base_image.save(output_path)


def _guess_extension(source_name: Optional[str]) -> str:
    if source_name and "." in source_name:
        return source_name[source_name.rfind(".") :]
    return ".png"


def _format_result(result: Any) -> Dict[str, Any]:
    detections = []
    image_size = getattr(result, "orig_img", None)
    width: Optional[int] = None
    height: Optional[int] = None

    if image_size is not None:
        try:
            height, width = image_size.shape[:2]
        except AttributeError:
            pass

    for box in result.boxes:
        bbox_tensor = box.xyxy[0]
        x_min, y_min, x_max, y_max = [float(value) for value in bbox_tensor.tolist()]
        polygon = [
            [x_min, y_min],
            [x_max, y_min],
            [x_max, y_max],
            [x_min, y_max],
        ]
        detections.append(
            {
                "bbox": {
                    "x_min": x_min,
                    "y_min": y_min,
                    "x_max": x_max,
                    "y_max": y_max,
                },
                "polygon": polygon,
                "confidence": float(box.conf[0].item()) if hasattr(box.conf[0], "item") else float(box.conf[0]),
                "label": result.names[int(box.cls[0].item())] if hasattr(box.cls[0], "item") else result.names[int(box.cls[0])],
            }
        )

    return {
        "image_size": {
            "width": width,
            "height": height,
        } if width is not None and height is not None else None,
        "detections": detections,
    }


__all__ = [
    "ModelNotFoundError",
    "detect_regions_from_bytes",
    "detect_regions_from_path",
    "render_debug_image",
]


