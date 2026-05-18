using System.Windows;

namespace UGTLive
{
    /// <summary>
    /// Exit confirmation dialog. The user selects one of three GPU-service
    /// actions (radio buttons) and clicks "Exit App" to quit, or "Back"
    /// (or Esc / the window X) to cancel and keep the app running.
    /// </summary>
    public partial class ExitConfirmDialog : Window
    {
        /// <summary>True only if the user clicked "Exit App".</summary>
        public bool Confirmed { get; private set; } = false;

        /// <summary>The GPU-service action chosen via radio button. Defaults to closing only owned services.</summary>
        public GpuServiceExitAction SelectedAction { get; private set; } = GpuServiceExitAction.CloseOwned;

        private const string ConfigKey = "LastGpuExitAction";

        public ExitConfirmDialog()
        {
            InitializeComponent();

            // Restore the last chosen option (default: close only owned services).
            string saved = ConfigManager.Instance.GetValue(ConfigKey, nameof(GpuServiceExitAction.CloseOwned));
            if (saved == nameof(GpuServiceExitAction.CloseAll))
                closeAllRadio.IsChecked = true;
            else if (saved == nameof(GpuServiceExitAction.LeaveRunning))
                leaveRunningRadio.IsChecked = true;
            else
                closeOwnedRadio.IsChecked = true;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            if (closeAllRadio.IsChecked == true)
                SelectedAction = GpuServiceExitAction.CloseAll;
            else if (leaveRunningRadio.IsChecked == true)
                SelectedAction = GpuServiceExitAction.LeaveRunning;
            else
                SelectedAction = GpuServiceExitAction.CloseOwned;

            // Remember the choice for next time.
            ConfigManager.Instance.SetValue(ConfigKey, SelectedAction.ToString());
            ConfigManager.Instance.SaveConfig();

            Confirmed = true;
            DialogResult = true; // closes the dialog
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
        }
    }
}
