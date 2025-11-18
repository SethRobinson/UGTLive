# UGTLive OCR Services

This directory contains modular, independent OCR services for UGTLive. Each service runs as a standalone FastAPI server with its own conda environment, making the system more maintainable and flexible.

## Architecture Overview

### Design Philosophy

The new services architecture replaces the monolithic socket-based Python server with independent FastAPI services:

- **Modular**: Each OCR engine runs in its own service with isolated dependencies
- **FastAPI-based**: Modern HTTP/REST API with persistent connections and binary data support
- **Standardized**: All services follow the same configuration and endpoint structure
- **Scalable**: Easy to add new services or disable unused ones
- **Efficient**: Binary image transfer (not base64), minimal middleware, HTTP keep-alive

### Directory Structure

```
services/
├── util/                      # Shared utilities
│   └── InstallMiniConda.bat  # Miniconda installer
├── shared/                    # Shared Python code and resources
│   ├── config_parser.py      # Service configuration parser
│   ├── response_models.py    # Pydantic response models
│   ├── color_analysis.py     # GPU color extraction
│   ├── test_images/          # Test images for validation
│   └── README.md
├── localdata/                 # Downloaded models and cache (in .gitignore)
│   └── models/               # Auto-downloaded AI models
│       └── manga109_yolo/    # YOLO text detection model (~50MB)
│           ├── model.pt      # Downloaded from HuggingFace
│           └── labels.json
├── EasyOCR/                   # EasyOCR service (port 5000)
│   ├── service_config.txt
│   ├── server.py
│   ├── SetupServerCondaEnv.bat
│   ├── RunServer.bat
│   ├── DiagnosticTest.bat
│   └── TestService.bat
├── MangaOCR/                  # MangaOCR service (port 5001)
│   ├── service_config.txt
│   ├── server.py
│   ├── manga_yolo_detector.py # YOLO text detection (MangaOCR-specific)
│   ├── test_model_download.py # Test YOLO model download
│   ├── SetupServerCondaEnv.bat
│   ├── RunServer.bat
│   ├── DiagnosticTest.bat
│   └── TestService.bat
├── DocTR/                     # DocTR service (port 5002)
│   ├── service_config.txt
│   ├── server.py
│   ├── SetupServerCondaEnv.bat
│   ├── RunServer.bat
│   ├── DiagnosticTest.bat
│   └── TestService.bat
└── README.md                  # This file
```

## Service Configuration

Each service has a `service_config.txt` file with the following format:

```
description|A brief description of the service|
github_url|https://github.com/project/repo|
author|Author or organization name|
service_name|ServiceName|
venv_name|ugt_servicename|
port|5000|
local_only|true|
version|1.0.0|
```

### Configuration Fields

- **description**: Human-readable description of the service (can contain Unicode)
- **github_url**: Link to the OCR engine's repository
- **author**: Creator or maintainer of the OCR engine
- **service_name**: Display name of the service (**must be ASCII, no spaces**)
- **venv_name**: Name of the conda/virtual environment (**must be ASCII, no spaces**)
- **port**: Port number the service listens on (must be unique)
- **local_only**: If "true", binds to 127.0.0.1; if "false", binds to 0.0.0.0
- **version**: Version of the OCR engine

### Encoding Requirements

⚠️ **Important**: For maximum compatibility across different Windows locales:

- **service_name** and **venv_name** must use **ASCII characters only** (no Japanese, Chinese, etc.)
- Other fields (description, author, github_url) can contain Unicode characters
- Config files are read with UTF-8 encoding
- All batch scripts force UTF-8 code page (chcp 65001) for international compatibility

## Available Services

### EasyOCR (Port 5000)

**Description**: Versatile OCR engine supporting multiple languages including Japanese, Korean, Chinese, and English.

**Best for**: General-purpose OCR, multi-language support, good balance of speed and accuracy.

**Environment**: `ugt_easyocr` (venv_name in config)

### MangaOCR (Port 5001)

**Description**: Specialized OCR optimized for Japanese manga and comic text recognition. Uses YOLO for text region detection.

**Best for**: Japanese manga, comic books, stylized Japanese text.

**Environment**: `ugt_mangaocr` (venv_name in config)

**Special Features**:
- YOLO-based text region detection
- Configurable region filtering (min_region_width, min_region_height, overlap_allowed_percent)
- Automatic image padding for small images

### DocTR (Port 5002)

**Description**: Document OCR engine with strong English text recognition (does NOT support Japanese).

**Best for**: English documents, forms, printed text.

**Environment**: `ugt_doctr` (venv_name in config)

**Note**: DocTR does not support Japanese text. Use EasyOCR or MangaOCR for Japanese.

## Standard API Endpoints

All services implement these standard endpoints:

### POST /process

Process an image and return OCR results.

**Request**:
- Method: `POST`
- Content-Type: `application/octet-stream`
- Body: Binary image data (PNG, JPG, etc.)
- Query Parameters:
  - `lang`: Language code (default varies by service)
  - `char_level`: Split text into characters (default: `true`)
  - Service-specific parameters (e.g., MangaOCR's region filtering)

**Response**:
```json
{
  "status": "success",
  "texts": [
    {
      "text": "あ",
      "x": 100,
      "y": 200,
      "width": 30,
      "height": 40,
      "vertices": [[100, 200], [130, 200], [130, 240], [100, 240]],
      "confidence": 0.95,
      "background_color": {"rgb": [255, 255, 255], "hex": "#ffffff", "percentage": 80.0},
      "foreground_color": {"rgb": [0, 0, 0], "hex": "#000000", "percentage": 20.0}
    }
  ],
  "processing_time": 1.234,
  "language": "japan",
  "char_level": true,
  "backend": "gpu"
}
```

### GET /info

Get service information and configuration.

**Response**:
```json
{
  "service_name": "EasyOCR",
  "description": "A versatile OCR engine...",
  "version": "1.7.2",
  "conda_env_name": "ugt_easyocr",  // Note: Python server reads venv_name from config but returns conda_env_name in /info
  "port": 5000,
  "local_only": true,
  "github_url": "https://github.com/JaidedAI/EasyOCR",
  "author": "JaidedAI"
}
```

### GET /health

Health check endpoint.

**Response**:
```json
{
  "status": "healthy",
  "service": "EasyOCR",
  "version": "1.7.2"
}
```

### POST /shutdown

Gracefully shutdown the service.

**Response**:
```json
{
  "status": "success",
  "message": "Service shutting down"
}
```

## Setup and Usage

### Initial Setup

1. **Install Miniconda** (if not already installed):
   ```batch
   cd services\util
   InstallMiniConda.bat
   ```

2. **Setup a service** (e.g., EasyOCR):
   ```batch
   cd services\EasyOCR
   SetupServerCondaEnv.bat
   ```
   
   This will:
   - Detect your GPU (RTX 30/40/50 series)
   - Create a conda environment with appropriate Python version
     - Python 3.10 for RTX 30/40 series
     - Python 3.11 for RTX 50 series
   - Install PyTorch with appropriate CUDA version
     - PyTorch 2.6.0 + CUDA 11.8 for RTX 30/40 series
     - PyTorch nightly + CUDA 12.8 for RTX 50 series
   - Install all dependencies
   - Download required models

3. **Run diagnostic tests**:
   ```batch
   DiagnosticTest.bat
   ```

4. **Start the service**:
   ```batch
   RunServer.bat
   ```

5. **Test the running service**:
   ```batch
   TestService.bat
   ```

### Testing a Service with curl

```batch
# Health check
curl http://localhost:5000/health

# Get service info
curl http://localhost:5000/info

# Process an image
curl -X POST http://localhost:5000/process?lang=japan ^
     -H "Content-Type: application/octet-stream" ^
     --data-binary "@path\to\image.png"
```

## Adding a New Service

To add a new OCR service:

1. **Create service directory**:
   ```
   services/NewOCR/
   ```

2. **Create `service_config.txt`**:
   ```
   description|Your OCR engine description|
   github_url|https://github.com/...|
   author|Author name|
   service_name|NewOCR|
   venv_name|ugt_newocr|
   port|5003|
   local_only|true|
   version|1.0.0|
   ```

3. **Create `server.py`**:
   - Import shared utilities from `../shared/`
   - Parse `service_config.txt` using `config_parser.py`
   - Implement FastAPI endpoints: `/process`, `/info`, `/health`, `/shutdown`
   - Use `response_models.py` for standardized responses
   - Optionally use `color_analysis.py` for color extraction

4. **Create batch scripts**:
   - `SetupServerCondaEnv.bat`: Parse config, detect GPU, create conda env, install dependencies
   - `RunServer.bat`: Activate env and run `server.py`
   - `DiagnosticTest.bat`: Test env and imports
   - `TestService.bat`: Test running service with curl

5. **Test thoroughly**:
   - Run setup script
   - Run diagnostic tests
   - Start server
   - Test with TestService.bat

See existing services (EasyOCR, MangaOCR, DocTR) as reference implementations.

## Technical Details

### Binary Image Transfer

Services accept binary image data directly in the request body, avoiding base64 encoding overhead:

```python
# Client side (C#)
byte[] imageBytes = File.ReadAllBytes(imagePath);
var content = new ByteArrayContent(imageBytes);
content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
var response = await httpClient.PostAsync($"http://localhost:5000/process?lang=japan", content);

# Server side (Python/FastAPI)
image_bytes = await request.body()
image = Image.open(BytesIO(image_bytes))
```

### HTTP Keep-Alive

FastAPI/Uvicorn automatically supports HTTP keep-alive, allowing persistent connections for multiple requests without reconnection overhead.

### GPU Acceleration

All services support GPU acceleration when available:
- Automatically detect CUDA availability
- Auto-detect GPU series and install appropriate PyTorch version:
  - RTX 30/40 series: PyTorch 2.6.0 with CUDA 11.8
  - RTX 50 series: PyTorch nightly with CUDA 12.8
- Move models to GPU if available
- Report backend (gpu/cpu) in responses

### Color Extraction

Services use GPU-accelerated k-means clustering (via CuPy) to extract dominant foreground and background colors from text regions. Falls back to CPU if CuPy is unavailable.

## Troubleshooting

### Conda not found
Run `services\util\InstallMiniConda.bat`, then restart your terminal/application.

### GPU not detected
Ensure NVIDIA drivers are installed and up to date. Run `nvidia-smi` to verify. For RTX 50 series cards, ensure you have the latest NVIDIA drivers that support CUDA 12.8.

### Port already in use
Check `service_config.txt` and ensure each service uses a unique port. Default ports:
- EasyOCR: 5000
- MangaOCR: 5001
- DocTR: 5002

### Model download failures
Ensure you have a stable internet connection. Models are downloaded automatically on first setup.

### Import errors after setup
Run `DiagnosticTest.bat` to verify the environment. If it fails, rerun `SetupServerCondaEnv.bat`.

## Performance Tips

1. **Use GPU**: GPU acceleration provides 5-10x speedup for most OCR operations
2. **Keep connections alive**: Reuse HTTP client connections for multiple requests
3. **Binary transfer**: Always send images as binary data, not base64
4. **Batch processing**: If processing many images, keep the service running instead of starting/stopping
5. **Appropriate service**: Use MangaOCR for Japanese manga, DocTR for English documents, EasyOCR for general use

## C# Application Integration

The UGTLive C# application integrates with these services through:

- **PythonServicesManager**: Discovers services by scanning `app/services/` directories
- **PythonService**: Represents each service, manages lifecycle (start/stop), checks status
- **HTTP Communication**: Uses HttpClient to send binary image data to `/process` endpoint
- **Service Discovery**: Automatically detects services via `service_config.txt` files
- **Auto-Start**: Services can be configured to start automatically on app launch
- **Status Monitoring**: Checks service health via `/health` and `/info` endpoints

### Integration Details

- Services are discovered on app startup via `PythonServicesManager.Instance.DiscoverServices()`
- Each service is represented by a `PythonService` object with properties from `service_config.txt`
- The C# app uses `venv_name` from config (not `conda_env_name`) to check if service is installed
- Services can be started/stopped via the Python Services Manager UI dialog
- Binary image transfer uses `application/octet-stream` content type
- HTTP keep-alive is enabled for better performance

## Migration from Old Webserver

The old monolithic `app/webserver/` system has been replaced by this modular architecture:

**Old**:
- Single conda environment for all OCR engines
- Socket-based communication
- Write images to disk for processing
- One server.py handling all engines

**New**:
- Separate conda environment per service
- HTTP/REST API with FastAPI
- Binary image transfer in memory
- Independent server.py per service
- Service discovery and management via C# PythonServicesManager

