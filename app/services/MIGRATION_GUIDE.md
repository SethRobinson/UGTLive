# Migration Guide: Old Webserver → New Services Architecture

This guide explains how to migrate from the old monolithic `app/webserver/` system to the new modular services architecture in `app/services/`.

## What Changed

### Old Architecture (`app/webserver/`)

```
app/webserver/
├── server.py                    # Monolithic socket server
├── process_image_easyocr.py     # EasyOCR implementation
├── process_image_mangaocr.py    # MangaOCR implementation
├── process_image_doctr.py       # DocTR implementation
├── color_analysis.py            # Shared utilities
├── SetupServerCondaEnv.bat      # Single environment for all
└── _NVidia30And40Series.bat     # GPU-specific setup
```

**Communication**: TCP sockets (custom protocol)
**Image Transfer**: Write to disk, then process
**Environment**: Single conda env `ocrstuff` for all OCR engines

### New Architecture (`app/services/`)

```
app/services/
├── util/
│   └── InstallMiniConda.bat
├── shared/
│   ├── config_parser.py
│   ├── response_models.py
│   ├── color_analysis.py
├── EasyOCR/
│   ├── service_config.txt
│   ├── server.py              # FastAPI server
│   └── [batch scripts]
├── MangaOCR/
│   ├── service_config.txt
│   ├── server.py              # FastAPI server
│   └── [batch scripts]
└── DocTR/
    ├── service_config.txt
    ├── server.py              # FastAPI server
    └── [batch scripts]
```

**Communication**: HTTP/REST with FastAPI
**Image Transfer**: Binary data in HTTP request body
**Environment**: Separate conda env for each service

## Benefits of New Architecture

1. **Isolation**: Each service has its own dependencies, preventing conflicts
2. **Modularity**: Enable/disable services independently
3. **Maintainability**: Update one service without affecting others
4. **Scalability**: Easy to add new services or run on different ports/machines
5. **Modern API**: FastAPI provides automatic documentation, validation, and better performance
6. **Binary Transfer**: Direct binary image transfer is more efficient than base64/JSON
7. **Standards**: HTTP keep-alive, proper error codes, RESTful design

## Migration Steps

### Step 1: Install New Services

For each service you want to use:

```batch
cd app\services\EasyOCR
SetupServerCondaEnv.bat

cd app\services\MangaOCR
SetupServerCondaEnv.bat

cd app\services\DocTR
SetupServerCondaEnv.bat
```

### Step 2: Test Services

```batch
cd app\services\EasyOCR
DiagnosticTest.bat
RunServer.bat
# In another terminal:
TestService.bat
```

Repeat for each service.

### Step 3: Update C# Application Code

#### Old Code (Socket-based)

```csharp
// Old socket communication
using (var client = new TcpClient("127.0.0.1", 9999))
using (var stream = client.GetStream())
{
    // Write image to disk
    File.WriteAllBytes("image_to_process.png", imageBytes);
    
    // Send command
    string command = $"read_image|{lang}|{implementation}";
    byte[] commandBytes = Encoding.UTF8.GetBytes(command);
    stream.Write(commandBytes, 0, commandBytes.Length);
    
    // Read size header
    byte[] sizeBuffer = new byte[1024];
    int bytesRead = stream.Read(sizeBuffer, 0, sizeBuffer.Length);
    string sizeStr = Encoding.UTF8.GetString(sizeBuffer, 0, bytesRead).Trim();
    int responseSize = int.Parse(sizeStr.Split('\r', '\n')[0]);
    
    // Read JSON response
    byte[] responseBuffer = new byte[responseSize];
    // ... complex buffering logic ...
}
```

#### New Code (HTTP-based)

The actual implementation uses `PythonServicesManager` and `PythonService`:

```csharp
// In Logic.cs - ProcessImageWithHttpServiceAsync
private async Task<string?> ProcessImageWithHttpServiceAsync(byte[] imageBytes, string serviceName, string language)
{
    // Get service from manager (discovered on startup)
    var service = PythonServicesManager.Instance.GetServiceByName(serviceName);
    
    if (service == null)
    {
        Console.WriteLine($"Service {serviceName} not found");
        return null;
    }
    
    // Check if service is running
    if (!service.IsRunning)
    {
        bool isRunning = await service.CheckIsRunningAsync();
        if (!isRunning)
        {
            // Show error dialog offering to start service
            bool openManager = ErrorPopupManager.ShowServiceWarning(
                $"The {serviceName} service is not running.\n\nWould you like to open the Python Services Manager to start it?",
                "Service Not Available");
            
            if (openManager)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ServerSetupDialog.ShowDialogSafe(fromSettings: true);
                });
            }
            return null;
        }
    }
    
    // Build query parameters
    string langParam = MapLanguageForService(language);
    string url = $"{service.ServerUrl}:{service.Port}/process?lang={langParam}&char_level=true";
    
    // Add MangaOCR-specific parameters
    if (serviceName == "MangaOCR")
    {
        int minWidth = ConfigManager.Instance.GetMangaOcrMinRegionWidth();
        int minHeight = ConfigManager.Instance.GetMangaOcrMinRegionHeight();
        double overlapPercent = ConfigManager.Instance.GetMangaOcrOverlapAllowedPercent();
        url += $"&min_region_width={minWidth}&min_region_height={minHeight}&overlap_allowed_percent={overlapPercent}";
    }
    
    // Send HTTP request with keep-alive
    var content = new ByteArrayContent(imageBytes);
    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    
    using var request = new HttpRequestMessage(HttpMethod.Post, url);
    request.Content = content;
    request.Headers.ConnectionClose = false;
    var response = await _httpClient.SendAsync(request);
    
    if (!response.IsSuccessStatusCode)
    {
        Console.WriteLine($"HTTP request failed: {response.StatusCode}");
        service.MarkAsNotRunning();
        return null;
    }
    
    // Return JSON response directly
    string jsonResponse = await response.Content.ReadAsStringAsync();
    return jsonResponse;
}

// Response model
public class OCRResponse
{
    public string Status { get; set; }
    public List<TextObject> Texts { get; set; }
    public double ProcessingTime { get; set; }
    public string Language { get; set; }
    public bool CharLevel { get; set; }
    public string Backend { get; set; }
}

public class TextObject
{
    public string Text { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public List<List<int>> Vertices { get; set; }
    public double? Confidence { get; set; }
    public ColorInfo BackgroundColor { get; set; }
    public ColorInfo ForegroundColor { get; set; }
}

public class ColorInfo
{
    public List<int> Rgb { get; set; }
    public string Hex { get; set; }
    public double Percentage { get; set; }
}
```

### Step 4: Update Service Management

#### Old: Start/Stop Single Server

```csharp
// Start server
Process.Start(new ProcessStartInfo
{
    FileName = "cmd.exe",
    Arguments = "/c conda activate ocrstuff && python server.py",
    WorkingDirectory = @"app\webserver"
});

// Stop server (kill process)
```

#### New: Manage Multiple Services

The actual implementation uses `PythonServicesManager` and `PythonService`:

```csharp
// Discover services on startup (in Logic.cs Initialize())
PythonServicesManager.Instance.DiscoverServices();

// Start a service
var service = PythonServicesManager.Instance.GetServiceByName("EasyOCR");
bool started = await service.StartAsync(showWindow: false);

// Stop a service (graceful shutdown)
await service.StopAsync();

// Check if service is running
bool isRunning = await service.CheckIsRunningAsync();

// Check service health
bool isHealthy = await service.CheckHealthAsync();

// Get all services
List<PythonService> allServices = PythonServicesManager.Instance.GetAllServices();

// Start all auto-start services
await PythonServicesManager.Instance.StartAutoStartServicesAsync(showWindow: false);

// Stop all services owned by app
await PythonServicesManager.Instance.StopOwnedServicesAsync();
```

### Step 5: Service Discovery

The app automatically discovers services on startup:

```csharp
// In Logic.cs Initialize() - called on app startup
PythonServicesManager.Instance.DiscoverServices();

// Internally, PythonServicesManager:
// 1. Scans app/services/ directory
// 2. Skips ignored directories: "shared", "util", "localdata"
// 3. Parses service_config.txt from each service directory
// 4. Creates PythonService objects with properties:
//    - ServiceName (from service_name)
//    - Port (from port)
//    - VenvName (from venv_name) - Note: config uses venv_name, not conda_env_name
//    - Description, Version, Author, GithubUrl, etc.
// 5. Loads AutoStart preference from ConfigManager

// Get discovered services
var service = PythonServicesManager.Instance.GetServiceByName("EasyOCR");
List<PythonService> allServices = PythonServicesManager.Instance.GetAllServices();
```

## Parameter Mapping

### EasyOCR

| Old Parameter | New Parameter | Notes |
|--------------|---------------|-------|
| `lang` | `lang` | Same values |
| `char_level` | `char_level` | Same behavior |
| - | - | Returns same JSON structure |

### MangaOCR

| Old Parameter | New Parameter | Notes |
|--------------|---------------|-------|
| `lang` | `lang` | Always 'japan' |
| `char_level` | `char_level` | Same behavior |
| `min_region_width` | `min_region_width` | Same (default: 10) |
| `min_region_height` | `min_region_height` | Same (default: 10) |
| `overlap_allowed_percent` | `overlap_allowed_percent` | Same (default: 50.0) |

### DocTR

| Old Parameter | New Parameter | Notes |
|--------------|---------------|-------|
| `lang` | `lang` | Accepted but not used |
| `char_level` | `char_level` | Same behavior |

## Response Format

The response format is largely the same, with minor improvements:

### Old Response
```json
{
  "texts": [
    {
      "text": "あ",
      "x": 100,
      "y": 200,
      "width": 30,
      "height": 40,
      "vertices": [[100, 200], [130, 200], [130, 240], [100, 240]],
      "confidence": 0.95,
      "background_color": {...},
      "foreground_color": {...}
    }
  ]
}
```

### New Response
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
      "background_color": {...},
      "foreground_color": {...}
    }
  ],
  "processing_time": 0.234,
  "language": "japan",
  "char_level": true,
  "backend": "gpu"
}
```

**Added fields**:
- `status`: "success" or "error"
- `processing_time`: Time taken in seconds
- `language`: Language used
- `char_level`: Whether char-level was used
- `backend`: "gpu" or "cpu"

## Cleanup Old System

Once migration is complete and tested:

1. **Keep for reference**: Keep `app/webserver/` temporarily
2. **Backup**: Make a backup of the old system
3. **Remove**: After confirming everything works, you can remove:
   - `app/webserver/server.py`
   - Old conda environment: `conda env remove -n ocrstuff`
4. **Keep useful files**: You may want to keep test images and models

## Troubleshooting

### Port Conflicts

If ports 5000-5002 are in use:
1. Edit `service_config.txt` in each service
2. Change the `port|` line
3. Update your C# code to use the new ports

### Performance Comparison

Run both systems side-by-side to compare:
- Response times should be similar or better
- First request may be slower (model initialization)
- Subsequent requests should be faster (HTTP keep-alive)

### Error Handling

New services return proper HTTP status codes:
- `200`: Success
- `400`: Bad request (no image data)
- `500`: Server error (OCR failed)

Update your C# code to handle these appropriately.

## Support

For issues or questions:
1. Check service logs (console output)
2. Run `DiagnosticTest.bat` for the problematic service
3. Verify with `TestService.bat`
4. Check the service-specific README in each service folder

## Rollback Plan

If you need to roll back:
1. Stop new services
2. Start old webserver: `cd app\webserver && RunServer.bat`
3. Revert C# code changes

The old system remains intact until you explicitly remove it.

