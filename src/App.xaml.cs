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
            
            // Initialize ChatBoxWindow instance without showing it
            // This ensures ChatBoxWindow.Instance is available immediately
            new ChatBoxWindow();
            
            // Create main window but don't show it yet
            // MainWindow will initialize LogWindow after setting up the console
            _mainWindow = new MainWindow();
            
            // We'll attach the keyboard handlers when the windows are loaded
            // Each window now has its own Application_KeyDown method attached to PreviewKeyDown
            
            // Show ServerSetupDialog as the startup/splash screen
            ShowServerSetupDialogAsStartup();
        }
        
        private void ShowServerSetupDialogAsStartup()
        {
            try
            {
                // Show the server setup dialog (which now acts as our startup screen)
                ServerSetupDialog dialog = ServerSetupDialog.Instance;
                
                // Set up event handler for when dialog closes
                dialog.Closed += (s, args) =>
                {
                    // Show main window after dialog closes
                    _mainWindow?.Show();
                    
                    // Attach key handler to other windows once main window is shown
                    AttachKeyHandlersToAllWindows();
                };
                
                // Show the dialog (modal - blocks until closed)
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error showing server setup dialog at startup: {ex.Message}");
                // Fallback: show main window if dialog fails
                _mainWindow?.Show();
                AttachKeyHandlersToAllWindows();
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
    // NOTE: This is currently unused - each window handles its own keyboard events
    private void Application_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Only process hotkeys at window level if global hotkeys are disabled
        // (When global hotkeys are enabled, the global hook handles them)
        if (!HotkeyManager.Instance.GetGlobalHotkeysEnabled())
        {
            var modifiers = System.Windows.Input.Keyboard.Modifiers;
            bool handled = HotkeyManager.Instance.HandleKeyDown(e.Key, modifiers);
            
            if (handled)
            {
                e.Handled = true;
            }
        }
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
        // Cleanup log window if it exists
        LogWindow.Instance?.cleanup();
        
        // Ensure server cleanup happens on exit
        ServerProcessManager.Instance.StopServer();
        base.OnExit(e);
    }
}