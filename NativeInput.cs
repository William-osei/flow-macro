using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FarmMacro
{
    public enum KeyboardInputMode { VirtualKey, ScanCode }
    public interface IInputReadiness
    {
        bool CanSend(MacroAction action, out string reason);
    }
    public sealed class TargetWindow
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; }
        public string ProcessName { get; set; }
        public override string ToString() { return Title + "  (" + ProcessName + ")"; }
    }

    public sealed class NativeInput : IInputBackend, IRecordingSource, IInputReadiness
    {
        readonly Func<string, bool> readInputDown;
        public NativeInput() : this(ReadSystemInputDown) { }
        internal NativeInput(Func<string, bool> readInputDown)
        {
            if (readInputDown == null) throw new ArgumentNullException("readInputDown");
            this.readInputDown = readInputDown;
            KeyboardMode = KeyboardInputMode.VirtualKey;
        }
        public KeyboardInputMode KeyboardMode { get; set; }
        public static readonly string[] SupportedInputs = BuildInputs();
        // Store the exact down packet so a keyboard-layout change cannot alter key-up.
        private readonly Dictionary<string, INPUT> held = new Dictionary<string, INPUT>(StringComparer.OrdinalIgnoreCase);
        private IntPtr boundTarget;
        private uint boundPid;
        private long boundProcessStart;

        private static string[] BuildInputs()
        {
            List<string> result = new List<string>();
            for (char c = 'A'; c <= 'Z'; c++) result.Add(c.ToString());
            for (char c = '0'; c <= '9'; c++) result.Add(c.ToString());
            result.AddRange(new string[] { "Space", "Shift", "Ctrl", "Alt", "Tab", "Enter", "Escape", "Backspace", "Up", "Down", "Left", "Right", "Delete", "Insert", "Home", "End", "PageUp", "PageDown" });
            // F8-F11 belong to the app's record, play, emergency-stop and point hotkeys.
            for (char digit = '1'; digit <= '7'; digit++) result.Add("F" + digit);
            result.Add("F12");
            for (char digit = '0'; digit <= '9'; digit++) result.Add("NumPad" + digit);
            result.AddRange(new string[] { "Add", "Subtract", "Multiply", "Divide", "Decimal", "Left click", "Right click", "Middle click" });
            return result.ToArray();
        }

        public static string NormalizeInput(string input)
        {
            if (input == null) return null;
            foreach (string candidate in SupportedInputs)
                if (String.Equals(input.Trim(), candidate, StringComparison.OrdinalIgnoreCase)) return candidate;
            return null;
        }

        public static bool IsMouseInput(string input)
        {
            return input == "Left click" || input == "Right click" || input == "Middle click";
        }

        // Read only the current-down bit; the historical transition bit is shared
        // with other processes and is unsuitable for a reliable emergency stop.
        public static bool IsEmergencyStopPressed()
        {
            return (GetAsyncKeyState(0x79) & 0x8000) != 0;
        }
        public bool CanSend(MacroAction action, out string reason)
        {
            foreach (string modifier in new string[] { "Ctrl", "Alt", "Shift" })
            {
                if (readInputDown(modifier) && !held.ContainsKey(modifier))
                {
                    reason = "Waiting - release Ctrl, Alt and Shift before macro input";
                    return false;
                }
            }
            if (!held.ContainsKey(action.Input) && readInputDown(action.Input))
            {
                reason = "Waiting - release " + action.Input + " before macro input";
                return false;
            }
            reason = null;
            return true;
        }

        static bool ReadSystemInputDown(string input)
        {
            uint key = input == "Left click" ? 0x01u : input == "Right click" ? 0x02u : input == "Middle click" ? 0x04u : VirtualKey(input);
            return (GetAsyncKeyState((int)key) & 0x8000) != 0;
        }

        public HashSet<string> ReadPressedInputs()
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!IsTargetForeground(boundTarget)) return result;
            foreach (string input in SupportedInputs)
            {
                uint key;
                if (input == "Left click") key = 0x01;
                else if (input == "Right click") key = 0x02;
                else if (input == "Middle click") key = 0x04;
                else key = VirtualKey(input);
                if ((GetAsyncKeyState((int)key) & 0x8000) != 0) result.Add(input);
            }
            if (!IsTargetForeground(boundTarget)) result.Clear();
            return result;
        }

        public bool TryGetMousePosition(IntPtr target, out int x, out int y)
        {
            return GetClientCursor(target, out x, out y) && IsClientPointSafe(target, x, y);
        }

        public static bool IsClientPointSafe(IntPtr target, int x, int y)
        {
            RECT client;
            if (!IsWindow(target) || !GetClientRect(target, out client) || x < 0 || y < 0 || x >= client.Right || y >= client.Bottom) return false;
            POINT point = new POINT { X = x, Y = y };
            if (!ClientToScreen(target, ref point)) return false;
            IntPtr hit = WindowFromPoint(point);
            return hit == target || GetAncestor(hit, 2) == target;
        }

        public static List<TargetWindow> ListTargets()
        {
            List<TargetWindow> result = new List<TargetWindow>();
            uint ownPid = (uint)Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr hwnd, IntPtr ignored)
            {
                if (!IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) == 0) return true;
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == ownPid) return true;
                StringBuilder title = new StringBuilder(GetWindowTextLength(hwnd) + 1);
                GetWindowText(hwnd, title, title.Capacity);
                try
                {
                    using (Process process = Process.GetProcessById((int)pid))
                        result.Add(new TargetWindow { Handle = hwnd, Title = title.ToString(), ProcessName = process.ProcessName });
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                return true;
            }, IntPtr.Zero);
            result.Sort(delegate(TargetWindow a, TargetWindow b) { return StringComparer.OrdinalIgnoreCase.Compare(a.Title, b.Title); });
            return result;
        }

        public void BindTarget(IntPtr target)
        {
            if (!IsWindow(target) || !IsWindowVisible(target)) throw new ArgumentException("The target window is no longer available. Refresh the list.");
            uint pid;
            GetWindowThreadProcessId(target, out pid);
            if (pid == 0) throw new ArgumentException("Cannot identify the target process.");
            string targetName = "The selected app";
            try
            {
                using (Process process = Process.GetProcessById((int)pid))
                    targetName = process.ProcessName.StartsWith("Roblox", StringComparison.OrdinalIgnoreCase) ? "Roblox" : process.ProcessName;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            InputPermissions.RequireCompatible(pid, targetName);
            long processStart = GetProcessStart(pid);
            boundTarget = target;
            boundPid = pid;
            boundProcessStart = processStart;
        }

        private static long GetProcessStart(uint pid)
        {
            try { using (Process process = Process.GetProcessById((int)pid)) return process.StartTime.ToUniversalTime().Ticks; }
            catch (ArgumentException) { throw new ArgumentException("The target process has closed."); }
            catch (InvalidOperationException) { throw new ArgumentException("The target process has closed."); }
            catch (Win32Exception) { throw new ArgumentException("Cannot verify the target process identity."); }
        }

        public bool IsTargetForeground(IntPtr target)
        {
            if (target == IntPtr.Zero || target != boundTarget || GetForegroundWindow() != target || !IsWindow(target) || !IsWindowVisible(target)) return false;
            uint pid;
            GetWindowThreadProcessId(target, out pid);
            if (pid != boundPid) return false;
            try { return GetProcessStart(pid) == boundProcessStart; }
            catch (ArgumentException) { return false; }
        }

        public static bool GetClientCursor(IntPtr target, out int x, out int y)
        {
            x = 0;
            y = 0;
            POINT point;
            RECT client;
            if (!IsWindow(target) || !GetCursorPos(out point)) return false;
            if (!ScreenToClient(target, ref point) || !GetClientRect(target, out client)) return false;
            if (point.X < 0 || point.Y < 0 || point.X >= client.Right || point.Y >= client.Bottom) return false;
            x = point.X;
            y = point.Y;
            return true;
        }

        public bool Press(IntPtr target, MacroAction action)
        {
            if (!IsTargetForeground(target)) return false;
            if (held.ContainsKey(action.Input)) throw new InvalidOperationException("An input is already held.");
            if (IsMouseInput(action.Input))
            {
                POINT screen;
                RECT client;
                if (!GetClientRect(target, out client)) return false;
                if (action.X.HasValue)
                {
                    if (!action.Y.HasValue || action.X.Value < 0 || action.Y.Value < 0 || action.X.Value >= client.Right || action.Y.Value >= client.Bottom) return false;
                    screen = new POINT { X = action.X.Value, Y = action.Y.Value };
                    if (!ClientToScreen(target, ref screen)) return false;
                }
                else
                {
                    if (!GetCursorPos(out screen)) return false;
                    POINT relative = screen;
                    if (!ScreenToClient(target, ref relative) || relative.X < 0 || relative.Y < 0 || relative.X >= client.Right || relative.Y >= client.Bottom) return false;
                }
                // Reject other windows/overlays even when the point lies in target bounds.
                IntPtr hit = WindowFromPoint(screen);
                if (hit != target && GetAncestor(hit, 2) != target) return false;
                if (!IsTargetForeground(target)) return false;
                if (action.X.HasValue && !SetCursorPos(screen.X, screen.Y)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not position the pointer.");
                if (!IsTargetForeground(target)) return false;
            }
            INPUT packet = CreateDownPacket(action.Input);
            Send(packet);
            held.Add(action.Input, packet);
            return true;
        }

        public void Release(string input)
        {
            INPUT packet;
            if (!held.TryGetValue(input, out packet)) return;
            if (packet.type == 0) packet.data.mouse.dwFlags *= 2;
            else packet.data.keyboard.dwFlags |= 0x0002u;
            Send(packet);
            held.Remove(input);
        }

        public void ReleaseAll()
        {
            Exception failure = null;
            foreach (string input in new List<string>(held.Keys))
            {
                try { Release(input); }
                catch (Exception ex) { if (failure == null) failure = ex; }
            }
            if (failure != null) throw failure;
        }

        private INPUT CreateDownPacket(string input)
        {
            INPUT packet = new INPUT();
            if (IsMouseInput(input))
            {
                packet.type = 0;
                uint downFlag = input == "Left click" ? 0x0002u : input == "Right click" ? 0x0008u : 0x0020u;
                packet.data.mouse.dwFlags = downFlag;
            }
            else
            {
                uint key = VirtualKey(input);
                uint scan = MapVirtualKey(key, 4);
                if (scan == 0) throw new InvalidOperationException("The keyboard layout does not support " + input + ".");
                packet.type = 1;
                packet.data.keyboard.wScan = (ushort)(scan & 0xff);
                packet.data.keyboard.wVk = KeyboardMode == KeyboardInputMode.VirtualKey ? (ushort)key : (ushort)0;
                bool extended = (scan & 0xff00) != 0 || input == "Up" || input == "Down" || input == "Left" || input == "Right" ||
                    input == "Delete" || input == "Insert" || input == "Home" || input == "End" || input == "PageUp" || input == "PageDown" || input == "Divide";
                packet.data.keyboard.dwFlags = (KeyboardMode == KeyboardInputMode.ScanCode ? 0x0008u : 0) | (extended ? 0x0001u : 0);
            }
            return packet;
        }

        private static void Send(INPUT packet)
        {
            INPUT[] packets = new INPUT[] { packet };
            if (SendInput(1, packets, Marshal.SizeOf(typeof(INPUT))) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not accept input. Check focus and privilege level.");
        }

        private static uint VirtualKey(string input)
        {
            if (input.Length == 1) return input[0];
            if (input.Length == 2 && input[0] == 'F' && input[1] >= '1' && input[1] <= '7') return 0x70u + (uint)(input[1] - '1');
            if (input == "F12") return 0x7b;
            if (input.Length == 7 && input.StartsWith("NumPad", StringComparison.Ordinal) && input[6] >= '0' && input[6] <= '9')
                return 0x60u + (uint)(input[6] - '0');
            switch (input)
            {
                case "Space": return 0x20;
                case "Shift": return 0x10;
                case "Ctrl": return 0x11;
                case "Alt": return 0x12;
                case "Tab": return 0x09;
                case "Enter": return 0x0d;
                case "Escape": return 0x1b;
                case "Backspace": return 0x08;
                case "Up": return 0x26;
                case "Down": return 0x28;
                case "Left": return 0x25;
                case "Right": return 0x27;
                case "Delete": return 0x2e;
                case "Insert": return 0x2d;
                case "Home": return 0x24;
                case "End": return 0x23;
                case "PageUp": return 0x21;
                case "PageDown": return 0x22;
                case "Add": return 0x6b;
                case "Subtract": return 0x6d;
                case "Multiply": return 0x6a;
                case "Divide": return 0x6f;
                case "Decimal": return 0x6e;
                default: throw new ArgumentException("Unsupported input.");
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION data; }
        [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
        {
            public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
        {
            public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }
}
