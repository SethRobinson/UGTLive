using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace UGTLive
{
    public class PythonServicesManager
    {
        private static PythonServicesManager? _instance;
        private readonly Dictionary<string, PythonService> _services;
        private readonly string[] _ignoredDirectories = { "shared", "util", "localdata" };
        
        public static PythonServicesManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new PythonServicesManager();
                }
                return _instance;
            }
        }
        
        private PythonServicesManager()
        {
            _services = new Dictionary<string, PythonService>();
        }
        
        /// <summary>
        /// Discovers all services in app/services directory
        /// Preserves runtime state (IsOwnedByApp, IsRunning) for existing services
        /// </summary>
        public void DiscoverServices()
        {
            // DON'T clear existing services - preserve runtime state!
            // _services.Clear();  <-- THIS WAS THE BUG!
            
            string servicesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "services");
            
            if (!Directory.Exists(servicesDir))
            {
                Console.WriteLine($"Services directory not found: {servicesDir}");
                return;
            }
            
            var subdirectories = Directory.GetDirectories(servicesDir);
            
            foreach (var dir in subdirectories)
            {
                string dirName = Path.GetFileName(dir);
                
                // Skip ignored directories
                if (_ignoredDirectories.Contains(dirName.ToLower()))
                {
                    continue;
                }
                
                // Try to parse service config
                var newService = PythonService.ParseFromConfig(dir);
                
                if (newService != null)
                {
                    // Check if we already have this service
                    if (_services.TryGetValue(newService.ServiceName, out var existingService))
                    {
                        // Service already exists - just update config values, preserve runtime state
                        Console.WriteLine($"Updating existing service: {newService.ServiceName} (IsOwnedByApp={existingService.IsOwnedByApp})");
                        
                        // Update AutoStart from config
                        existingService.AutoStart = ConfigManager.Instance.GetServiceAutoStart(newService.ServiceName);
                    }
                    else
                    {
                        // New service - add it
                        newService.AutoStart = ConfigManager.Instance.GetServiceAutoStart(newService.ServiceName);
                        _services[newService.ServiceName] = newService;
                        Console.WriteLine($"Discovered new service: {newService.ServiceName} (port {newService.Port})");
                    }
                }
            }
            
            Console.WriteLine($"Total services: {_services.Count}");
        }
        
        /// <summary>
        /// Gets a service by name
        /// </summary>
        public PythonService? GetServiceByName(string serviceName)
        {
            if (_services.TryGetValue(serviceName, out var service))
            {
                return service;
            }
            return null;
        }
        
        /// <summary>
        /// Checks if a service exists
        /// </summary>
        public bool DoesServiceExist(string serviceName)
        {
            return _services.ContainsKey(serviceName);
        }
        
        /// <summary>
        /// Checks if a service is running
        /// </summary>
        public async Task<bool> IsServiceRunningAsync(string serviceName)
        {
            var service = GetServiceByName(serviceName);
            
            
            if (service != null)
            {
                if (service.IsRunning) return true;

                return await service.CheckIsRunningAsync();
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets all discovered services
        /// </summary>
        public List<PythonService> GetAllServices()
        {
            return _services.Values.ToList();
        }
        
        /// <summary>
        /// Starts all services that have AutoStart enabled (parallel execution for faster startup)
        /// </summary>
        public async Task StartAutoStartServicesAsync(bool showWindow, Action<string>? statusCallback = null)
        {
            var autoStartServices = _services.Values.Where(s => s.AutoStart).ToList();
            
            if (autoStartServices.Count == 0)
            {
                statusCallback?.Invoke("No services configured for auto-start");
                return;
            }
            
            // Phase 1: Check all services in parallel to see if they're already running
            statusCallback?.Invoke($"Checking status of {autoStartServices.Count} service(s)...");
            
            var checkTasks = autoStartServices.Select(async service =>
            {
                if (!service.IsRunning)
                {
                    await service.CheckIsRunningAsync();
                }
                return service;
            });
            
            await Task.WhenAll(checkTasks);
            
            // Filter to only services that need starting
            var servicesToStart = autoStartServices.Where(s => !s.IsRunning).ToList();
            var alreadyRunningCount = autoStartServices.Count - servicesToStart.Count;
            
            if (alreadyRunningCount > 0)
            {
                statusCallback?.Invoke($"{alreadyRunningCount} service(s) already running - skipping");
            }
            
            if (servicesToStart.Count == 0)
            {
                statusCallback?.Invoke("All services already running");
                return;
            }
            
            // Phase 2: Start all services with staggered launches
            statusCallback?.Invoke($"Starting {servicesToStart.Count} service(s)...");
            
            var startTasks = servicesToStart.Select(async (service, index) =>
            {
                // Stagger process starts by 1.5 seconds each to avoid conflicts
                if (index > 0)
                {
                    await Task.Delay(100 * index);
                }
                
                statusCallback?.Invoke($"Starting {service.ServiceName}...");
                
                bool started = await service.StartAsync(showWindow);
                
                if (started)
                {
                    statusCallback?.Invoke($"{service.ServiceName} started successfully");
                }
                else
                {
                    statusCallback?.Invoke($"Failed to start {service.ServiceName}");
                }
                
                return started;
            });
            
            await Task.WhenAll(startTasks);
            
            statusCallback?.Invoke("Service startup complete");
        }
        
        /// <summary>
        /// Stops all services that were started by the app (parallel execution for faster shutdown)
        /// </summary>
        public async Task StopOwnedServicesAsync()
        {
            Console.WriteLine("=== StopOwnedServicesAsync called ===");
            Console.WriteLine($"Total services: {_services.Count}");
            
            foreach (var service in _services.Values)
            {
                Console.WriteLine($"  {service.ServiceName}: IsOwnedByApp={service.IsOwnedByApp}, IsRunning={service.IsRunning}");
            }
            
            var ownedServices = _services.Values.Where(s => s.IsOwnedByApp).ToList();
            
            if (ownedServices.Count == 0)
            {
                Console.WriteLine("No owned services to stop");
                return;
            }
            
            Console.WriteLine($"Stopping {ownedServices.Count} owned service(s) in parallel...");
            
            var stopTasks = ownedServices.Select(async service =>
            {
                Console.WriteLine($"Stopping {service.ServiceName}...");
                await service.StopAsync();
            });
            
            await Task.WhenAll(stopTasks);
            
            Console.WriteLine("All owned services stopped");
        }
        
        /// <summary>
        /// Saves AutoStart preferences for all services
        /// </summary>
        public void SaveAutoStartPreferences()
        {
            foreach (var service in _services.Values)
            {
                ConfigManager.Instance.SetServiceAutoStart(service.ServiceName, service.AutoStart);
            }
        }
        
        /// <summary>
        /// Refreshes the status of all services
        /// Only checks services that aren't already marked as running
        /// </summary>
        public async Task RefreshAllServicesStatusAsync()
        {
            var tasks = _services.Values.Select(async service =>
            {
                // Only check if not already marked as running
                if (!service.IsRunning)
                {
                    await service.CheckIsRunningAsync();
                }
                
                // Only check installation if still not running after check
                if (!service.IsRunning)
                {
                    await service.CheckIsInstalledAsync();
                }
            });
            
            await Task.WhenAll(tasks);
        }
    }
}

