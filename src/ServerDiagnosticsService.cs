using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UGTLive
{
    public class ServerDiagnosticsService
    {
        private static ServerDiagnosticsService? _instance;
        
        public static ServerDiagnosticsService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ServerDiagnosticsService();
                }
                return _instance;
            }
        }
        
        private ServerDiagnosticsService() { }
        
        /// <summary>
        /// Checks if conda is available in the system PATH
        /// </summary>
        public async Task<(bool available, string version, string errorMessage)> CheckCondaAvailableAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Try to find conda.exe or conda.bat
                    string condaPath = FindCondaExecutable();
                    if (string.IsNullOrEmpty(condaPath))
                    {
                        return (false, "", "Conda is not installed or not in PATH");
                    }
                    
                    // Try to get conda version
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = condaPath,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };
                    
                    using (Process process = Process.Start(psi)!)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        
                        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                        {
                            string version = output.Trim();
                            return (true, version, "");
                        }
                        else
                        {
                            return (true, "Unknown", error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (false, "", $"Error checking conda: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// Finds conda executable in common locations or PATH
        /// </summary>
        private string FindCondaExecutable()
        {
            // First try "conda" command (if in PATH)
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "conda",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                
                using (Process process = Process.Start(psi)!)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        string[] paths = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string path in paths)
                        {
                            string trimmed = path.Trim();
                            if (File.Exists(trimmed))
                            {
                                return trimmed;
                            }
                        }
                    }
                }
            }
            catch { }
            
            // Try common installation locations
            string[] commonPaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "miniconda3", "Scripts", "conda.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "anaconda3", "Scripts", "conda.exe"),
                Path.Combine("C:", "Users", Environment.UserName, "miniconda3", "Scripts", "conda.exe"),
                Path.Combine("C:", "Users", Environment.UserName, "anaconda3", "Scripts", "conda.exe"),
                Path.Combine("C:", "ProgramData", "miniconda3", "Scripts", "conda.exe"),
                Path.Combine("C:", "ProgramData", "anaconda3", "Scripts", "conda.exe")
            };
            
            foreach (string path in commonPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            
            return "";
        }
        
        /// <summary>
        /// Checks if the ocrstuff conda environment exists
        /// </summary>
        public async Task<(bool exists, string pythonVersion, string errorMessage)> CheckCondaEnvironmentAsync()
        {
            return await Task.Run(async () =>
            {
                try
                {
                    var condaCheck = await CheckCondaAvailableAsync();
                    if (!condaCheck.available)
                    {
                        return (false, "", condaCheck.errorMessage);
                    }
                    
                    string condaPath = FindCondaExecutable();
                    if (string.IsNullOrEmpty(condaPath))
                    {
                        return (false, "", "Conda executable not found");
                    }
                    
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = condaPath,
                        Arguments = "env list",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };
                    
                    using (Process process = Process.Start(psi)!)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        
                        if (process.ExitCode != 0)
                        {
                            return (false, "", $"Error listing conda environments: {error}");
                        }
                        
                        // Check if ocrstuff environment exists
                        if (output.Contains("ocrstuff"))
                        {
                            // Try to get Python version from the environment
                            string pythonVersion = await GetPythonVersionFromEnvironmentAsync();
                            return (true, pythonVersion, "");
                        }
                        else
                        {
                            return (false, "", "ocrstuff environment not found");
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (false, "", $"Error checking conda environment: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// Gets Python version from the ocrstuff environment
        /// </summary>
        private async Task<string> GetPythonVersionFromEnvironmentAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    string condaPath = FindCondaExecutable();
                    if (string.IsNullOrEmpty(condaPath))
                    {
                        return "Unknown";
                    }
                    
                    // Create a batch file to activate environment and get Python version
                    string tempBatch = Path.Combine(Path.GetTempPath(), $"check_python_{Guid.NewGuid()}.bat");
                    string pythonCheckScript = $"@echo off\r\ncall conda activate ocrstuff\r\npython --version\r\n";
                    
                    File.WriteAllText(tempBatch, pythonCheckScript);
                    
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = tempBatch,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.UTF8
                        };
                        
                        using (Process process = Process.Start(psi)!)
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit();
                            
                            // Extract version from output like "Python 3.9.18"
                            Match match = Regex.Match(output, @"Python\s+(\d+\.\d+\.\d+)");
                            if (match.Success)
                            {
                                return match.Groups[1].Value;
                            }
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempBatch))
                        {
                            File.Delete(tempBatch);
                        }
                    }
                }
                catch { }
                
                return "Unknown";
            });
        }
        
        /// <summary>
        /// Checks for NVIDIA GPU and returns model name
        /// </summary>
        public async Task<(bool found, string modelName, string errorMessage)> CheckNvidiaGpuAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Use nvidia-smi to detect NVIDIA GPU (most reliable method)
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "nvidia-smi",
                        Arguments = "--query-gpu=name --format=csv,noheader",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    
                    using (Process? process = Process.Start(psi))
                    {
                        if (process != null)
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            string error = process.StandardError.ReadToEnd();
                            process.WaitForExit();
                            
                            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                            {
                                // Parse the GPU name (first line, trim whitespace)
                                string gpuName = output.Split('\n')[0].Trim();
                                
                                // Check if it's an error message
                                if (gpuName.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                                {
                                    return (false, "", "nvidia-smi returned an error");
                                }
                                
                                if (!string.IsNullOrWhiteSpace(gpuName))
                                {
                                    return (true, gpuName, "");
                                }
                            }
                            
                            // If we got here, nvidia-smi ran but didn't return a GPU name
                            if (!string.IsNullOrWhiteSpace(error))
                            {
                                return (false, "", $"nvidia-smi error: {error.Trim()}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // nvidia-smi not found or failed
                    return (false, "", $"No NVIDIA GPU detected (nvidia-smi not available: {ex.Message})");
                }
                
                return (false, "", "No NVIDIA GPU detected");
            });
        }
        
        /// <summary>
        /// Checks Python packages in the ocrstuff environment
        /// </summary>
        public async Task<Dictionary<string, (bool installed, string version)>> CheckPythonPackagesAsync()
        {
            return await Task.Run(async () =>
            {
                Dictionary<string, (bool installed, string version)> results = new Dictionary<string, (bool installed, string version)>();
                
                string[] packagesToCheck = {
                    "torch", "easyocr", "manga-ocr", "python-doctr", "ultralytics", 
                    "opencv-python", "cupy", "cupy-cuda11x", "cupy-cuda12x"
                };
                
                try
                {
                    string condaPath = FindCondaExecutable();
                    if (string.IsNullOrEmpty(condaPath))
                    {
                        // Mark all as not installed if conda not found
                        foreach (string pkg in packagesToCheck)
                        {
                            results[pkg] = (false, "");
                        }
                        return results;
                    }
                    
                    // Create a batch file to check packages
                    string tempBatch = Path.Combine(Path.GetTempPath(), $"check_packages_{Guid.NewGuid()}.bat");
                    StringBuilder batchContent = new StringBuilder();
                    batchContent.AppendLine("@echo off");
                    batchContent.AppendLine("call conda activate ocrstuff");
                    
                    foreach (string pkg in packagesToCheck)
                    {
                        // Use pip show to get package info
                        batchContent.AppendLine($"python -m pip show {pkg} 2>nul");
                    }
                    
                    File.WriteAllText(tempBatch, batchContent.ToString());
                    
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = tempBatch,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            StandardOutputEncoding = Encoding.UTF8
                        };
                        
                        using (Process process = Process.Start(psi)!)
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit();
                            
                            // Parse output - pip show outputs package info separated by blank lines
                            string[] sections = output.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                            
                            foreach (string pkg in packagesToCheck)
                            {
                                bool found = false;
                                string version = "";
                                
                                foreach (string section in sections)
                                {
                                    if (section.Contains($"Name: {pkg}", StringComparison.OrdinalIgnoreCase) ||
                                        section.Contains($"Name: {pkg.Replace("-", "_")}", StringComparison.OrdinalIgnoreCase))
                                    {
                                        found = true;
                                        Match versionMatch = Regex.Match(section, @"Version:\s+([^\r\n]+)");
                                        if (versionMatch.Success)
                                        {
                                            version = versionMatch.Groups[1].Value.Trim();
                                        }
                                        break;
                                    }
                                }
                                
                                results[pkg] = (found, version);
                            }
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempBatch))
                        {
                            File.Delete(tempBatch);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error checking Python packages: {ex.Message}");
                    // Mark all as unknown on error
                    foreach (string pkg in packagesToCheck)
                    {
                        if (!results.ContainsKey(pkg))
                        {
                            results[pkg] = (false, "");
                        }
                    }
                }
                
                return results;
            });
        }
        
        /// <summary>
        /// Checks if the server is running on the expected port
        /// Legacy method - no longer used (legacy server on port 9999 removed, services are managed by PythonServicesManager)
        /// </summary>
        public async Task<(bool running, string errorMessage)> CheckServerRunningAsync()
        {
            // Legacy server on port 9999 no longer exists
            return await Task.FromResult((false, "Legacy server no longer exists - use PythonServicesManager to check service status"));
        }
        
        /// <summary>
        /// Runs comprehensive diagnostics using the batch file
        /// </summary>
        public async Task<Dictionary<string, string>> RunDiagnosticsCheckAsync()
        {
            return await Task.Run(() =>
            {
                Dictionary<string, string> results = new Dictionary<string, string>();
                
                try
                {
                    string webserverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webserver");
                    string diagnosticsBatch = Path.Combine(webserverPath, "RunServerDiagnosticsCheck.bat");
                    
                    if (!File.Exists(diagnosticsBatch))
                    {
                        results["error"] = "Diagnostics batch file not found";
                        return results;
                    }
                    
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = diagnosticsBatch,
                        WorkingDirectory = webserverPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };
                    
                    using (Process process = Process.Start(psi)!)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        
                        // Parse output - assuming structured format
                        results["output"] = output;
                        results["error"] = error;
                        results["exitCode"] = process.ExitCode.ToString();
                    }
                }
                catch (Exception ex)
                {
                    results["error"] = $"Error running diagnostics: {ex.Message}";
                }
                
                return results;
            });
        }
    }
}

