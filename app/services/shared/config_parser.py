"""Configuration parser for service_config.txt files."""

from pathlib import Path
from typing import Dict, Optional


def parse_service_config(config_path: str) -> Dict[str, str]:
    """
    Parse a service_config.txt file and return a dictionary of settings.
    
    Format: key|value|
    
    Args:
        config_path: Path to the service_config.txt file
        
    Returns:
        Dictionary of configuration key-value pairs
    """
    config = {}
    
    try:
        config_file = Path(config_path)
        if not config_file.exists():
            return config
            
        with open(config_file, 'r', encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith('#'):
                    continue
                    
                # Parse key|value| format
                parts = line.split('|')
                if len(parts) >= 2:
                    key = parts[0].strip()
                    value = parts[1].strip()
                    if key:
                        config[key] = value
                        
    except Exception as e:
        print(f"Error parsing config file {config_path}: {e}")
        
    return config


def get_config_value(config: Dict[str, str], key: str, default: str = "") -> str:
    """
    Get a configuration value with a default fallback.
    
    Args:
        config: Configuration dictionary
        key: Configuration key to retrieve
        default: Default value if key not found
        
    Returns:
        Configuration value or default
    """
    return config.get(key, default)


def get_service_port(config_path: str) -> int:
    """
    Get the port number from a service configuration file.
    
    Args:
        config_path: Path to the service_config.txt file
        
    Returns:
        Port number (default: 5000)
    """
    config = parse_service_config(config_path)
    port_str = get_config_value(config, 'port', '5000')
    try:
        return int(port_str)
    except ValueError:
        return 5000


def get_service_name(config_path: str) -> str:
    """
    Get the service name from a service configuration file.
    
    Args:
        config_path: Path to the service_config.txt file
        
    Returns:
        Service name (default: 'Unknown Service')
    """
    config = parse_service_config(config_path)
    return get_config_value(config, 'service_name', 'Unknown Service')

