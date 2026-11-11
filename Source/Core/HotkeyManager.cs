using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickShot.Core
{
    public sealed class HotkeyManager : IDisposable
    {
        private class Binding
        {
            public int Id;
            public string Task;
            public uint Vk;
            public bool Ctrl, Alt, Shift, Win;
        }

        private sealed class HotkeyMsgWindow : NativeWindow, IDisposable
        {
            private readonly Action<int> _onHotKeyId;

            public HotkeyMsgWindow(Action<int> onHotKeyId)
            {
                _onHotKeyId = onHotKeyId;
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeMethods.WM_HOTKEY)
                {
                    _onHotKeyId?.Invoke(m.WParam.ToInt32());
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                if (Handle != IntPtr.Zero)
                    DestroyHandle();
            }
        }

        private readonly List<Binding> _bindings = new List<Binding>();
        private readonly List<int> _registeredIds = new List<int>();
        private readonly Dictionary<uint, int> _downVkTicks = new Dictionary<uint, int>();
        private readonly Dictionary<string, int> _lastTaskFireTick = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly NativeMethods.LowLevelKeyboardProc _hookProc;
        private readonly HotkeyMsgWindow _hotkeyWindow;
        private IntPtr _hookId = IntPtr.Zero;
        private volatile bool _paused;
        private readonly Control _syncControl;
        private int _nextId = 100;

        // Live LL modifier tracking (avoids GetAsyncKeyState race on fast chords + cleans stale state automatically)
        private bool _lCtrl, _rCtrl, _lShift, _rShift, _lAlt, _rAlt, _lWin, _rWin;

        public event Action<string> HotkeyTriggered;

        private static readonly Dictionary<string, uint> VkMap = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            ["BACKSPACE"] = 0x08, ["BACK"] = 0x08, ["TAB"] = 0x09, ["ENTER"] = 0x0D, ["RETURN"] = 0x0D,
            ["PAUSE"] = 0x13, ["PAUSEBREAK"] = 0x13, ["CANCEL"] = 0x13,
            ["CAPSLOCK"] = 0x14, ["ESCAPE"] = 0x1B, ["ESC"] = 0x1B, ["SPACE"] = 0x20,
            ["PAGEUP"] = 0x21, ["PRIOR"] = 0x21, ["PAGEDOWN"] = 0x22, ["NEXT"] = 0x22, ["END"] = 0x23, ["HOME"] = 0x24,
            ["LEFT"] = 0x25, ["UP"] = 0x26, ["RIGHT"] = 0x27, ["DOWN"] = 0x28,
            ["PRINTSCREEN"] = 0x2C, ["SNAPSHOT"] = 0x2C, ["PRNTSC"] = 0x2C,
            ["INSERT"] = 0x2D, ["DELETE"] = 0x2E, ["DEL"] = 0x2E,
            ["NUMLOCK"] = 0x90, ["SCROLLLOCK"] = 0x91, ["SCROLL"] = 0x91,
            ["NUM0"] = 0x60, ["NUM1"] = 0x61, ["NUM2"] = 0x62, ["NUM3"] = 0x63, ["NUM4"] = 0x64,
            ["NUM5"] = 0x65, ["NUM6"] = 0x66, ["NUM7"] = 0x67, ["NUM8"] = 0x68, ["NUM9"] = 0x69,
            ["NUMPAD0"] = 0x60, ["NUMPAD1"] = 0x61, ["NUMPAD2"] = 0x62, ["NUMPAD3"] = 0x63, ["NUMPAD4"] = 0x64,
            ["NUMPAD5"] = 0x65, ["NUMPAD6"] = 0x66, ["NUMPAD7"] = 0x67, ["NUMPAD8"] = 0x68, ["NUMPAD9"] = 0x69,
            ["NUMMULT"] = 0x6A, ["MULTIPLY"] = 0x6A, ["NUMADD"] = 0x6B, ["ADD"] = 0x6B,
            ["NUMSUB"] = 0x6D, ["SUBTRACT"] = 0x6D, ["NUMDEC"] = 0x6E, ["DECIMAL"] = 0x6E, ["NUMDIV"] = 0x6F, ["DIVIDE"] = 0x6F,
            [";"] = 0xBA, ["OEMSEMICOLON"] = 0xBA, ["OEM1"] = 0xBA,
            ["="] = 0xBB, ["PLUS"] = 0xBB, ["OEMPLUS"] = 0xBB,
            [","] = 0xBC, ["OEMCOMMA"] = 0xBC,
            ["-"] = 0xBD, ["OEMMINUS"] = 0xBD,
            ["."] = 0xBE, ["OEMPERIOD"] = 0xBE,
            ["/"] = 0xBF, ["OEMQUESTION"] = 0xBF, ["OEM2"] = 0xBF,
            ["`"] = 0xC0, ["OEMTILDE"] = 0xC0, ["OEM3"] = 0xC0,
            ["["] = 0xDB, ["OEMOPENBRACKETS"] = 0xDB, ["OEM4"] = 0xDB,
            ["\\"] = 0xDC, ["OEMPIPE"] = 0xDC, ["OEM5"] = 0xDC,
            ["]"] = 0xDD, ["OEMCLOSEBRACKETS"] = 0xDD, ["OEM6"] = 0xDD,
            ["'"] = 0xDE, ["OEMQUOTES"] = 0xDE, ["OEM7"] = 0xDE
        };

        static HotkeyManager()
        {
            foreach (Keys k in Enum.GetValues(typeof(Keys)))
            {
                int code = (int)k & 0xFFFF;
                if (code > 0 && code < 256)
                {
                    string name = k.ToString();
                    if (!VkMap.ContainsKey(name))
                        VkMap[name] = (uint)code;
                }
            }
            for (int i = 1; i <= 24; i++) VkMap["F" + i] = (uint)(0x6F + i);
            for (int i = 0; i <= 9; i++) VkMap[i.ToString()] = (uint)(0x30 + i);
            for (int i = 65; i <= 90; i++) VkMap[((char)i).ToString()] = (uint)i;
        }

        public HotkeyManager(Control syncControl)
        {
            _syncControl = syncControl;
            _hookProc = HookCallback;
            InstallLowLevelHook();
            try
            {
                _hotkeyWindow = new HotkeyMsgWindow(OnOsHotKeyId);
            }
            catch { }
        }

        private void InstallLowLevelHook()
        {
            if (_hookId != IntPtr.Zero) return;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            _hookId = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _hookProc,
                NativeMethods.GetModuleHandle(curModule.ModuleName),
                0);
        }

        public void Pause()
        {
            _paused = true;
            _downVkTicks.Clear();
            ClearModifierCache();
        }

        public void Resume()
        {
            _downVkTicks.Clear();
            ClearModifierCache();
            _paused = false;
            if (_hookId == IntPtr.Zero) InstallLowLevelHook();
        }

        private void ClearModifierCache()
        {
            _lCtrl = _rCtrl = _lShift = _rShift = _lAlt = _rAlt = _lWin = _rWin = false;
        }

        public void ApplySettings(HotkeySettings hk, bool includeF9Stop = false)
        {
            lock (_bindings)
            {
                UnregisterAllOsHotkeys();
                _bindings.Clear();
                if (hk != null)
                {
                    AddBinding("captureRegion", hk.CaptureRegion == null ? "PrintScreen" : hk.CaptureRegion);
                    AddBinding("captureScrolling", hk.CaptureScrolling);
                    AddBinding("recordGif", hk.RecordGif);
                    AddBinding("ocrSnip", hk.OcrSnip);
                }
                if (includeF9Stop)
                {
                    string stopKey = !string.IsNullOrWhiteSpace(hk?.StopRecording) ? hk.StopRecording : "F9";
                    AddBinding("stopRecording", stopKey);
                    string pauseKey = !string.IsNullOrWhiteSpace(hk?.PauseResumeRecording) ? hk.PauseResumeRecording : "F10";
                    AddBinding("pauseRecording", pauseKey);
                }
                RegisterAllOsHotkeys();
            }
        }

        private void UnregisterAllOsHotkeys()
        {
            if (_hotkeyWindow == null || _hotkeyWindow.Handle == IntPtr.Zero) return;
            foreach (int id in _registeredIds)
                NativeMethods.UnregisterHotKey(_hotkeyWindow.Handle, id);
            _registeredIds.Clear();
        }

        private void RegisterAllOsHotkeys()
        {
            if (_hotkeyWindow == null || _hotkeyWindow.Handle == IntPtr.Zero) return;
            foreach (var b in _bindings)
            {
                uint mods = NativeMethods.MOD_NOREPEAT;
                if (b.Ctrl) mods |= NativeMethods.MOD_CONTROL;
                if (b.Alt) mods |= NativeMethods.MOD_ALT;
                if (b.Shift) mods |= NativeMethods.MOD_SHIFT;
                if (b.Win) mods |= NativeMethods.MOD_WIN;
                if (NativeMethods.RegisterHotKey(_hotkeyWindow.Handle, b.Id, mods, b.Vk))
                    _registeredIds.Add(b.Id);
            }
        }

        private void OnOsHotKeyId(int id)
        {
            if (_paused) return;
            string matchedTask = null;
            lock (_bindings)
            {
                foreach (var b in _bindings)
                {
                    if (b.Id == id) { matchedTask = b.Task; break; }
                }
            }
            if (matchedTask != null)
                DispatchTaskDeduped(matchedTask);
        }

        private void DispatchTaskDeduped(string task)
        {
            int now = Environment.TickCount;
            lock (_lastTaskFireTick)
            {
                if (_lastTaskFireTick.TryGetValue(task, out int prev) && unchecked(now - prev) < 260)
                    return;
                _lastTaskFireTick[task] = now;
            }
            if (_syncControl != null && !_syncControl.IsDisposed && _syncControl.IsHandleCreated)
            {
                _syncControl.BeginInvoke(new Action(() => HotkeyTriggered?.Invoke(task)));
            }
            else
            {
                HotkeyTriggered?.Invoke(task);
            }
        }

        private void AddBinding(string task, string accel)
        {
            if (string.IsNullOrWhiteSpace(accel) || accel.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
                return;

            string[] parts = accel.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            bool c = false, a = false, s = false, w = false;
            string mainKey = null;

            foreach (string raw in parts)
            {
                string up = raw.Trim().ToUpperInvariant();
                if (up == "CTRL" || up == "CONTROL" || up == "COMMANDORCONTROL" || up == "CMDORCTRL") c = true;
                else if (up == "ALT" || up == "OPTION") a = true;
                else if (up == "SHIFT") s = true;
                else if (up == "SUPER" || up == "WIN" || up == "META" || up == "COMMAND") w = true;
                else mainKey = up.Replace(" ", "");
            }

            if (string.IsNullOrEmpty(mainKey) || !VkMap.TryGetValue(mainKey, out uint vk))
                return;

            _bindings.RemoveAll(b => b.Task == task);
            _bindings.Add(new Binding { Id = _nextId++, Task = task, Vk = vk, Ctrl = c, Alt = a, Shift = s, Win = w });
        }

        public static string FormatDisplay(string accel)
        {
            if (string.IsNullOrWhiteSpace(accel) || accel.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
                return "None";
            return accel
                .Replace("CommandOrControl", "Ctrl")
                .Replace("Super", "Win")
                .Replace("ScrollLock", "Scroll Lock")
                .Replace("PauseBreak", "Pause / Break")
                .Replace("Pause", "Pause / Break")
                .Replace("+", " + ");
        }

        private static bool IsModifierVk(uint vk) =>
            vk == 0x10 || vk == 0x11 || vk == 0x12 ||
            vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 ||
            vk == 0xA4 || vk == 0xA5 || vk == 0x5B || vk == 0x5C;

        private void UpdateModifierTrack(uint vk, bool down)
        {
            switch (vk)
            {
                case 0xA2: _lCtrl = down; break;
                case 0xA3: _rCtrl = down; break;
                case 0x11: _lCtrl = down; if (!down) _rCtrl = false; break;
                case 0xA0: _lShift = down; break;
                case 0xA1: _rShift = down; break;
                case 0x10: _lShift = down; if (!down) _rShift = false; break;
                case 0xA4: _lAlt = down; break;
                case 0xA5: _rAlt = down; break;
                case 0x12: _lAlt = down; if (!down) _rAlt = false; break;
                case 0x5B: _lWin = down; break;
                case 0x5C: _rWin = down; break;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                var kb = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                uint vk = kb.vkCode;
                if (vk == 0x03) vk = 0x13; // Ctrl+Break -> Pause

                bool isDown = msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN;
                bool isUp = msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP;

                if (IsModifierVk(vk))
                {
                    if (isDown) UpdateModifierTrack(vk, true);
                    else if (isUp) UpdateModifierTrack(vk, false);
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                if (isUp)
                {
                    _downVkTicks.Remove(vk);
                }
                else if (isDown)
                {
                    if (_paused)
                    {
                        _downVkTicks.Clear();
                        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    int now = Environment.TickCount;
                    // Refresh modifier states against AsyncKeyState cleanly so lost KEYUPs never stick modifiers
                    bool asyncCtrl = (NativeMethods.GetAsyncKeyState(0x11) & 0x8000) != 0;
                    bool asyncShift = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
                    bool asyncAlt = (NativeMethods.GetAsyncKeyState(0x12) & 0x8000) != 0;
                    bool asyncWin = ((NativeMethods.GetAsyncKeyState(0x5B) | NativeMethods.GetAsyncKeyState(0x5C)) & 0x8000) != 0;
                    if (!asyncCtrl && (_lCtrl || _rCtrl)) _lCtrl = _rCtrl = false;
                    if (!asyncShift && (_lShift || _rShift)) _lShift = _rShift = false;
                    if (!asyncAlt && (_lAlt || _rAlt)) _lAlt = _rAlt = false;
                    if (!asyncWin && (_lWin || _rWin)) _lWin = _rWin = false;

                    bool ctrl = asyncCtrl || _lCtrl || _rCtrl;
                    bool shift = asyncShift || _lShift || _rShift;
                    bool alt = asyncAlt || _lAlt || _rAlt || ((kb.flags & 0x20 /* LLKHF_ALTDOWN */) != 0);
                    bool win = asyncWin || _lWin || _rWin;

                    bool isRepeat = _downVkTicks.TryGetValue(vk, out int prevTick) && unchecked(now - prevTick) < 900;
                    _downVkTicks[vk] = now;

                    if (!isRepeat)
                    {
                        string matchedTask = null;
                        lock (_bindings)
                        {
                            for (int i = 0; i < _bindings.Count; i++)
                            {
                                var b = _bindings[i];
                                if (b.Vk == vk && b.Ctrl == ctrl && b.Alt == alt && b.Shift == shift && b.Win == win)
                                {
                                    matchedTask = b.Task;
                                    break;
                                }
                            }
                        }

                        if (matchedTask != null)
                        {
                            string taskCopy = matchedTask;
                            DispatchTaskDeduped(taskCopy);
                            return (IntPtr)1;
                        }
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            UnregisterAllOsHotkeys();
            _hotkeyWindow?.Dispose();
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }
}

