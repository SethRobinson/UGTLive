using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UGTLive
{
    public class ServiceItemViewModel : INotifyPropertyChanged
    {
        private string _serviceName = "";
        private string _description = "";
        private int _port;
        private string _statusIcon = "⏳";
        private string _statusText = "Checking...";
        private string _statusColor = "Gray";
        private bool _autoStart;
        private string _startStopButtonText = "Start";
        private bool _startStopEnabled;
        private bool _installEnabled;
        private bool _uninstallEnabled;
        private bool _testEnabled;
        
        public string ServiceName
        {
            get => _serviceName;
            set { _serviceName = value; OnPropertyChanged(); }
        }
        
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }
        
        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(); }
        }
        
        public string StatusIcon
        {
            get => _statusIcon;
            set { _statusIcon = value; OnPropertyChanged(); }
        }
        
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }
        
        public string StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }
        
        public bool AutoStart
        {
            get => _autoStart;
            set { _autoStart = value; OnPropertyChanged(); }
        }
        
        public string StartStopButtonText
        {
            get => _startStopButtonText;
            set { _startStopButtonText = value; OnPropertyChanged(); }
        }
        
        public bool StartStopEnabled
        {
            get => _startStopEnabled;
            set { _startStopEnabled = value; OnPropertyChanged(); }
        }
        
        public bool InstallEnabled
        {
            get => _installEnabled;
            set { _installEnabled = value; OnPropertyChanged(); }
        }
        
        public bool UninstallEnabled
        {
            get => _uninstallEnabled;
            set { _uninstallEnabled = value; OnPropertyChanged(); }
        }
        
        public bool TestEnabled
        {
            get => _testEnabled;
            set { _testEnabled = value; OnPropertyChanged(); }
        }
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

