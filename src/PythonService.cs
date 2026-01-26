using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Management;

namespace UGTLive
{
    public class PythonService
    {
        // Windows API for showing/hiding console windows
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        
        // Windows API for process priority management
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);
        
        [DllImport("kernel32.dll")]
        private static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);
        
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;
        
        // Process access rights
        private const uint PROCESS_SET_INFORMATION = 0x0200;
        
        // Priority classes
        private const uint HIGH_PRIORITY_CLASS = 0x00000080;
        private const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
        
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        
        // Configuration properties
        public string ServiceName { get; private set; } = "";
        public string Description { get; private set; } = "";
        public int Port { get; private set; }
        public string VenvName { get; private set; } = "";
        public string Version { get; private set; } = "";
        public string Author { get; private set; } = "";
        public string GithubUrl { get; private set; } = "";
        public string ServerUrl { get; private set; } = "http://127.0.0.1";
        public bool LocalOnly { get; private set; } = true;
        public string ServiceDirectory { get; private set; } = "";
        
        // State properties
        public bool IsRunning { get; private set; }
        public bool IsInstalled { get; private set; }
        public bool AutoStart { get; set; }
        
        // Caching flags to avoid slow repeated checks
        private bool _hasCheckedVenv = false;
        
        // Process management
        private Process? _process;
        private bool _ownedByApp;
        private IntPtr _consoleWindowHandle = IntPtr.Zero;
        private Task<bool>? _startingTask;
        
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
            
            if (config.TryGetValue("venv_name", out var envName))
            {
                service.VenvName = envName;
            }
            else
            {
                Console.WriteLine($"Missing venv_name in {configPath}");
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
            
            if (config.TryGetValue("server_url", out var serverUrl))
            {
                service.ServerUrl = serverUrl;
            }
            
            return service;
        }
        
        /// <summary>
        /// Checks if the service is currently running by hitting the /info endpoint
        /// </summary>
        public async Task<bool> CheckIsRunningAsync(bool forceCheck = false)
        {
            if (!forceCheck && IsRunning) return true;

            try
            {
                string url = $"{ServerUrl}:{Port}/info";
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
        /// Checks if the service is installed by checking if venv folder exists
        /// Uses caching to avoid slow repeated filesystem checks
        /// </summary>
        public async Task<bool> CheckIsInstalledAsync()
        {
            // If service is already marked as running, it's definitely installed - skip all checks
            if (IsRunning)
            {
                IsInstalled = true;
                _hasCheckedVenv = true;
                return true;
            }

            // If we've already checked the venv and found it installed, trust that cache
            if (_hasCheckedVenv && IsInstalled)
            {
                return true;
            }

            // Check if service is running now - if so, it's definitely installed
            if (await CheckIsRunningAsync())
            {
                IsInstalled = true;
                _hasCheckedVenv = true;
                return true;
            }
            
            
            // Only do the venv folder check if we haven't checked before
            if (!_hasCheckedVenv)
            {
                Console.WriteLine($"Checking virtual environment for {ServiceName} (this will be cached)...");
                return await Task.Run(() =>
                {
                    try
                    {
                        string venvPath = Path.Combine(ServiceDirectory, "venv");
                        string activatePath = Path.Combine(venvPath, "Scripts", "activate.bat");
                        
                        // Check if venv folder and activate script exist
                        IsInstalled = Directory.Exists(venvPath) && File.Exists(activatePath);
                        _hasCheckedVenv = true;
                        
                        if (IsInstalled)
                        {
                            Console.WriteLine($"Virtual environment found for {ServiceName}");
                        }
                        else
                        {
                            Console.WriteLine($"Virtual environment not found for {ServiceName}");
                        }
                        
                        return IsInstalled;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error checking if {ServiceName} is installed: {ex.Message}");
                    }
                    
                    IsInstalled = false;
                    _hasCheckedVenv = true;
                    return false;
                });
            }
            
            return IsInstalled;
        }
        
        /// <summary>
        /// Marks that venv check should be re-done (call after uninstall)
        /// </summary>
        public void InvalidateVenvCache()
        {
            _hasCheckedVenv = false;
            IsInstalled = false;
        }
        
        /// <summary>
        /// Starts the service using RunServer.bat
        /// </summary>
        public async Task<bool> StartAsync(bool showWindow)
        {
            // If a start operation is already in progress, wait for it and return its result
            if (_startingTask != null && !_startingTask.IsCompleted)
            {
                Console.WriteLine($"StartAsync called for {ServiceName} but start is already in progress. Waiting for existing task...");
                return await _startingTask;
            }

            // Start a new task
            _startingTask = StartAsyncInternal(showWindow);
            return await _startingTask;
        }

        private async Task<bool> StartAsyncInternal(bool showWindow)
        {
            // First check if service is already running
            // This handles cases where the service was left running from a previous session
            if (await CheckIsRunningAsync())
            {
                Console.WriteLine($"{ServiceName} is already running on port {Port}");
                // Mark as not owned by app since it was started externally/previously
                _ownedByApp = false;
                return true;
            }
            
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
                    
                    // Generate a unique title for the window to help us find it later
                    // This is crucial for reliable window finding and hiding
                    string uniqueTitle = $"UGTLive_Service_{ServiceName}_{Port}";
                    
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        // Use /c to run the command and terminate, but the command runs the batch file
                        // We set the title first so we can find the window later
                        Arguments = $"/c title {uniqueTitle} & \"{batchFile}\" nopause",
                        WorkingDirectory = ServiceDirectory,
                        UseShellExecute = true, // Always use shell execute to get a window we can control
                        WindowStyle = showWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
                        CreateNoWindow = false // Ignored when UseShellExecute is true
                    };
                    
                    _process = Process.Start(psi);
                    
                    if (_process != null)
                    {
                        // Mark as owned BEFORE checking process status
                        _ownedByApp = true;
                        
                        Console.WriteLine($"Started {ServiceName} service (PID: {_process.Id}) - IsOwnedByApp=TRUE");
                        
                        // Check if process has already exited (happens with UseShellExecute sometimes)
                        try
                        {
                            // Give it a moment to fail if it's going to
                            if (_process.WaitForExit(1000))
                            {
                                if (_process.ExitCode != 0)
                                {
                                    Console.WriteLine($"ERROR: {ServiceName} failed to start (Exit Code: {_process.ExitCode})");
                                    _ownedByApp = false;
                                    return false;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Cannot check if process exited: {ex.Message}");
                        }
                        
                        // Try to find and store the console window handle
                        // Wait a bit for the window to appear
                        if (showWindow)
                        {
                            System.Threading.Thread.Sleep(500);
                        }
                        else
                        {
                            // If hidden, we still need to wait a bit for the window to be created
                            System.Threading.Thread.Sleep(200);
                        }
                        
                        // Try to find by unique title first (most reliable)
                        _consoleWindowHandle = FindConsoleWindowByTitle();
                        
                        // Fallback to process ID if title search failed
                        if (_consoleWindowHandle == IntPtr.Zero)
                        {
                            _consoleWindowHandle = FindConsoleWindowForProcess(_process.Id);
                        }
                        
                        if (_consoleWindowHandle != IntPtr.Zero)
                        {
                            Console.WriteLine($"Found console window for {ServiceName}");
                        }
                        else
                        {
                            Console.WriteLine($"Could not find console window for {ServiceName} yet (will retry later if needed)");
                        }
                        
                        // Set high priority for the cmd.exe process to prevent throttling when minimized
                        SetProcessPriority(_process.Id, HIGH_PRIORITY_CLASS);
                        
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to get process handle for {ServiceName} - IsOwnedByApp will be FALSE");
                        _ownedByApp = false;
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

            // Wait for the service to actually become ready
            // This ensures we detect startup failures (like port conflicts)
            bool isReady = await WaitForServiceReadyAsync(timeoutSeconds: 30);
            
            if (isReady)
            {
                IsRunning = true;
                IsInstalled = true;
                
                // Set priority for child Python processes (they spawn after the batch file runs)
                // Wait a bit for Python processes to start, then set their priority
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000); // Wait 2 seconds for Python processes to start
                    SetPriorityForChildPythonProcesses();
                });
                
                return true;
            }
            else
            {
                Console.WriteLine($"Service {ServiceName} failed to start properly");
                // Cleanup if process is still running but not responding (zombie)
                if (_process != null && !_process.HasExited)
                {
                    try { _process.Kill(); } catch { }
                }
                
                // Reset state
                _process = null;
                _ownedByApp = false;
                IsRunning = false;
                return false;
            }
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
                // Check if process died while we were waiting
                // This catches immediate failures like port conflicts
                if (_process != null && _process.HasExited)
                {
                     Console.WriteLine($"Service {ServiceName} process exited unexpectedly with code {_process.ExitCode}");
                     return false;
                }

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
        /// Stops the service by sending /shutdown endpoint and waiting for confirmation
        /// </summary>
        public async Task<bool> StopAsync()
        {
            try
            {
                // Send shutdown signal
                string url = $"{ServerUrl}:{Port}/shutdown";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.ConnectionClose = false;
                
                // Use a short timeout for the shutdown request itself
                // The server might die before responding fully
                var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
                try 
                {
                    var response = await _httpClient.SendAsync(request, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Sent shutdown signal to {ServiceName}");
                    }
                }
                catch (Exception) { /* Ignore timeout/error during shutdown request */ }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping {ServiceName}: {ex.Message}");
            }
            
            // Mark as not owned anymore
            _process = null;
            _ownedByApp = false;
            
            // Force IsRunning to false so the verification check actually hits the network
            IsRunning = false;
            
            // Wait for it to actually stop
            return await WaitForServiceShutdownAsync(10);
        }
        
        private async Task<bool> WaitForServiceShutdownAsync(int timeoutSeconds)
        {
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            
            while (DateTime.Now - startTime < timeout)
            {
                // Reset flag to force network check
                IsRunning = false;
                
                // Check if service is still responding
                bool isRunning = await CheckIsRunningAsync();
                
                if (!isRunning)
                {
                    Console.WriteLine($"Service {ServiceName} has stopped.");
                    return true;
                }
                
                Console.WriteLine($"Service {ServiceName} is still stopping...");
                await Task.Delay(500);
            }
            
            Console.WriteLine($"Service {ServiceName} failed to stop within {timeoutSeconds} seconds");
            return false;
        }
        
        /// <summary>
        /// Gets service info from /info endpoint
        /// </summary>
        public async Task<string> GetServiceInfoAsync()
        {
            try
            {
                string url = $"{ServerUrl}:{Port}/info";
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
        /// Finds the console window handle for a given process ID
        /// </summary>
        private IntPtr FindConsoleWindowForProcess(int processId)
        {
            IntPtr foundWindow = IntPtr.Zero;
            
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                
                if (windowProcessId == processId)
                {
                    // Get window title to verify it's a console window
                    var title = new System.Text.StringBuilder(256);
                    GetWindowText(hWnd, title, 256);
                    
                    // Console windows typically have titles
                    if (title.Length > 0)
                    {
                        foundWindow = hWnd;
                        return false; // Stop enumeration
                    }
                }
                
                return true; // Continue enumeration
            }, IntPtr.Zero);
            
            return foundWindow;
        }
        
        /// <summary>
        /// Finds console window by searching for service name or port in window title
        /// This is more reliable when UseShellExecute=true since we might not have the right process ID
        /// </summary>
        private IntPtr FindConsoleWindowByTitle()
        {
            IntPtr foundWindow = IntPtr.Zero;
            string uniqueTitle = $"UGTLive_Service_{ServiceName}_{Port}";
            
            EnumWindows((hWnd, lParam) =>
            {
                // First check if this is a console window by class name
                // We check for standard ConsoleWindowClass and Windows Terminal's CASCADIA_HOSTING_WINDOW_CLASS
                var className = new System.Text.StringBuilder(256);
                GetClassName(hWnd, className, 256);
                string classNameStr = className.ToString();
                
                bool isConsole = classNameStr.Equals("ConsoleWindowClass", StringComparison.OrdinalIgnoreCase) ||
                                 classNameStr.Equals("CASCADIA_HOSTING_WINDOW_CLASS", StringComparison.OrdinalIgnoreCase);
                
                if (!isConsole)
                {
                    return true; // Not a console window, continue enumeration
                }
                
                var title = new System.Text.StringBuilder(256);
                GetWindowText(hWnd, title, 256);
                
                if (title.Length > 0)
                {
                    string titleStr = title.ToString();
                    
                    // Check for our unique title
                    if (titleStr.Contains(uniqueTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"Found console window by unique title for {ServiceName}: '{titleStr}'");
                        foundWindow = hWnd;
                        return false; // Stop enumeration
                    }
                }
                
                return true; // Continue enumeration
            }, IntPtr.Zero);
            
            return foundWindow;
        }
        
        /// <summary>
        /// Sets the priority class for a process by process ID
        /// </summary>
        private void SetProcessPriority(int processId, uint priorityClass)
        {
            try
            {
                IntPtr hProcess = OpenProcess(PROCESS_SET_INFORMATION, false, processId);
                if (hProcess != IntPtr.Zero)
                {
                    bool success = SetPriorityClass(hProcess, priorityClass);
                    CloseHandle(hProcess);
                    
                    if (success)
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            string priorityName = priorityClass == HIGH_PRIORITY_CLASS ? "High" : 
                                                 priorityClass == ABOVE_NORMAL_PRIORITY_CLASS ? "Above Normal" : "Unknown";
                            Console.WriteLine($"Set process priority to {priorityName} for PID {processId} ({ServiceName})");
                        }
                    }
                    else
                    {
                        if (ConfigManager.Instance.GetLogExtraDebugStuff())
                        {
                            Console.WriteLine($"Failed to set process priority for PID {processId} ({ServiceName})");
                        }
                    }
                }
                else
                {
                    if (ConfigManager.Instance.GetLogExtraDebugStuff())
                    {
                        Console.WriteLine($"Could not open process handle for PID {processId} ({ServiceName}) - may require admin rights");
                    }
                }
            }
            catch (Exception ex)
            {
                if (ConfigManager.Instance.GetLogExtraDebugStuff())
                {
                    Console.WriteLine($"Error setting process priority for PID {processId} ({ServiceName}): {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Finds and sets priority for child Python processes spawned by this service
        /// Uses WMI to find processes with our cmd.exe as parent
        /// </summary>
        private void SetPriorityForChildPythonProcesses()
        {
            if (_process == null || _process.HasExited)
            {
                return;
            }
            
            try
            {
                int parentProcessId = _process.Id;
                int childCount = 0;
                
                // Use WMI to find child processes
                string query = $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parentProcessId}";
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            int childProcessId = Convert.ToInt32(obj["ProcessId"]);
                            Process? childProcess = null;
                            
                            try
                            {
                                childProcess = Process.GetProcessById(childProcessId);
                            }
                            catch
                            {
                                // Process may have exited, skip
                                continue;
                            }
                            
                            // Check if it's a Python process
                            string processName = childProcess.ProcessName.ToLower();
                            if (processName == "python" || processName == "pythonw" || processName.StartsWith("python"))
                            {
                                SetProcessPriority(childProcessId, HIGH_PRIORITY_CLASS);
                                childCount++;
                                
                                // Also find grandchildren (Python processes spawned by batch scripts)
                                FindAndSetPriorityForGrandchildren(childProcessId);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing child process: {ex.Message}");
                        }
                    }
                }
                
                if (childCount > 0)
                {
                    Console.WriteLine($"Set priority for {childCount} child Python process(es) for {ServiceName}");
                }
                else
                {
                    // Fallback: Try to find Python processes by checking command line or working directory
                    // This is less accurate but may catch processes if WMI fails
                    FallbackSetPythonPriority();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding child Python processes for {ServiceName}: {ex.Message}");
                // Fallback to less accurate method
                FallbackSetPythonPriority();
            }
        }
        
        /// <summary>
        /// Fallback method to set priority for Python processes that might be related to this service
        /// This is less accurate but may help if WMI fails or processes aren't direct children
        /// </summary>
        private void FallbackSetPythonPriority()
        {
            try
            {
                Process[] pythonProcesses = Process.GetProcessesByName("python");
                
                foreach (Process pythonProc in pythonProcesses)
                {
                    try
                    {
                        // Try to check if the process's command line contains our service directory
                        // This requires checking MainModule which may fail without admin rights
                        if (pythonProc.MainModule != null)
                        {
                            string fileName = pythonProc.MainModule.FileName;
                            // If the Python executable is in our service directory's venv, it's likely ours
                            if (fileName.Contains(ServiceDirectory.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
                                fileName.Contains(ServiceDirectory.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
                            {
                                SetProcessPriority(pythonProc.Id, HIGH_PRIORITY_CLASS);
                                Console.WriteLine($"Set priority for Python process {pythonProc.Id} (matched by path)");
                            }
                        }
                    }
                    catch
                    {
                        // May not have access to MainModule, skip this process
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fallback priority setting failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Recursively finds and sets priority for grandchild processes (e.g., Python processes spawned by batch scripts)
        /// </summary>
        private void FindAndSetPriorityForGrandchildren(int parentProcessId)
        {
            try
            {
                string query = $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parentProcessId}";
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            int grandchildProcessId = Convert.ToInt32(obj["ProcessId"]);
                            Process? grandchildProcess = null;
                            
                            try
                            {
                                grandchildProcess = Process.GetProcessById(grandchildProcessId);
                            }
                            catch
                            {
                                continue;
                            }
                            
                            string processName = grandchildProcess.ProcessName.ToLower();
                            if (processName == "python" || processName == "pythonw" || processName.StartsWith("python"))
                            {
                                SetProcessPriority(grandchildProcessId, HIGH_PRIORITY_CLASS);
                                Console.WriteLine($"Set priority for grandchild Python process {grandchildProcessId}");
                                
                                // Recursively check for deeper descendants
                                FindAndSetPriorityForGrandchildren(grandchildProcessId);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing grandchild process: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding grandchildren: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Shows or hides the service console window (if owned by app)
        /// </summary>
        public void SetWindowVisibility(bool show)
        {
            if (!_ownedByApp)
            {
                Console.WriteLine($"Cannot change window visibility for {ServiceName} - not owned by app");
                return;
            }
            
            // If we don't have the handle yet, try to find it
            if (_consoleWindowHandle == IntPtr.Zero)
            {
                // First try by process ID if we have a process handle
                if (_process != null)
                {
                    _consoleWindowHandle = FindConsoleWindowForProcess(_process.Id);
                }
                
                // If that didn't work, try finding by window title
                if (_consoleWindowHandle == IntPtr.Zero)
                {
                    _consoleWindowHandle = FindConsoleWindowByTitle();
                }
            }
            
            if (_consoleWindowHandle != IntPtr.Zero)
            {
                int command = show ? SW_RESTORE : SW_HIDE;
                bool result = ShowWindow(_consoleWindowHandle, command);
                Console.WriteLine($"Set window visibility for {ServiceName}: {(show ? "SHOW" : "HIDE")} - Result: {result}");
            }
            else
            {
                Console.WriteLine($"Could not find console window for {ServiceName}");
            }
        }
    }
}
