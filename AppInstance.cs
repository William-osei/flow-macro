using System;
using System.Runtime.InteropServices;

namespace FarmMacro
{
    internal static class AppInstance
    {
        internal static readonly uint ShowMessage = RegisterWindowMessage("FarmMacro.ShowMainWindow.v1");
        const string WindowProperty = "FarmMacro.MainWindow.v1";

        internal static void RegisterWindow(IntPtr window)
        {
            if (window != IntPtr.Zero) SetProp(window, WindowProperty, new IntPtr(1));
        }
        internal static void UnregisterWindow(IntPtr window)
        {
            if (window != IntPtr.Zero) RemoveProp(window, WindowProperty);
        }

        internal static void ShowExisting()
        {
            if (ShowMessage == 0) return;
            EnumWindows(delegate(IntPtr window, IntPtr ignored)
            {
                if (GetProp(window, WindowProperty) == IntPtr.Zero) return true;
                uint pid;
                GetWindowThreadProcessId(window, out pid);
                if (pid != 0) AllowSetForegroundWindow(pid);
                PostMessage(window, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                return false;
            }, IntPtr.Zero);
        }

        delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr window, string property, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window, string property);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr window, string property);
    }
}
