using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace UGTLive
{
    public class PythonService
    {
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        
        // Configuration properties
        public string ServiceName { get; private set; } = "";
        public string Description { get; private set; } = "";
        public int Port { get; private set; }
        public string CondaEnvName { get; private set; } = "";
        public string Version { get; private set; } = "";
        public string Author { get; private set; } = "";
        public string GithubUrl { get; private set; } = "";
        public bool LocalOnly { get; private set; } = true;
        public string ServiceDirectory { get; private set; } = "";
        
        // State properties
        public bool IsRunning { get; private set; }
        public bool IsInstalled { get; private set; }
        public bool AutoStart { get; set; }
        
        // Caching flags to avoid slow repeated checks
        private bool _hasCheckedCondaEnv = false;
        
        // Process management
        private Process? _process;
        private bool _ownedByApp;
        
        public bool IsOwnedByApp => _ownedByApp;
        
        /// <summary>
        /// Creates a PythonService instance from a service_config.txt file
        /// </summary>
        public static PythonService? ParseFromConfig(string serviceDirectory)
        {
            string configPath = Path.Combine(serviceDirectory, "service_config.txt");
            
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"No service_config.txt found in {serviceDirectory}");
                return null;
            }
            
            var config = ServiceConfigParser.ParseConfig(configPath);
            
            if (config.Count == 0)
            {
                Console.WriteLine($"Empty or invalid config in {serviceDirectory}");
                return null;
            }
            
            var service = new PythonService
            {
                ServiceDirectory = serviceDirectory
            };
            
            // Parse required fields
            if (config.TryGetValue("service_name", out var serviceName))
            {
                service.ServiceName = serviceName;
            }
            else
            {
                Console.WriteLine($"Missing service_name in {configPath}");
                return null;
            }
            
            if (config.TryGetValue("port", out var portStr) && int.TryParse(portStr, out int port))
            {
                service.Port = port;
            }
            else
            {
                Console.WriteLine($"Missing or invalid port in {configPath}");
                return null;
            }
            
            if (config.TryGetValue("conda_env_name", out var envName))
            {
                service.CondaEnvName = envName;
            }
            else
            {
                Console.WriteLine($"Missing conda_env_name in {configPath}");
                return null;
            }
            
            // Parse optional fields
            if (config.TryGetValue("description", out var desc))
            {
                service.Description = desc;
            }
            
            if (config.TryGetValue("version", out var ver))
            {
                service.Version = ver;
            }
            
            if (config.TryGetValue("author", out var auth))
            {
                service.Author = auth;
            }
            
            if (config.TryGetValue("github_url", out var url))
            {
                service.GithubUrl = url;
            }
            
            if (config.TryGetValue("local_only", out var localStr))
            {
                service.LocalOnly = localStr.ToLower() == "true";
            }
            
            return service;
        }
        
        /// <summary>
        /// Checks if the service is currently running by hitting the /info endpoint
        /// </summary>
        public async Task<bool> CheckIsRunningAsync()
        {
            if (IsRunning) return true;

            try
            {
                string url = $"http://localhost:{Port}/info";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.ConnectionClose = false;
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    IsRunning = true;
                    IsInstalled = true;  //well, if it's running it must be installed, right?
                    return true;
                }
            }
            catch (Exception)
            {
                // Service not responding
            }
            
            IsRunning = false;
            return false;
        }
        
        /// <summary>
        /// Marks the service as not running (call this when a request fails)
        /// </summary>
        public void MarkAsNotRunning()
        {
            IsRunning = false;
        }
        
        /// <summary>
        /// Checks if the service is installed by checking if conda environment exists
        /// Uses caching to avoid slow repeated conda env checks
        /// </summary>
        public async Task<bool> CheckIsInstalledAsync()
        {
            // If service is already marked as running, it's definitely installed - skip all checks
            if (IsRunning)
            {
                IsInstalled = true;
                _hasCheckedCondaEnv = true;
                return true;
            }

            // If we've already checked the conda env and found it installed, trust that cache
            if (_hasCheckedCondaEnv && IsInstalled)
            {
                return true;
            }

            // Check if service is running now - if so, it's definitely installed
            if (await CheckIsRunningAsync())
            {
                IsInstalled = true;
                _hasCheckedCondaEnv = true;
                return true;
            }
            
            
            // Only do the slow conda env check if we haven't checked before
            if (!_hasCheckedCondaEnv)
            {
                Console.WriteLine($"Checking conda environment for {ServiceName} (this will be cached)...");
                return await Task.Run(() =>
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = "conda",
                            Arguments = $"env list",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        
                        using (Process process = Process.Start(psi)!)
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit();
                            
                            if (process.ExitCode == 0)
                            {
                                // Check if our environment name appears in the output
                                IsInstalled = output.Contains(CondaEnvName);
                                _hasCheckedCondaEnv = true;
                                return IsInstalled;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error checking if {ServiceName} is installed: {ex.Message}");
                    }
                    
                    IsInstalled = false;
                    _hasCheckedCondaEnv = true;
                    return false;
                });
            }
            
            return IsInstalled;
        }
        
        /// <summary>
        /// Marks that conda env check should be re-done (call after uninstall)
        /// </summary>
        public void InvalidateCondaEnvCache()
        {
            _hasCheckedCondaEnv = false;
            IsInstalled = false;
        }
        
        /// <summary>
        /// Starts the service using RunServer.bat
        /// </summary>
        public async Task<bool> StartAsync(bool showWindow)
        {
            /*
            // First check if service is already running
            if (await CheckIsRunningAsync())
            {
                Console.WriteLine($"{ServiceName} is already running on port {Port}");
                // Mark as not owned by app since it was started externally
                _ownedByApp = false;
                return true;
            }
            */
            
            // Start the process
            bool processStarted = await Task.Run(() =>
            {
                try
                {
                    string batchFile = Path.Combine(ServiceDirectory, "RunServer.bat");
                    
                    if (!File.Exists(batchFile))
                    {
                        Console.WriteLine($"RunServer.bat not found for {ServiceName}");
                        return false;
                    }
                    
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = batchFile,
                        WorkingDirectory = ServiceDirectory,
                        UseShellExecute = showWindow,
                        CreateNoWindow = !showWindow
                    };
                    
                    _process = Process.Start(psi);
                    _ownedByApp = true;
                    
                    if (_process != null)
                    {
                        Console.WriteLine($"Started {ServiceName} service (PID: {_process.Id})");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error starting {ServiceName}: {ex.Message}");
                }
                
                return false;
            });
            
            if (!processStarted)
            {
                return false;
            }

            //let's just assume it's ready now
            IsRunning = true;
            IsInstalled = true;

            return true;
            //return await WaitForServiceReadyAsync(timeoutSeconds: 30);
        }
        
        /// <summary>
        /// Polls the service /info endpoint until it responds or timeout is reached
        /// </summary>
        private async Task<bool> WaitForServiceReadyAsync(int timeoutSeconds)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            
            while (DateTime.Now - startTime < timeout)
            {
                if (await CheckIsRunningAsync())
                {
                    Console.WriteLine($"{ServiceName} is ready!");
                    return true;
                }
                
                // Wait 500ms before next check
                await Task.Delay(500);
            }
            
            Console.WriteLine($"{ServiceName} did not respond within {timeoutSeconds} seconds");
            return false;
        }
        
        /// <summary>
        /// Stops the service gracefully using /shutdown endpoint
        /// </summary>
        public async Task<bool> StopAsync()
        {
            try
            {
                // Try graceful shutdown via API
                string url = $"http://localhost:{Port}/shutdown";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.ConnectionClose = false;
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Sent shutdown signal to {ServiceName}");
                    
                    // Wait for process to exit if we own it
                    if (_process != null && !_process.HasExited)
                    {
                        _process.WaitForExit(5000);
                    }
                    
                    _process = null;
                    _ownedByApp = false;
                    IsRunning = false;
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping {ServiceName} gracefully: {ex.Message}");
            }
            
            // If graceful shutdown failed and we own the process, kill it
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill();
                    _process = null;
                    _ownedByApp = false;
                    IsRunning = false;
                    Console.WriteLine($"Forcefully terminated {ServiceName}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error killing {ServiceName} process: {ex.Message}");
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets service info from /info endpoint
        /// </summary>
        public async Task<string> GetServiceInfoAsync()
        {
            try
            {
                string url = $"http://localhost:{Port}/info";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.ConnectionClose = false;
                var response = await _httpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting info for {ServiceName}: {ex.Message}");
            }
            
            return "";
        }
        
        /// <summary>
        /// Checks service health using /health endpoint
        /// </summary>
        public async Task<bool> CheckHealthAsync()
        {
            try
            {
                string url = $"http://localhost:{Port}/health";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.ConnectionClose = false;
                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

