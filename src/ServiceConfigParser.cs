using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UGTLive
{
    public static class ServiceConfigParser
    {
        /// <summary>
        /// Parses a service_config.txt file and returns a dictionary of key-value pairs
        /// </summary>
        /// <param name="configPath">Path to the service_config.txt file</param>
        /// <returns>Dictionary containing configuration key-value pairs</returns>
        public static Dictionary<string, string> ParseConfig(string configPath)
        {
            var config = new Dictionary<string, string>();
            
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file not found: {configPath}");
                return config;
            }
            
            try
            {
                // Read file with UTF-8 encoding
                string[] lines = File.ReadAllLines(configPath, Encoding.UTF8);
                
                foreach (string line in lines)
                {
                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    {
                        continue;
                    }
                    
                    // Parse pipe-delimited format: key|value|
                    string[] parts = line.Split('|');
                    
                    if (parts.Length >= 2)
                    {
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();
                        
                        if (!string.IsNullOrEmpty(key))
                        {
                            config[key] = value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing config file {configPath}: {ex.Message}");
            }
            
            return config;
        }
    }
}

