"""FastAPI server for Qwen3-TTS service using faster-qwen3-tts (CUDA graphs)."""

import sys
import os
import io
import time
import asyncio
import warnings
from pathlib import Path
from io import BytesIO
from contextlib import asynccontextmanager
from typing import Optional

# Suppress noisy third-party warnings before any ML imports
warnings.filterwarnings("ignore", category=FutureWarning)
warnings.filterwarnings("ignore", category=DeprecationWarning)

os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")

import ssl
import certifi
ssl._create_default_https_context = lambda: ssl.create_default_context(cafile=certifi.where())

import uvicorn
from fastapi import FastAPI, HTTPException
from fastapi.responses import Response, JSONResponse
from pydantic import BaseModel
import numpy as np
import torch
import soundfile as sf

# Suppress qwen_tts import spam (flash-attn warning, SoX check)
_real_stdout = sys.stdout
sys.stdout = io.StringIO()
try:
    from faster_qwen3_tts import FasterQwen3TTS
finally:
    sys.stdout = _real_stdout

# Add shared folder to path
shared_dir = Path(__file__).parent.parent / "shared"
sys.path.insert(0, str(shared_dir))

from config_parser import parse_service_config, get_config_value

# Load service configuration
config_path = Path(__file__).parent / "service_config.txt"
SERVICE_CONFIG = parse_service_config(str(config_path))

SERVICE_NAME = get_config_value(SERVICE_CONFIG, 'service_name', 'Qwen3TTS')
SERVICE_PORT = int(get_config_value(SERVICE_CONFIG, 'port', '5004'))
SERVICE_INSTALL_VERSION = get_config_value(SERVICE_CONFIG, 'service_install_version', '1')

# Global model reference
TTS_MODEL = None

SUPPORTED_SPEAKERS = [
    "aiden", "dylan", "eric", "ono_anna", "ryan",
    "serena", "sohee", "uncle_fu", "vivian",
]

DEFAULT_MODEL_ID = "Qwen/Qwen3-TTS-12Hz-0.6B-CustomVoice"


def normalize_audio(audio, target_peak=0.95):
    """Peak-normalize audio so the loudest sample hits target_peak."""
    peak = np.max(np.abs(audio))
    if peak > 0 and peak < target_peak:
        audio = audio * (target_peak / peak)
    return audio


class TTSRequest(BaseModel):
    text: str
    voice: str = "ono_anna"
    language: str = "Auto"


def print_gpu_info():
    """Print detailed GPU information at startup."""
    print("-" * 60)
    print("GPU Information:")
    try:
        if torch.cuda.is_available():
            device_count = torch.cuda.device_count()
            print(f"  CUDA available: True")
            print(f"  CUDA version: {torch.version.cuda}")
            print(f"  PyTorch version: {torch.__version__}")
            print(f"  GPU count: {device_count}")
            for i in range(device_count):
                props = torch.cuda.get_device_properties(i)
                vram_gb = props.total_memory / (1024 ** 3)
                print(f"  GPU {i}: {props.name}")
                print(f"    VRAM: {vram_gb:.1f} GB")
                print(f"    Compute capability: {props.major}.{props.minor}")
        else:
            print(f"  CUDA available: False")
            print(f"  PyTorch version: {torch.__version__}")
            print(f"  Running on CPU (this will be slow)")
    except Exception as e:
        print(f"  Error querying GPU info: {e}")
    print("-" * 60)


def load_model():
    """Load the Qwen3-TTS model with CUDA graph acceleration.

    Retries up to MAX_RETRIES times on network errors (common on machines with
    flaky HuggingFace connectivity).  After all online attempts fail, tries
    once more in offline mode so a previously-cached model can still be used.
    """
    global TTS_MODEL

    MAX_RETRIES = 3
    RETRY_DELAY = 5

    dtype = torch.bfloat16 if torch.cuda.is_available() else torch.float32
    device = "cuda" if torch.cuda.is_available() else "cpu"

    print(f"Loading Qwen3-TTS model: {DEFAULT_MODEL_ID}")
    print(f"  Device: {device}, Dtype: {dtype}")
    print(f"  Engine: faster-qwen3-tts (CUDA graphs)")

    last_error = None
    for attempt in range(1, MAX_RETRIES + 1):
        try:
            start_time = time.time()
            TTS_MODEL = FasterQwen3TTS.from_pretrained(
                DEFAULT_MODEL_ID,
                device=device,
                dtype=dtype,
                attn_implementation="sdpa",
            )
            elapsed = time.time() - start_time
            print(f"  Model loaded in {elapsed:.1f}s")
            return TTS_MODEL
        except Exception as e:
            last_error = e
            err_str = str(e).lower()
            is_network = any(kw in err_str for kw in [
                "connectionreset", "connection aborted", "protocolerror",
                "connectionerror", "timeout", "10054", "urlopen",
            ])
            if is_network and attempt < MAX_RETRIES:
                print(f"  Network error on attempt {attempt}/{MAX_RETRIES}: {e}")
                print(f"  Retrying in {RETRY_DELAY}s...")
                time.sleep(RETRY_DELAY)
                continue
            break

    # All online attempts failed — try offline mode with cached files
    print(f"  Online loading failed after {MAX_RETRIES} attempts.")
    print(f"  Last error: {last_error}")
    print(f"  Trying offline mode (using cached model files)...")
    try:
        os.environ["HF_HUB_OFFLINE"] = "1"
        start_time = time.time()
        TTS_MODEL = FasterQwen3TTS.from_pretrained(
            DEFAULT_MODEL_ID,
            device=device,
            dtype=dtype,
            attn_implementation="sdpa",
        )
        elapsed = time.time() - start_time
        print(f"  Model loaded from cache in {elapsed:.1f}s (offline mode)")
        return TTS_MODEL
    except Exception as offline_err:
        print(f"  Offline loading also failed: {offline_err}")
        raise last_error
    finally:
        os.environ.pop("HF_HUB_OFFLINE", None)


def warmup_model():
    """Run a throwaway TTS generation to trigger CUDA graph compilation."""
    global TTS_MODEL
    if TTS_MODEL is None:
        return

    print("Warming up TTS model (compiling CUDA graphs)...")
    start_time = time.time()
    try:
        TTS_MODEL.generate_custom_voice(
            text="Warmup complete.",
            language="auto",
            speaker="ono_anna",
        )
        elapsed = time.time() - start_time
        print(f"  Warmup finished in {elapsed:.1f}s — first real request will be fast")
    except Exception as e:
        print(f"  Warmup failed (non-fatal): {e}")


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Pre-load TTS model at startup."""
    print("=" * 60)
    print("PRE-LOADING QWEN3-TTS MODEL AT STARTUP")
    print("=" * 60)

    print_gpu_info()

    try:
        load_model()
        print("[OK] Qwen3-TTS model loaded successfully")
        print(f"  Supported speakers: {SUPPORTED_SPEAKERS}")
        warmup_model()

    except Exception as e:
        print(f"[FAIL] Failed to load Qwen3-TTS model: {e}")
        import traceback
        traceback.print_exc()
        print("Model will be loaded on first request instead.")

    print("[OK] Service is ready for requests!")
    print("=" * 60)

    yield


app = FastAPI(title=SERVICE_NAME, version=SERVICE_INSTALL_VERSION, lifespan=lifespan)


@app.post("/tts")
async def text_to_speech(request: TTSRequest):
    """Generate speech from text. Returns WAV audio bytes."""
    global TTS_MODEL

    if TTS_MODEL is None:
        raise HTTPException(status_code=503, detail="Model not loaded yet")

    if not request.text or not request.text.strip():
        raise HTTPException(status_code=400, detail="No text provided")

    speaker = request.voice.lower()
    if speaker not in SUPPORTED_SPEAKERS:
        raise HTTPException(
            status_code=400,
            detail=f"Unknown voice '{request.voice}'. Available: {SUPPORTED_SPEAKERS}"
        )

    language = request.language if request.language != "Auto" else "auto"

    try:
        start_time = time.time()

        wavs, sr = TTS_MODEL.generate_custom_voice(
            text=request.text,
            language=language,
            speaker=speaker,
        )

        elapsed = time.time() - start_time
        text_preview = request.text[:60] + "..." if len(request.text) > 60 else request.text
        print(f"TTS generated in {elapsed:.2f}s for voice={speaker}, text='{text_preview}'")

        audio = normalize_audio(wavs[0])

        buf = BytesIO()
        sf.write(buf, audio, sr, format="WAV")
        buf.seek(0)

        return Response(
            content=buf.read(),
            media_type="audio/wav",
            headers={"Content-Disposition": "attachment; filename=tts_output.wav"}
        )

    except Exception as e:
        print(f"Error generating TTS: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/info")
async def get_info():
    """Get service information."""
    info = {
        "service_name": get_config_value(SERVICE_CONFIG, 'service_name', 'Qwen3TTS'),
        "description": get_config_value(SERVICE_CONFIG, 'description', ''),
        "service_install_version": get_config_value(SERVICE_CONFIG, 'service_install_version', '1'),
        "venv_name": get_config_value(SERVICE_CONFIG, 'venv_name', 'ugt_qwen3tts'),
        "port": int(get_config_value(SERVICE_CONFIG, 'port', '5004')),
        "server_url": get_config_value(SERVICE_CONFIG, 'server_url', 'http://127.0.0.1'),
        "local_only": get_config_value(SERVICE_CONFIG, 'local_only', 'true') == 'true',
        "github_url": get_config_value(SERVICE_CONFIG, 'github_url', ''),
        "service_author": get_config_value(SERVICE_CONFIG, 'service_author', ''),
        "available_voices": SUPPORTED_SPEAKERS,
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
