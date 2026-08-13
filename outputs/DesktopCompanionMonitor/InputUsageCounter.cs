using System.Runtime.InteropServices;

namespace PcCompanionMonitor;

internal sealed class InputUsageCounter : IDisposable
{
    private readonly InputUsageStore _store;
    private readonly HashSet<uint> _pressedKeys = [];
    private HookProc? _mouseProc;
    private HookProc? _keyboardProc;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;

    public InputUsageCounter(InputUsageStore store) => _store = store;

    public void Start()
    {
        if (_mouseHook != IntPtr.Zero || _keyboardHook != IntPtr.Zero) return;
        _mouseProc = MouseCallback;
        _keyboardProc = KeyboardCallback;
        IntPtr module = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(14, _mouseProc, module, 0);
        _keyboardHook = SetWindowsHookEx(13, _keyboardProc, module, 0);
    }

    public void Dispose()
    {
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = IntPtr.Zero;
        _keyboardHook = IntPtr.Zero;
        _pressedKeys.Clear();
    }

    private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == 0x201) _store.AddLeftClick();
            else if (msg == 0x204) _store.AddRightClick();
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private IntPtr KeyboardCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = wParam.ToInt32();
            uint virtualKey = (uint)Marshal.ReadInt32(lParam);
            if (msg is 0x100 or 0x104)
            {
                if (_pressedKeys.Add(virtualKey))
                {
                    _store.AddKeyboardPress(GetKeyCategory(virtualKey));
                }
            }
            else if (msg is 0x101 or 0x105)
            {
                _pressedKeys.Remove(virtualKey);
            }
        }
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private static KeyCategory GetKeyCategory(uint virtualKey)
    {
        KeyCategory category = KeyCategory.None;
        if (virtualKey is 0x57 or 0x41 or 0x53 or 0x44)
        {
            category |= KeyCategory.Wasd;
        }
        if (virtualKey is 0x51 or 0x57 or 0x45 or 0x52)
        {
            category |= KeyCategory.Qwer;
        }
        if (virtualKey is 0x10 or 0xA0 or 0xA1)
        {
            category |= KeyCategory.Shift;
        }
        if (virtualKey is 0x11 or 0xA2 or 0xA3)
        {
            category |= KeyCategory.Ctrl;
        }
        if (virtualKey == 0x09)
        {
            category |= KeyCategory.Tab;
        }
        return category;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);
}
