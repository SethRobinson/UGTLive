using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Application = System.Windows.Application;

namespace UGTLive;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private MainWindow? _mainWindow;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Set up application-wide keyboard handling
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        
        // We'll hook keyboard events in the main window and other windows instead
        // of at the application level (which isn't supported in this context)
        
        // Show splash screen first
        SplashManager.Instance.ShowSplash();
        
        // Initialize ChatBoxWindow instance without showing it
        // This ensures ChatBoxWindow.Instance is available immediately
        new ChatBoxWindow();
        
        // Create main window but don't show it yet
        _mainWindow = new MainWindow();
        
        // We'll attach the keyboard handlers when the windows are loaded
        // Each window now has its own Application_KeyDown method attached to PreviewKeyDown
        
        // Add event handler to show main window after splash closes
        SplashManager.Instance.SplashClosed += async (sender, args) =>
        {
            // Check if server is needed and running before showing main window
            await CheckServerAndShowDialogIfNeededAsync();
            
            _mainWindow?.Show();
            
            // Attach key handler to other windows once main window is shown
            AttachKeyHandlersToAllWindows();
        };
    }
    
    /// <summary>
    /// Checks if server is running and shows setup dialog if needed
    /// </summary>
    private async Task CheckServerAndShowDialogIfNeededAsync()
    {
        try
        {
            // Check what OCR method is configured
            string ocrMethod = ConfigManager.Instance.GetOcrMethod();
            
            // Only check server if using EasyOCR, Manga OCR, or docTR
            if (ocrMethod != "EasyOCR" && ocrMethod != "Manga OCR" && ocrMethod != "docTR")
            {
                // Using Windows OCR, no server needed
                return;
            }
            
            // Detect if server is already running
            bool serverRunning = await ServerProcessManager.Instance.DetectExistingServerAsync();
            
            if (!serverRunning)
            {
                // Server not running, show setup dialog
                this.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        ServerSetupDialog.ShowDialogSafe();
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"Error showing server setup dialog at startup: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Error checking server status at startup: {ex.Message}");
        }
    }
    
    // Ensure all windows are initialized and loaded
    private void AttachKeyHandlersToAllWindows()
    {
        // Each window now automatically attaches its own keyboard handler
        // when it's loaded, using PreviewKeyDown and its own Application_KeyDown method.
        // We don't need to do anything here anymore.
    }
    
    // Handle application-level keyboard events
    private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // No need to check window focus since this is only called when a window has focus
        KeyboardShortcuts.HandleKeyDown(e);
    }
    
    // Handle any unhandled exceptions to prevent app crashes
    private void App_DispatcherUnhandledException(object sender, 
                                               System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // Log the exception
        System.Console.WriteLine($"Unhandled application exception: {e.Exception.Message}");
        System.Console.WriteLine($"Stack trace: {e.Exception.StackTrace}");
        
        // Mark as handled to prevent app from crashing
        e.Handled = true;
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        // Ensure server cleanup happens on exit
        ServerProcessManager.Instance.StopServer();
        base.OnExit(e);
    }
}