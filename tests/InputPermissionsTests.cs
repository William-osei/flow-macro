using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FarmMacro;

internal static class InputPermissionsTests
{
    const uint Low = 0x1000, Medium = 0x2000, MediumPlus = 0x2100, High = 0x3000, SystemLevel = 0x4000;
    static int passed;

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetProcessHandleCount(IntPtr process, out uint count);

    static void Check(bool value, string message) { if (!value) throw new Exception("FAILED: " + message); }
    static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    static bool Contains(string value, string text) { return value != null && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0; }

    public static int Main()
    {
        try
        {
            Run("Equal integrity permits ordinary input", delegate {
                foreach (uint level in new[] { 0u, Low, Medium, MediumPlus, High, SystemLevel })
                    Check(InputPermissions.Compare(level, level) == InputPermissionDecision.Allowed, "equal level " + level);
            });
            Run("Lower-integrity targets remain permitted", delegate {
                Check(InputPermissions.Compare(High, Medium) == InputPermissionDecision.Allowed, "high to medium");
                Check(InputPermissions.Compare(Medium, Low) == InputPermissionDecision.Allowed, "medium to low");
                Check(InputPermissions.Compare(Low, 0u) == InputPermissionDecision.Allowed, "untrusted zero is a known level");
            });
            Run("Higher target integrity is detected including Medium Plus", delegate {
                Check(InputPermissions.Compare(Medium, High) == InputPermissionDecision.TargetElevated, "medium cannot inject high");
                Check(InputPermissions.Compare(Medium, MediumPlus) == InputPermissionDecision.TargetElevated, "medium plus is higher than medium");
                Check(InputPermissions.Compare(High, SystemLevel) == InputPermissionDecision.TargetElevated, "system is higher than high");
                Check(InputPermissions.Compare(0u, Low) == InputPermissionDecision.TargetElevated, "untrusted cannot inject low");
            });
            Run("Unknown integrity never silently passes as equal", delegate {
                Check(InputPermissions.Compare(null, Medium) == InputPermissionDecision.Unknown, "unknown current process");
                Check(InputPermissions.Compare(Medium, null) == InputPermissionDecision.Unknown, "unknown target");
                Check(InputPermissions.Compare(null, null) == InputPermissionDecision.Unknown, "both unknown");
                Check(InputPermissions.Compare(0u, null) == InputPermissionDecision.Unknown, "known zero does not conceal an unknown target");
            });
            Run("Comparison uses unsigned levels without subtraction overflow", delegate {
                Check(InputPermissions.Compare(0u, UInt32.MaxValue) == InputPermissionDecision.TargetElevated, "large target level");
                Check(InputPermissions.Compare(UInt32.MaxValue, 0u) == InputPermissionDecision.Allowed, "large own level");
            });
            Run("Permission failure explains the mismatch and manual remedies", delegate {
                string elevated = InputPermissions.GetFailureMessage(InputPermissionDecision.TargetElevated, "Roblox");
                Check(Contains(elevated, "Roblox") && Contains(elevated, "administrator"), "identify target privilege");
                Check(Contains(elevated, "close") && Contains(elevated, "relaunch"), "explain user-controlled restart options");
                string unknown = InputPermissions.GetFailureMessage(InputPermissionDecision.Unknown, "Roblox");
                Check(Contains(unknown, "cannot") && Contains(unknown, "verify"), "unknown must be explicit");
                Check(InputPermissions.GetFailureMessage(InputPermissionDecision.Allowed, "Roblox") == null, "allowed has no failure");
            });
            Run("Read-only production query retrieves current process integrity", delegate {
                uint level; string error;
                using (Process current = Process.GetCurrentProcess()) {
                    Check(InputPermissions.TryGetIntegrityLevel((uint)current.Id, out level, out error), "own query failed: " + error);
                }
                Check(String.IsNullOrEmpty(error), "successful query leaves no error");
                Check(InputPermissions.Compare(level, level) == InputPermissionDecision.Allowed, "queried level is usable");
            });
            Run("Invalid process IDs return clear unknown-query failures", delegate {
                foreach (uint pid in new[] { 0u, UInt32.MaxValue }) {
                    uint level; string error;
                    Check(!InputPermissions.TryGetIntegrityLevel(pid, out level, out error), "invalid PID must fail: " + pid);
                    Check(!String.IsNullOrWhiteSpace(error), "failed query needs a readable error");
                }
            });
            Run("Repeated token queries release process and token handles", delegate {
                uint pid;
                using (Process current = Process.GetCurrentProcess()) pid = (uint)current.Id;
                uint level; string error;
                for (int index = 0; index < 8; index++)
                    Check(InputPermissions.TryGetIntegrityLevel(pid, out level, out error), "query warmup failed: " + error);
                uint before, after;
                Check(GetProcessHandleCount(GetCurrentProcess(), out before), "cannot read initial handle count");
                for (int index = 0; index < 128; index++) {
                    Check(InputPermissions.TryGetIntegrityLevel(pid, out level, out error), "repeated own query failed: " + error);
                    Check(!InputPermissions.TryGetIntegrityLevel(UInt32.MaxValue, out level, out error), "invalid repeated query unexpectedly succeeded");
                }
                Check(GetProcessHandleCount(GetCurrentProcess(), out after), "cannot read final handle count");
                // Permit a few lazy runtime handles while detecting a process or
                // token handle leaked by each query. No finalizer collection is
                // forced here, so deterministic cleanup is exercised.
                Check(after <= before + 4, "query handles increased from " + before + " to " + after);
            });
            Console.WriteLine(passed + " input permission tests passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
