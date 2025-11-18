# Service Uninstallation Guide

## Overview

Each OCR service now has a dedicated `RemoveServerCondaEnv.bat` script that allows you to uninstall the conda environment for that service. This is useful for:
- Freeing up disk space by removing unused services
- Troubleshooting by cleanly removing and reinstalling a service
- Managing multiple services and only keeping the ones you need

## Uninstalling a Service

### Method 1: Manual Uninstall (Interactive)

Simply double-click or run the removal script for the service you want to uninstall:

```batch
# EasyOCR
cd app\services\EasyOCR
RemoveServerCondaEnv.bat

# MangaOCR
cd app\services\MangaOCR
RemoveServerCondaEnv.bat

# DocTR
cd app\services\DocTR
RemoveServerCondaEnv.bat
```

The script will:
1. Read the service configuration from `service_config.txt`
2. Display the service name and environment name
3. Check if the conda environment exists
4. Remove the environment if found
5. Pause so you can see the results

### Method 2: Automated Uninstall (Non-Interactive)

For automated scripts or when called from other batch files, use the `nopause` parameter:

```batch
RemoveServerCondaEnv.bat nopause
```

This is used internally by `SetupServerCondaEnv.bat` to remove old environments before creating new ones.

## What Gets Removed

The removal script will:
- ✅ Remove the conda environment (e.g., `ugt_easyocr`, `ugt_mangaocr`, `ugt_doctr`)
- ✅ Free up disk space used by Python packages and dependencies
- ✅ Remove any cached models specific to that environment

The removal script will NOT remove:
- ❌ The service source files (`server.py`, batch scripts, etc.)
- ❌ Shared models in `app/services/localdata/models/` (used by multiple services)
- ❌ Service configuration files (`service_config.txt`)
- ❌ Setup logs (`setup_conda_log.txt`)

## Reinstalling a Service

After uninstalling, you can reinstall the service at any time:

```batch
cd app\services\EasyOCR
SetupServerCondaEnv.bat
```

The setup script will:
1. Automatically call `RemoveServerCondaEnv.bat nopause` to ensure a clean environment
2. Create a fresh conda environment
3. Install all dependencies
4. Download required models

## Examples

### Uninstall EasyOCR Service

```batch
D:\projects\CSharp\UGTLive>cd app\services\EasyOCR

D:\projects\CSharp\UGTLive\app\services\EasyOCR>RemoveServerCondaEnv.bat
=============================================================
  EasyOCR - Remove Conda Environment
  Environment: ugt_easyocr
=============================================================

Checking for environment "ugt_easyocr"...
Found environment "ugt_easyocr". Removing it...

=============================================================
  SUCCESS! Environment "ugt_easyocr" removed successfully.
=============================================================

Press any key to exit...
```

### Uninstall All Services

To remove all OCR services at once:

```batch
cd app\services\EasyOCR
call RemoveServerCondaEnv.bat nopause

cd ..\MangaOCR
call RemoveServerCondaEnv.bat nopause

cd ..\DocTR
call RemoveServerCondaEnv.bat nopause

echo All services uninstalled!
```

## Troubleshooting

### Error: Failed to remove environment

**Symptom**: Script fails with "Failed to remove environment" error

**Solution**: 
1. Close all terminals/command prompts that have the environment activated
2. Close any Python processes running from that environment
3. Try running the script again

### Error: Conda is not installed or not in PATH

**Symptom**: Script cannot find conda

**Solution**:
1. Ensure Miniconda or Anaconda is installed
2. Restart your terminal to refresh the PATH
3. Verify conda is accessible: `conda --version`

### Environment not found

**Symptom**: Script says "Environment not found. Nothing to remove."

**Cause**: The environment was already removed or was never created

**Action**: No action needed - the environment is already gone

## Advanced Usage

### Check Which Environments Exist

To see all conda environments:

```batch
conda env list
```

Look for environments starting with `ugt_`:
- `ugt_easyocr` - EasyOCR service
- `ugt_mangaocr` - MangaOCR service  
- `ugt_doctr` - DocTR service

### Manual Removal (Without Script)

If the script doesn't work, you can manually remove environments:

```batch
conda env remove -n ugt_easyocr -y
conda env remove -n ugt_mangaocr -y
conda env remove -n ugt_doctr -y
```

## Disk Space Considerations

Typical conda environment sizes:
- **EasyOCR**: ~3-4 GB (includes PyTorch, EasyOCR models)
- **MangaOCR**: ~3-4 GB (includes PyTorch, MangaOCR, YOLO models)
- **DocTR**: ~3-4 GB (includes PyTorch, DocTR models)

Removing unused services can free up significant disk space!

## Integration with UGTLive App

The UGTLive C# application can call `RemoveServerCondaEnv.bat nopause` programmatically to allow users to uninstall services from within the app:

```csharp
public void UninstallService(string serviceName)
{
    string serviceDir = Path.Combine("app", "services", serviceName);
    string batchFile = Path.Combine(serviceDir, "RemoveServerCondaEnv.bat");
    
    var process = Process.Start(new ProcessStartInfo
    {
        FileName = batchFile,
        Arguments = "nopause",
        WorkingDirectory = serviceDir,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        CreateNoWindow = false
    });
    
    process.WaitForExit();
    
    if (process.ExitCode == 0)
    {
        MessageBox.Show($"{serviceName} uninstalled successfully!");
    }
    else
    {
        MessageBox.Show($"Failed to uninstall {serviceName}");
    }
}
```

## See Also

- `README.md` - Main services documentation
- `QUICK_START.md` - Getting started guide
- `SetupServerCondaEnv.bat` - Service installation script
- `service_config.txt` - Service configuration (contains environment name)

