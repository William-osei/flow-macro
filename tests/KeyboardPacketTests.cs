using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using FarmMacro;

internal static class KeyboardPacketTests
{
    static object Packet(NativeInput input, string key)
    {
        return typeof(NativeInput).GetMethod("CreateDownPacket", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, new object[] { key });
    }
    static object Keyboard(object packet) { return packet.GetType().GetField("data").GetValue(packet).GetType().GetField("keyboard").GetValue(packet.GetType().GetField("data").GetValue(packet)); }
    static uint Flags(object keyboard) { return (uint)keyboard.GetType().GetField("dwFlags").GetValue(keyboard); }
    static ushort Key(object keyboard) { return (ushort)keyboard.GetType().GetField("wVk").GetValue(keyboard); }
    static ushort Scan(object keyboard) { return (ushort)keyboard.GetType().GetField("wScan").GetValue(keyboard); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class ExpectedKey
    {
        public string Name; public ushort VirtualKey, ScanCode; public bool Extended;
        public ExpectedKey(string name, ushort key, ushort scan, bool extended) { Name = name; VirtualKey = key; ScanCode = scan; Extended = extended; }
    }
    static readonly ExpectedKey[] addedKeys = {
        new ExpectedKey("Backspace", 0x08, 0x0e, false),
        new ExpectedKey("Delete", 0x2e, 0x53, true), new ExpectedKey("Insert", 0x2d, 0x52, true),
        new ExpectedKey("Home", 0x24, 0x47, true), new ExpectedKey("End", 0x23, 0x4f, true),
        new ExpectedKey("PageUp", 0x21, 0x49, true), new ExpectedKey("PageDown", 0x22, 0x51, true),
        new ExpectedKey("F1", 0x70, 0x3b, false), new ExpectedKey("F2", 0x71, 0x3c, false),
        new ExpectedKey("F3", 0x72, 0x3d, false), new ExpectedKey("F4", 0x73, 0x3e, false),
        new ExpectedKey("F5", 0x74, 0x3f, false), new ExpectedKey("F6", 0x75, 0x40, false),
        new ExpectedKey("F7", 0x76, 0x41, false), new ExpectedKey("F12", 0x7b, 0x58, false),
        new ExpectedKey("NumPad0", 0x60, 0x52, false), new ExpectedKey("NumPad1", 0x61, 0x4f, false),
        new ExpectedKey("NumPad2", 0x62, 0x50, false), new ExpectedKey("NumPad3", 0x63, 0x51, false),
        new ExpectedKey("NumPad4", 0x64, 0x4b, false), new ExpectedKey("NumPad5", 0x65, 0x4c, false),
        new ExpectedKey("NumPad6", 0x66, 0x4d, false), new ExpectedKey("NumPad7", 0x67, 0x47, false),
        new ExpectedKey("NumPad8", 0x68, 0x48, false), new ExpectedKey("NumPad9", 0x69, 0x49, false),
        new ExpectedKey("Add", 0x6b, 0x4e, false), new ExpectedKey("Subtract", 0x6d, 0x4a, false),
        new ExpectedKey("Multiply", 0x6a, 0x37, false), new ExpectedKey("Divide", 0x6f, 0x35, true),
        new ExpectedKey("Decimal", 0x6e, 0x53, false)
    };
    public static int Main()
    {
        try
        {
            NativeInput input = new NativeInput();
            Check(input.KeyboardMode == KeyboardInputMode.VirtualKey, "Virtual keys must be the default compatibility mode.");
            object c = Keyboard(Packet(input, "C"));
            Check(Key(c) == 0x43 && (Flags(c) & 8) == 0 && Scan(c) != 0, "Standard C packet must use its virtual key and no scan-only flag.");
            Console.WriteLine("PASS Standard keyboard packets carry the expected virtual key");
            object arrow = Keyboard(Packet(input, "Up"));
            Check(Key(arrow) == 0x26 && (Flags(arrow) & 1) != 0, "Arrow keys retain the extended flag.");
            Console.WriteLine("PASS Extended keyboard flags are retained");
            input.KeyboardMode = KeyboardInputMode.ScanCode;
            object v = Keyboard(Packet(input, "V"));
            Check(Key(v) == 0 && Scan(v) != 0 && (Flags(v) & 8) != 0, "Scan mode must remain separate from virtual-key mode.");
            Console.WriteLine("PASS Scan-code compatibility mode remains available separately");
            Type packetType = typeof(NativeInput).GetNestedType("INPUT", BindingFlags.NonPublic);
            Check(Marshal.SizeOf(packetType) == (IntPtr.Size == 8 ? 40 : 28), "Native INPUT layout mismatch.");
            Console.WriteLine("PASS Native input layout matches the current architecture");
            foreach (KeyboardInputMode mode in new[] { KeyboardInputMode.VirtualKey, KeyboardInputMode.ScanCode })
            {
                input.KeyboardMode = mode;
                foreach (ExpectedKey expected in addedKeys)
                {
                    object keyboard = Keyboard(Packet(input, expected.Name));
                    uint flags = (mode == KeyboardInputMode.ScanCode ? 8u : 0u) | (expected.Extended ? 1u : 0u);
                    Check(Key(keyboard) == (mode == KeyboardInputMode.VirtualKey ? expected.VirtualKey : 0) && Scan(keyboard) == expected.ScanCode && Flags(keyboard) == flags,
                        expected.Name + " has wrong virtual-key, scan-code or extended flags in " + mode + " mode.");
                }
            }
            Console.WriteLine("PASS All 30 added keys have correct virtual-key and scan-code packets");
            HashSet<string> supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in NativeInput.SupportedInputs) Check(supported.Add(name), "Supported inputs must not contain duplicate keys: " + name);
            foreach (ExpectedKey expected in addedKeys)
                Check(NativeInput.NormalizeInput("  " + expected.Name.ToLowerInvariant() + "  ") == expected.Name, "New key names must normalize: " + expected.Name);
            foreach (string old in new[] { "C", "V", "Space", "Shift", "Ctrl", "Alt", "Tab", "Enter", "Escape", "Up", "Down", "Left", "Right", "Left click", "Right click", "Middle click" })
                Check(NativeInput.NormalizeInput(old) == old, "Existing key must remain supported: " + old);
            Console.WriteLine("PASS New key names normalize without replacing existing inputs or adding aliases");
            foreach (string reserved in new[] { "F8", "F9", "F10", "F11", "NumPadEnter", "F13", "NumPad10" })
            {
                Check(NativeInput.NormalizeInput(" " + reserved.ToLowerInvariant() + " ") == null, "Reserved or ambiguous input must be rejected: " + reserved);
                bool rejected = false;
                try { Packet(input, reserved); } catch (TargetInvocationException ex) { rejected = ex.InnerException is ArgumentException; }
                Check(rejected, "Packet generation must reject unsupported input: " + reserved);
            }
            Console.WriteLine("PASS Reserved F8-F11 and duplicate Enter aliases cannot generate packets");
            HashSet<string> pressed = new HashSet<string>();
            NativeInput readiness = new NativeInput(delegate(string key) { return pressed.Contains(key); });
            string reason;
            pressed.Add("V");
            Check(!readiness.CanSend(new MacroAction { Input = "V" }, out reason) && reason.Contains("release V"), "An externally held V must wait for release instead of counting another down.");
            pressed.Clear();
            Check(readiness.CanSend(new MacroAction { Input = "V" }, out reason), "V must become ready when the external key releases.");
            Console.WriteLine("PASS Already-held input waits for release before a new press");
            pressed.Add("Ctrl");
            Check(!readiness.CanSend(new MacroAction { Input = "V" }, out reason), "A physical shortcut modifier must postpone V.");
            IDictionary owned = (IDictionary)typeof(NativeInput).GetField("held", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(readiness);
            owned.Add("Ctrl", Packet(readiness, "Ctrl"));
            Check(readiness.CanSend(new MacroAction { Input = "V" }, out reason), "An intentionally recorded, macro-owned modifier must allow its chord.");
            Console.WriteLine("PASS Physical modifiers wait while intentional recorded chords remain valid");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
