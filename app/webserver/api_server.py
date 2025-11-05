"""FastAPI application exposing the Manga109 YOLO region detection API."""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Any, Dict, Optional

from fastapi import FastAPI, File, HTTPException, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from manga_yolo_detector import ModelNotFoundError, detect_regions_from_bytes
DEBUG_DRAW_DETECTIONS = True
DEBUG_OUTPUT_DIR = Path(__file__).resolve().parent / "debug_outputs"
if DEBUG_DRAW_DETECTIONS:
    DEBUG_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)



logger = logging.getLogger("manga_yolo_api")
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)


app = FastAPI(
    title="Manga109 Region Detection API",
    description="HTTP API for running region detection using the Manga109 YOLO model.",
    version="1.0.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.get("/health", summary="Health check")
def health_check() -> Dict[str, str]:
    """Simple health endpoint so clients can verify the server is online."""

    return {"status": "ok"}


@app.post("/api/detect", summary="Run region detection")
async def detect_regions(image: UploadFile = File(...)) -> JSONResponse:
    """Accept an uploaded image and return region detections."""

    if not image.filename:
        raise HTTPException(status_code=400, detail="No filename provided for upload")

    content = await image.read()

    if not content:
        raise HTTPException(status_code=400, detail="Uploaded file is empty")

    debug_path: Optional[Path] = None
    if DEBUG_DRAW_DETECTIONS:
        suffix = Path(image.filename).suffix or ".png"
        stem = Path(image.filename).stem or "upload"
        debug_path = DEBUG_OUTPUT_DIR / f"{stem}_debug{suffix}"

    try:
        result = detect_regions_from_bytes(
            content,
            source_name=image.filename,
            debug_output_path=debug_path,
        )
    except ModelNotFoundError as exc:
        logger.error("Model not found: %s", exc)
        raise HTTPException(status_code=500, detail=str(exc)) from exc
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except Exception as exc:  # pragma: no cover - catch unexpected errors
        logger.exception("Unexpected error during detection")
        raise HTTPException(status_code=500, detail="Failed to run detection") from exc

    response: Dict[str, Any] = {
        "status": "success",
        "detections": result["detections"],
        "image_size": result["image_size"],
        "count": len(result["detections"]),
    }

    if debug_path is not None:
        response["debug_image_path"] = str(debug_path)

    return JSONResponse(content=response)


@app.get("/", summary="API metadata")
def root() -> Dict[str, Any]:
    """Return a short description so it's easy to debug from the browser."""

    return {
        "message": "Manga109 Region Detection API",
        "endpoints": {
            "POST /api/detect": "Upload an image file as form-data (field name 'image') to get region detections.",
            "GET /health": "Health probe endpoint.",
        },
    }


if __name__ == "__main__":  # pragma: no cover
    import uvicorn

    uvicorn.run(
        "api_server:app",
        host="0.0.0.0",
        port=8000,
        log_level="info",
    )


