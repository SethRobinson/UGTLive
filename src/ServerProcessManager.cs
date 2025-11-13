using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace UGTLive
{
    public class ServerProcessManager
    {
        private static ServerProcessManager? _instance;
        private Process? _serverProcess;
        private bool _serverStartedByApp = false;
        
        public static ServerProcessManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ServerProcessManager();
                }
                return _instance;
            }
        }
        
        private ServerProcessManager() { }
        
        /// <summary>
        /// Detects if server is already running before app starts
        /// </summary>
        public async Task<bool> DetectExistingServerAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var tcpClient = new TcpClient())
                    {
                        var result = tcpClient.BeginConnect("127.0.0.1", 9999, null, null);
                        var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                        
                        if (success)
                        {
                            tcpClient.EndConnect(result);
                            // Server is already running, don't manage its lifecycle
                            _serverStartedByApp = false;
                            return true;
                        }
                    }
                }
                catch { }
                
                return false;
            });
        }
        
        /// <summary>
        /// Checks if server is currently running
        /// </summary>
        public async Task<bool> IsServerRunningAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var tcpClient = new TcpClient())
                    {
                        var result = tcpClient.BeginConnect("127.0.0.1", 9999, null, null);
                        var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                        
                        if (success)
                        {
                            tcpClient.EndConnect(result);
                            return true;
                        }
                    }
                }
                catch { }
                
                return false;
            });
        }
        
        /// <summary>
        /// Starts the server process using RunServer.bat (does not wait for server to be ready)
        /// </summary>
        /// <param name="showWindow">If true, shows the server window. If false, runs invisibly.</param>
        public async Task<(bool success, string errorMessage)> StartServerProcessAsync(bool showWindow = true)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Check if already running
                    if (IsServerRunningAsync().Result)
                    {
                        return (true, "Server is already running");
                    }
                    
                    string webserverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "webserver");
                    string runServerBatch = Path.Combine(webserverPath, "RunServer.bat");
                    
                    if (!File.Exists(runServerBatch))
                    {
                        return (false, $"RunServer.bat not found at {runServerBatch}");
                    }
                    
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = runServerBatch,
                        WorkingDirectory = webserverPath,
                        UseShellExecute = true,
                        WindowStyle = showWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden
                    };
                    
                    _serverProcess = Process.Start(psi);
                    
                    if (_serverProcess == null)
                    {
                        return (false, "Failed to start server process");
                    }
                    
                    // Wait a moment for the process to start, then set window visibility
                    System.Threading.Thread.Sleep(500);
                    SetServerWindowVisibility(showWindow);
                    
                    // Mark that we started the server
                    _serverStartedByApp = true;
                    
                    return (true, "");
                }
                catch (Exception ex)
                {
                    return (false, $"Error starting server: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// Starts the server using RunServer.bat (legacy method for backward compatibility)
        /// </summary>
        public async Task<(bool success, string errorMessage)> StartServerAsync()
        {
            var result = await StartServerProcessAsync();
            if (!result.success)
            {
                return result;
            }
            
            // Wait a moment for server to start
            await Task.Delay(2000);
            
            // Verify server started
            if (await IsServerRunningAsync())
            {
                return (true, "");
            }
            else
            {
                return (false, "Server process started but not responding on port 9999");
            }
        }
        
        /// <summary>
        /// Stops the server if it was started by this app
        /// </summary>
        public void StopServer()
        {
            if (!_serverStartedByApp)
            {
                Console.WriteLine("Server was not started by this app, leaving it running");
                return;
            }
            
            try
            {
                DateTime? serverStartTime = null;
                
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    Console.WriteLine("Stopping server process started by this app");
                    
                    // Capture start time before disposing
                    try
                    {
                        serverStartTime = _serverProcess.StartTime;
                    }
                    catch { }
                    
                    // Try graceful shutdown first
                    try
                    {
                        _serverProcess.CloseMainWindow();
                        if (!_serverProcess.WaitForExit(3000))
                        {
                            // Force kill if graceful shutdown didn't work
                            _serverProcess.Kill();
                            _serverProcess.WaitForExit();
                        }
                    }
                    catch
                    {
                        // If CloseMainWindow fails, try kill
                        try
                        {
                            _serverProcess.Kill();
                            _serverProcess.WaitForExit();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error killing server process: {ex.Message}");
                        }
                    }
                    
                    _serverProcess.Dispose();
                    _serverProcess = null;
                }
                
                // Also try to find and kill any python processes that might be the server
                // Note: This is a best-effort approach. We track the server process start time
                // before disposing it, then use that to identify related python processes.
                
                try
                {
                    Process[] pythonProcesses = Process.GetProcessesByName("python");
                    foreach (Process proc in pythonProcesses)
                    {
                        try
                        {
                            // Only kill if process was started around the same time as our server process
                            // This is a heuristic to avoid killing unrelated python processes
                            if (serverStartTime.HasValue)
                            {
                                TimeSpan timeDiff = proc.StartTime - serverStartTime.Value;
                                if (Math.Abs(timeDiff.TotalSeconds) < 10) // Within 10 seconds
                                {
                                    Console.WriteLine($"Found potential server.py process (PID {proc.Id}), killing");
                                    proc.Kill();
                                    proc.WaitForExit();
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error finding server processes: {ex.Message}");
                }
                
                _serverStartedByApp = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping server: {ex.Message}");
            }
        }
        
        // Windows API functions for showing/hiding windows
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        
        /// <summary>
        /// Sets the visibility of the server window if it's running
        /// </summary>
        public void SetServerWindowVisibility(bool show)
        {
            if (_serverProcess == null || _serverProcess.HasExited)
            {
                return;
            }
            
            try
            {
                uint processId = (uint)_serverProcess.Id;
                IntPtr serverWindowHandle = IntPtr.Zero;
                
                // Find the window belonging to this process
                EnumWindows((hWnd, lParam) =>
                {
                    GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                    if (windowProcessId == processId)
                    {
                        // Check if this is a console window (has a class name starting with "ConsoleWindowClass")
                        // We want to find the main window for the process
                        serverWindowHandle = hWnd;
                        return false; // Stop enumeration
                    }
                    return true; // Continue enumeration
                }, IntPtr.Zero);
                
                if (serverWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(serverWindowHandle, show ? SW_SHOW : SW_HIDE);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting server window visibility: {ex.Message}");
            }
        }
        
        
        /// <summary>
        /// Gets whether the server was started by this app
        /// </summary>
        public bool ServerStartedByApp => _serverStartedByApp;
        
        /// <summary>
        /// Force stops any server running on port 9999, even if not started by this app
        /// </summary>
        public async Task<(bool success, string errorMessage)> ForceStopServerAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // First check if port 9999 is actually in use
                    bool portInUse = false;
                    try
                    {
                        using (var tcpClient = new TcpClient())
                        {
                            var result = tcpClient.BeginConnect("127.0.0.1", 9999, null, null);
                            portInUse = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));
                            if (portInUse)
                            {
                                tcpClient.EndConnect(result);
                            }
                        }
                    }
                    catch { }
                    
                    if (!portInUse)
                    {
                        return (true, "No server running on port 9999");
                    }
                    
                    // First try to stop server we started
                    if (_serverStartedByApp && _serverProcess != null && !_serverProcess.HasExited)
                    {
                        StopServer();
                        // Wait a moment and check if port is still in use
                        System.Threading.Thread.Sleep(1000);
                        try
                        {
                            using (var tcpClient = new TcpClient())
                            {
                                var result = tcpClient.BeginConnect("127.0.0.1", 9999, null, null);
                                if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100)))
                                {
                                    return (true, "Server stopped");
                                }
                            }
                        }
                        catch { }
                    }
                    
                    // If port is still in use, try to find and kill python/pythonw processes
                    // that might be running the server
                    try
                    {
                        string[] processNames = { "python", "pythonw" };
                        
                        foreach (string processName in processNames)
                        {
                            Process[] processes = Process.GetProcessesByName(processName);
                            
                            foreach (Process proc in processes)
                            {
                                try
                                {
                                    // Try to check if this process might be our server
                                    // by checking if it's listening on port 9999
                                    // We'll kill processes one by one until port is released
                                    Console.WriteLine($"Attempting to stop potential server process: {processName} (PID {proc.Id})");
                                    proc.Kill();
                                    proc.WaitForExit(5000);
                                    
                                    // Wait a moment for port to be released
                                    System.Threading.Thread.Sleep(1000);
                                    
                                    // Check if port is now released
                                    bool stillInUse = false;
                                    try
                                    {
                                        using (var tcpClient = new TcpClient())
                                        {
                                            var result = tcpClient.BeginConnect("127.0.0.1", 9999, null, null);
                                            stillInUse = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));
                                            if (stillInUse)
                                            {
                                                tcpClient.EndConnect(result);
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        // Port released (connection failed means port is free)
                                        return (true, "Server stopped");
                                    }
                                    
                                    if (!stillInUse)
                                    {
                                        return (true, "Server stopped");
                                    }
                                    
                                    // Port still in use, try next process
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error stopping process {proc.Id}: {ex.Message}");
                                    // Continue to next process
                                }
                            }
                        }
                        
                        // If we get here, we tried all processes but port is still in use
                        return (false, "Could not stop server - port 9999 still in use");
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Error stopping server: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    return (false, $"Error: {ex.Message}");
                }
            });
        }
    }
}

