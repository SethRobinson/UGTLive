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
├── manga_yolo_detector.py       # Shared utilities
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
│   └── manga_yolo_detector.py
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

```csharp
// New HTTP communication
private static readonly HttpClient _httpClient = new HttpClient();

public async Task<OCRResponse> ProcessImageAsync(byte[] imageBytes, string lang, string service)
{
    // Determine service port
    int port = service switch
    {
        "easyocr" => 5000,
        "mangaocr" => 5001,
        "doctr" => 5002,
        _ => 5000
    };
    
    // Create request
    var content = new ByteArrayContent(imageBytes);
    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    
    // Send request
    var url = $"http://127.0.0.1:{port}/process?lang={lang}&char_level=true";
    var response = await _httpClient.PostAsync(url, content);
    
    // Parse response
    var jsonString = await response.Content.ReadAsStringAsync();
    var result = JsonSerializer.Deserialize<OCRResponse>(jsonString);
    
    return result;
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

```csharp
public class ServiceManager
{
    private Dictionary<string, Process> _runningServices = new Dictionary<string, Process>();
    
    public void StartService(string serviceName)
    {
        string serviceDir = Path.Combine("app", "services", serviceName);
        string batchFile = Path.Combine(serviceDir, "RunServer.bat");
        
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = batchFile,
            WorkingDirectory = serviceDir,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        
        _runningServices[serviceName] = process;
    }
    
    public async Task StopServiceAsync(string serviceName, int port)
    {
        // Send shutdown request
        using var client = new HttpClient();
        await client.PostAsync($"http://127.0.0.1:{port}/shutdown", null);
        
        // Wait for process to exit
        if (_runningServices.TryGetValue(serviceName, out var process))
        {
            process.WaitForExit(5000);
            _runningServices.Remove(serviceName);
        }
    }
    
    public async Task<bool> IsServiceHealthyAsync(int port)
    {
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(2);
            var response = await client.GetAsync($"http://127.0.0.1:{port}/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
```

### Step 5: Service Discovery

The app can dynamically discover available services:

```csharp
public class ServiceDiscovery
{
    public List<ServiceInfo> DiscoverServices()
    {
        var services = new List<ServiceInfo>();
        var servicesDir = Path.Combine("app", "services");
        
        foreach (var dir in Directory.GetDirectories(servicesDir))
        {
            var configFile = Path.Combine(dir, "service_config.txt");
            
            // Skip special directories
            if (Path.GetFileName(dir) == "util" || 
                Path.GetFileName(dir) == "shared" ||
                !File.Exists(configFile))
            {
                continue;
            }
            
            var config = ParseServiceConfig(configFile);
            services.Add(config);
        }
        
        return services;
    }
    
    private ServiceInfo ParseServiceConfig(string configPath)
    {
        var config = new ServiceInfo();
        
        foreach (var line in File.ReadAllLines(configPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;
                
            var parts = line.Split('|');
            if (parts.Length < 2)
                continue;
                
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            
            switch (key)
            {
                case "service_name":
                    config.ServiceName = value;
                    break;
                case "port":
                    config.Port = int.Parse(value);
                    break;
                case "description":
                    config.Description = value;
                    break;
                // ... etc
            }
        }
        
        return config;
    }
}
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

