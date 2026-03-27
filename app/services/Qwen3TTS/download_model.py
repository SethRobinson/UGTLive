"""Pre-download Qwen3-TTS model files from HuggingFace with retry logic.

Called during Install.bat so the model is cached before the server first starts.
Uses snapshot_download which shows real-time download progress and doesn't require CUDA.
Retries with exponential backoff for flaky connections.
"""

import os
import sys
import time

os.environ["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1"

MODEL_ID = "Qwen/Qwen3-TTS-12Hz-0.6B-CustomVoice"
MAX_RETRIES = 5
BASE_DELAY = 5


def download_with_retry():
    from huggingface_hub import snapshot_download

    for attempt in range(1, MAX_RETRIES + 1):
        try:
            print(f"  Attempt {attempt}/{MAX_RETRIES}: Downloading {MODEL_ID}...")
            print(f"  (Download progress will appear below)")
            path = snapshot_download(MODEL_ID)
            print(f"  Model cached at: {path}")
            return True
        except KeyboardInterrupt:
            print("\n  Download cancelled by user.")
            return False
        except Exception as e:
            delay = BASE_DELAY * (2 ** (attempt - 1))
            print(f"  Attempt {attempt} failed: {e}")
            if attempt < MAX_RETRIES:
                print(f"  Retrying in {delay}s...")
                time.sleep(delay)

    print(f"  All {MAX_RETRIES} download attempts failed.")
    return False


if __name__ == "__main__":
    print("=" * 60)
    print("PRE-DOWNLOADING QWEN3-TTS MODEL")
    print("=" * 60)

    success = download_with_retry()
    if not success:
        print()
        print("ERROR: Could not download the model after multiple attempts.")
        print("The service will try again when started, but it won't be")
        print("usable until the download succeeds.")
        print("HuggingFace may be having issues -- try again later.")
        sys.exit(1)

    print("\nModel pre-download complete!")
    sys.exit(0)
