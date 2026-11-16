using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FarmMacro
{
    public enum InputPermissionDecision { Unknown, Allowed, TargetElevated }

    public sealed class InputPermissionException : InvalidOperationException
    {
        public string Headline { get; private set; }
        public InputPermissionException(string headline, string message) : base(message) { Headline = headline; }
    }

    // Read-only token queries. This class never elevates a process or changes security settings.
    public static class InputPermissions
    {
        const uint ProcessQueryLimitedInformation = 0x1000;
        const uint TokenQuery = 0x0008;
        const int TokenIntegrityLevel = 25;
        const int ErrorInsufficientBuffer = 122;
        const uint HighIntegrity = 0x3000;

        public static InputPermissionDecision Compare(uint? ownLevel, uint? targetLevel)
        {
            if (!ownLevel.HasValue || !targetLevel.HasValue) return InputPermissionDecision.Unknown;
            return targetLevel.Value > ownLevel.Value ? InputPermissionDecision.TargetElevated : InputPermissionDecision.Allowed;
        }

        public static string GetFailureMessage(InputPermissionDecision decision, string targetName)
        {
            string target = String.IsNullOrWhiteSpace(targetName) ? "The selected app" : targetName;
            if (decision == InputPermissionDecision.Allowed) return null;
            if (decision == InputPermissionDecision.TargetElevated)
                return target + " has higher Windows privileges than Farm Macro. Close Farm Macro, then use Start Farm Macro as Administrator.cmd or right-click FarmMacro.exe and choose Run as administrator. You can instead relaunch the game without administrator privileges. No input was sent.";
            return "Cannot verify the Windows privileges of Farm Macro and " + target + ". Starting is blocked, and no input was sent. Close Farm Macro and try again, or relaunch the game without administrator privileges. Check that the selected app is still open.";
        }

        public static void RequireCompatible(uint targetProcessId, string targetName)
        {
            uint ownLevel, targetLevel;
            string ownError, targetError;
            uint ownProcessId;
            using (Process current = Process.GetCurrentProcess()) ownProcessId = (uint)current.Id;
            bool ownKnown = TryGetIntegrityLevel(ownProcessId, out ownLevel, out ownError);
            bool targetKnown = TryGetIntegrityLevel(targetProcessId, out targetLevel, out targetError);
            InputPermissionDecision decision = Compare(ownKnown ? (uint?)ownLevel : null, targetKnown ? (uint?)targetLevel : null);
            if (decision == InputPermissionDecision.Allowed) return;
            string message = GetFailureMessage(decision, targetName);
            string headline = decision == InputPermissionDecision.Unknown ? "Cannot verify input permissions - starting is blocked." : "Permission mismatch: target has higher Windows privileges.";
            if (decision == InputPermissionDecision.TargetElevated && targetLevel >= HighIntegrity && ownLevel < HighIntegrity)
            {
                headline = "Permission mismatch: game is administrator; Farm Macro is standard.";
                string target = String.IsNullOrWhiteSpace(targetName) ? "The selected app" : targetName;
                message = target + " has administrator privileges. " + message.Substring(message.IndexOf("Close Farm Macro", StringComparison.Ordinal));
            }
            if (!ownKnown) message += "\n\nFarm Macro query: " + ownError;
            if (!targetKnown) message += "\n\nTarget query: " + targetError;
            throw new InputPermissionException(headline, message);
        }

        public static bool TryGetIntegrityLevel(uint processId, out uint integrityLevel, out string error)
        {
            integrityLevel = 0; error = null;
            if (processId == 0) { error = "Invalid process identifier."; return false; }
            IntPtr process = IntPtr.Zero, token = IntPtr.Zero, information = IntPtr.Zero;
            try
            {
                process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
                if (process == IntPtr.Zero) { error = QueryError("Cannot open process", Marshal.GetLastWin32Error()); return false; }
                if (!OpenProcessToken(process, TokenQuery, out token)) { error = QueryError("Cannot read process token", Marshal.GetLastWin32Error()); return false; }
                uint length;
                bool sized = GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out length);
                int sizingError = Marshal.GetLastWin32Error();
                if ((!sized && sizingError != ErrorInsufficientBuffer) || length < IntPtr.Size + 4 || length > 65536)
                { error = QueryError("Cannot size integrity information", sizingError); return false; }
                information = Marshal.AllocHGlobal((int)length);
                uint returned;
                if (!GetTokenInformation(token, TokenIntegrityLevel, information, length, out returned))
                { error = QueryError("Cannot read integrity information", Marshal.GetLastWin32Error()); return false; }
                if (returned < IntPtr.Size + 4 || returned > length) { error = "Invalid integrity information size."; return false; }
                // TOKEN_MANDATORY_LABEL begins with SID_AND_ATTRIBUTES; its first field is the SID pointer.
                IntPtr sid = Marshal.ReadIntPtr(information);
                if (sid == IntPtr.Zero || !IsValidSid(sid)) { error = "Invalid integrity identifier."; return false; }
                IntPtr countPointer = GetSidSubAuthorityCount(sid);
                if (countPointer == IntPtr.Zero) { error = "Missing integrity identifier components."; return false; }
                byte count = Marshal.ReadByte(countPointer);
                if (count == 0) { error = "Empty integrity identifier."; return false; }
                IntPtr relativeId = GetSidSubAuthority(sid, (uint)(count - 1));
                if (relativeId == IntPtr.Zero) { error = "Missing integrity level."; return false; }
                integrityLevel = unchecked((uint)Marshal.ReadInt32(relativeId));
                return true;
            }
            finally
            {
                if (information != IntPtr.Zero) Marshal.FreeHGlobal(information);
                if (token != IntPtr.Zero) CloseHandle(token);
                if (process != IntPtr.Zero) CloseHandle(process);
            }
        }

        static string QueryError(string operation, int code) { return operation + ": " + new Win32Exception(code).Message + " (" + code + ")."; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, uint length, out uint returnedLength);
        [DllImport("advapi32.dll")] static extern bool IsValidSid(IntPtr sid);
        [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    }
}
