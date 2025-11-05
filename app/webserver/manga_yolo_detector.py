"""Utilities for running region detection using the Manga109 YOLO model."""

from __future__ import annotations

import importlib
import os
import subprocess
import sys
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


def _import_ultralytics() -> None:
    """Ensure ultralytics is available and patched with required modules."""

    global _ULTRA_BLOCK, _YOLO_CLASS, _UPGRADE_ATTEMPTED

    if _ULTRA_BLOCK is not None and _YOLO_CLASS is not None:
        return

    ultralytics = importlib.import_module("ultralytics")  # type: ignore

    block_mod = importlib.import_module("ultralytics.nn.modules.block")
    block = block_mod
    _debug(f"Loaded detector helper from: {__file__}")
    _debug(f"ultralytics version: {getattr(ultralytics, '__version__', 'unknown')}")
    _debug(f"block module path: {getattr(block, '__file__', 'n/a')}")
    _debug(f"Initial block exports: {[name for name in dir(block) if name.startswith('C3')]}")

    needs_upgrade = not hasattr(block, "C3k") or not hasattr(block, "PSABlock")

    if needs_upgrade and not _UPGRADE_ATTEMPTED:
        _UPGRADE_ATTEMPTED = True
        target = "ultralytics>=8.3.23"
        _debug(f"Required modules missing; attempting pip install {target}")
        try:
            subprocess.check_call([sys.executable, "-m", "pip", "install", "--upgrade", target])
        except Exception as exc:  # pragma: no cover - installation issues
            _debug(f"pip install failed: {exc}")
        else:
            importlib.invalidate_caches()
            module = importlib.import_module("ultralytics")  # type: ignore
            module = importlib.reload(module)  # type: ignore[arg-type]
            block_mod = importlib.import_module("ultralytics.nn.modules.block")
            block = importlib.reload(block_mod)
            _debug(
                "After upgrade, block exports: %s"
                % [name for name in dir(block) if name.startswith("C3")]
            )

    # Provide fallbacks if features are still absent
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

    if not hasattr(block, "C3k2") and hasattr(block, "C3k"):
        _debug("Injecting fallback implementation for C3k2")

        class C3k2(block.C3k):  # type: ignore[attr-defined]
            def __init__(self, *args: Any, **kwargs: Any) -> None:
                kwargs.setdefault("k", 2)
                super().__init__(*args, **kwargs)

        setattr(block, "C3k2", C3k2)
        if hasattr(block, "__all__") and isinstance(block.__all__, list):
            block.__all__.append("C3k2")

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
                "Could not find manga109 YOLO model at "
                f"'{_MODEL_PATH}'. Please run SetupMangaStuff.bat first."
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
            pass


def _guess_extension(source_name: Optional[str]) -> str:
    if not source_name:
        return ".png"

    suffix = Path(source_name).suffix
    return suffix if suffix else ".png"


def _format_result(result: Any) -> Dict[str, Any]:
    image_height, image_width = result.orig_shape[:2]
    names = result.names

    detections: List[Dict[str, Any]] = []

    for box in result.boxes:
        xyxy = box.xyxy[0].tolist()
        x1, y1, x2, y2 = map(float, xyxy)

        confidence = float(box.conf[0]) if box.conf is not None else None
        class_id = int(box.cls[0]) if box.cls is not None else None

        label = None
        if class_id is not None:
            if isinstance(names, dict):
                label = names.get(class_id, f"class_{class_id}")
            elif isinstance(names, list) and class_id < len(names):
                label = names[class_id]
            else:
                label = f"class_{class_id}"

        polygon = [
            [x1, y1],
            [x2, y1],
            [x2, y2],
            [x1, y2],
        ]

        detections.append(
            {
                "label": label,
                "class_id": class_id,
                "confidence": confidence,
                "bbox": {
                    "x_min": x1,
                    "y_min": y1,
                    "x_max": x2,
                    "y_max": y2,
                    "width": x2 - x1,
                    "height": y2 - y1,
                },
                "polygon": polygon,
            }
        )

    return {
        "image_size": {"width": int(image_width), "height": int(image_height)},
        "detections": detections,
    }


def render_debug_image(image_path: Union[str, Path], detections: List[Dict[str, Any]], output_path: Path) -> None:
    """Render detection polygons into an image and save the debug visualization."""

    output_path.parent.mkdir(parents=True, exist_ok=True)

    image = Image.open(image_path).convert("RGB")
    draw = ImageDraw.Draw(image, "RGBA")

    try:
        font = ImageFont.truetype("arial.ttf", size=16)
    except OSError:
        font = ImageFont.load_default()

    color_cycle = {
        "body": (255, 0, 0, 120),
        "face": (0, 128, 255, 120),
        "text": (0, 255, 0, 120),
        "frame": (255, 215, 0, 120),
    }

    outline_cycle = {
        "body": (255, 0, 0, 255),
        "face": (30, 144, 255, 255),
        "text": (34, 139, 34, 255),
        "frame": (255, 140, 0, 255),
    }

    for det in detections:
        label = det.get("label") or "unknown"
        polygon = det.get("polygon")
        bbox = det.get("bbox")
        confidence = det.get("confidence")

        if polygon:
            points = [tuple(point) for point in polygon]
        elif bbox:
            points = [
                (bbox["x_min"], bbox["y_min"]),
                (bbox["x_max"], bbox["y_min"]),
                (bbox["x_max"], bbox["y_max"]),
                (bbox["x_min"], bbox["y_max"]),
            ]
        else:
            continue

        fill_color = color_cycle.get(label, (255, 255, 255, 80))
        outline_color = outline_cycle.get(label, (255, 255, 255, 200))

        draw.polygon(points, outline=outline_color, fill=fill_color)

        if confidence is not None:
            text = f"{label} ({confidence:.2f})"
        else:
            text = label

        text_position = points[0]
        text_bbox = draw.textbbox(text_position, text, font=font)
        padding = 4
        bg_bbox = (
            text_bbox[0] - padding,
            text_bbox[1] - padding,
            text_bbox[2] + padding,
            text_bbox[3] + padding,
        )
        draw.rectangle(bg_bbox, fill=(0, 0, 0, 170))
        draw.text((text_bbox[0], text_bbox[1]), text, font=font, fill=(255, 255, 255, 255))

    image.save(output_path)
    _debug(f"Wrote debug image to {output_path}")


__all__ = [
    "ModelNotFoundError",
    "detect_regions_from_bytes",
    "detect_regions_from_path",
    "render_debug_image",
]


