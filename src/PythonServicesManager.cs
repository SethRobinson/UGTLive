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
        /// </summary>
        public void DiscoverServices()
        {
            _services.Clear();
            
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
                var service = PythonService.ParseFromConfig(dir);
                
                if (service != null)
                {
                    // Load AutoStart preference from config
                    service.AutoStart = ConfigManager.Instance.GetServiceAutoStart(service.ServiceName);
                    
                    _services[service.ServiceName] = service;
                    Console.WriteLine($"Discovered service: {service.ServiceName} (port {service.Port})");
                }
            }
            
            Console.WriteLine($"Total services discovered: {_services.Count}");
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
        /// Starts all services that have AutoStart enabled
        /// </summary>
        public async Task StartAutoStartServicesAsync(bool showWindow, Action<string>? statusCallback = null)
        {
            var autoStartServices = _services.Values.Where(s => s.AutoStart).ToList();
            
            if (autoStartServices.Count == 0)
            {
                statusCallback?.Invoke("No services configured for auto-start");
                return;
            }
            
            foreach (var service in autoStartServices)
            {
                statusCallback?.Invoke($"Starting {service.ServiceName}...");
                
                bool started = await service.StartAsync(showWindow);
                
                if (started)
                {
                    // Wait a moment for service to become available
                    await Task.Delay(2000);
                    
                    // Check if it's actually running
                    bool isRunning = await service.CheckIsRunningAsync();
                    
                    if (isRunning)
                    {
                        statusCallback?.Invoke($"{service.ServiceName} started successfully");
                    }
                    else
                    {
                        statusCallback?.Invoke($"{service.ServiceName} process started but not responding yet");
                    }
                }
                else
                {
                    statusCallback?.Invoke($"Failed to start {service.ServiceName}");
                }
            }
        }
        
        /// <summary>
        /// Stops all services that were started by the app
        /// </summary>
        public async Task StopOwnedServicesAsync()
        {
            var ownedServices = _services.Values.Where(s => s.IsOwnedByApp).ToList();
            
            if (ownedServices.Count == 0)
            {
                Console.WriteLine("No owned services to stop");
                return;
            }
            
            Console.WriteLine($"Stopping {ownedServices.Count} owned service(s)...");
            
            foreach (var service in ownedServices)
            {
                Console.WriteLine($"Stopping {service.ServiceName}...");
                await service.StopAsync();
            }
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
        /// </summary>
        public async Task RefreshAllServicesStatusAsync()
        {
            var tasks = _services.Values.Select(async service =>
            {
                await service.CheckIsRunningAsync();
                if (!service.IsRunning)
                {
                    await service.CheckIsInstalledAsync();
                }
            });
            
            await Task.WhenAll(tasks);
        }
    }
}

