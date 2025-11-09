using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
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
        public async Task<(bool success, string errorMessage)> StartServerProcessAsync()
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
                        CreateNoWindow = false // Show window so user can see server output
                    };
                    
                    _serverProcess = Process.Start(psi);
                    
                    if (_serverProcess == null)
                    {
                        return (false, "Failed to start server process");
                    }
                    
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
        
        
        /// <summary>
        /// Gets whether the server was started by this app
        /// </summary>
        public bool ServerStartedByApp => _serverStartedByApp;
    }
}

