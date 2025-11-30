using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace UGTLive
{
    // Manages gamepad input using Windows XInput API
    public class GamepadManager
    {
        #region XInput API
        
        [DllImport("xinput1_4.dll")]
        private static extern int XInputGetState(int dwUserIndex, ref XINPUT_STATE pState);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }
        
        // Button flags
        private const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        private const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        private const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        private const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        private const ushort XINPUT_GAMEPAD_START = 0x0010;
        private const ushort XINPUT_GAMEPAD_BACK = 0x0020;
        private const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
        private const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
        private const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        private const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        private const ushort XINPUT_GAMEPAD_A = 0x1000;
        private const ushort XINPUT_GAMEPAD_B = 0x2000;
        private const ushort XINPUT_GAMEPAD_X = 0x4000;
        private const ushort XINPUT_GAMEPAD_Y = 0x8000;
        
        private const int ERROR_SUCCESS = 0;
        
        #endregion
        
        private static GamepadManager? _instance;
        private System.Threading.Timer? _pollTimer;
        private ushort _lastButtonState = 0;
        private bool _isEnabled = false;
        
        public static GamepadManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new GamepadManager();
                }
                return _instance;
            }
        }
        
        public event EventHandler<List<string>>? ButtonsPressed;
        
        private GamepadManager()
        {
        }
        
        // Start polling for gamepad input
        public void Start()
        {
            if (_isEnabled)
                return;
                
            _isEnabled = true;
            _pollTimer = new System.Threading.Timer(PollGamepad, null, 0, 16); // Poll every 16ms (~60fps)
            Console.WriteLine("Gamepad manager started");
        }
        
        // Stop polling for gamepad input
        public void Stop()
        {
            if (!_isEnabled)
                return;
                
            _isEnabled = false;
            _pollTimer?.Dispose();
            _pollTimer = null;
            Console.WriteLine("Gamepad manager stopped");
        }
        
        // Poll gamepad state
        private void PollGamepad(object? state)
        {
            if (!_isEnabled)
                return;
                
            try
            {
                XINPUT_STATE gamepadState = new XINPUT_STATE();
                int result = XInputGetState(0, ref gamepadState); // Poll first controller
                
                if (result == ERROR_SUCCESS)
                {
                    ushort buttons = gamepadState.Gamepad.wButtons;
                    
                    // Detect newly pressed buttons (buttons that are now pressed but weren't before)
                    // This prevents triggering on button release
                    ushort newlyPressed = (ushort)(buttons & ~_lastButtonState);
                    
                    if (newlyPressed != 0)
                    {
                        List<string> pressedButtons = GetPressedButtons(buttons);
                        List<string> newlyPressedButtons = GetPressedButtons(newlyPressed);
                        Console.WriteLine($"Gamepad: newly pressed={string.Join(",", newlyPressedButtons)}, all pressed={string.Join(",", pressedButtons)}");
                        if (pressedButtons.Count > 0)
                        {
                            ButtonsPressed?.Invoke(this, pressedButtons);
                        }
                    }
                    
                    _lastButtonState = buttons;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error polling gamepad: {ex.Message}");
            }
        }
        
        // Get list of currently pressed buttons
        public List<string> GetPressedButtons(ushort buttonState)
        {
            List<string> buttons = new List<string>();
            
            if ((buttonState & XINPUT_GAMEPAD_A) != 0) buttons.Add("A");
            if ((buttonState & XINPUT_GAMEPAD_B) != 0) buttons.Add("B");
            if ((buttonState & XINPUT_GAMEPAD_X) != 0) buttons.Add("X");
            if ((buttonState & XINPUT_GAMEPAD_Y) != 0) buttons.Add("Y");
            if ((buttonState & XINPUT_GAMEPAD_DPAD_UP) != 0) buttons.Add("DPad_Up");
            if ((buttonState & XINPUT_GAMEPAD_DPAD_DOWN) != 0) buttons.Add("DPad_Down");
            if ((buttonState & XINPUT_GAMEPAD_DPAD_LEFT) != 0) buttons.Add("DPad_Left");
            if ((buttonState & XINPUT_GAMEPAD_DPAD_RIGHT) != 0) buttons.Add("DPad_Right");
            if ((buttonState & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0) buttons.Add("LB");
            if ((buttonState & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0) buttons.Add("RB");
            if ((buttonState & XINPUT_GAMEPAD_LEFT_THUMB) != 0) buttons.Add("LS");
            if ((buttonState & XINPUT_GAMEPAD_RIGHT_THUMB) != 0) buttons.Add("RS");
            if ((buttonState & XINPUT_GAMEPAD_START) != 0) buttons.Add("Start");
            if ((buttonState & XINPUT_GAMEPAD_BACK) != 0) buttons.Add("Back");
            
            return buttons;
        }
        
        // Get current gamepad state (for UI display)
        public List<string> GetCurrentlyPressedButtons()
        {
            try
            {
                XINPUT_STATE gamepadState = new XINPUT_STATE();
                int result = XInputGetState(0, ref gamepadState);
                
                if (result == ERROR_SUCCESS)
                {
                    return GetPressedButtons(gamepadState.Gamepad.wButtons);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting gamepad state: {ex.Message}");
            }
            
            return new List<string>();
        }
    }
}

