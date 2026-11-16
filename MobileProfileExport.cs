using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace FarmMacro
{
    public sealed class MobileTapDraft
    {
        public string Format = "FarmMacro.Mobile";
        public int Version = 1;
        public string Name;
        public string TargetPackage = "dev.farmmacro.app";
        public int StartDelayMs;
        public int SessionLimitMs;
        public int ScreenWidth;
        public int ScreenHeight;
        public List<MobileTapAction> Actions = new List<MobileTapAction>();
    }
    public sealed class MobileTapAction
    {
        public string Label;
        public bool Enabled;
        public double? X;
        public double? Y;
        public int IntervalMs;
        public int HoldMs;
        public int Burst;
        public int OffsetMs;
    }
    public static class MobileProfileExport
    {
        public static MobileTapDraft CreateDraft(MacroWorkspaceProfile workspace)
        {
            if (workspace == null || workspace.Timers == null || workspace.Timers.Actions == null)
                throw new ArgumentException("The current timer setup is empty.");
            SavedProfile timers = workspace.Timers;
            if (timers.Version != 1) throw new ArgumentException("Unsupported timer profile version.");
            if (timers.Actions.Count > 32) throw new ArgumentException("Android supports up to 32 tap actions. Remove extra timer rows first.");
            if (timers.CountdownSeconds < 1 || timers.CountdownSeconds > 30 || timers.SessionSeconds < 0 || timers.SessionSeconds > 86400)
                throw new ArgumentException("Check the start delay and timed stop.");
            string name = workspace.Name;
            if (name != null)
                foreach (char character in name)
                    if (Char.IsControl(character)) throw new ArgumentException("Profile names cannot contain control characters.");
            name = String.IsNullOrWhiteSpace(name) ? "My mobile routine" : name.Trim();
            if (name.Length < 1 || name.Length > 60) throw new ArgumentException("Use a profile name of 1 to 60 characters.");
            MobileTapDraft draft = new MobileTapDraft {
                Name = name,
                StartDelayMs = timers.CountdownSeconds * 1000, SessionLimitMs = timers.SessionSeconds * 1000
            };
            int enabled = 0;
            foreach (MacroAction action in timers.Actions)
            {
                if (action == null || NativeInput.NormalizeInput(action.Input) == null) throw new ArgumentException("Choose a supported input for every timer row.");
                if (action.IntervalMs < 150 || action.IntervalMs > 86400000 || action.HoldMs < 10 || action.HoldMs > 1000 || action.HoldMs > action.IntervalMs ||
                    action.Burst < 1 || action.Burst > 20 || action.OffsetMs < 0 || action.OffsetMs > 86400000)
                    throw new ArgumentException("Android needs intervals of at least 0.15 sec, holds of 10 to 1000 ms, and 1 to 20 repeats. Adjust the timer rows first.");
                if (action.Enabled) enabled++;
                draft.Actions.Add(new MobileTapAction { Label = NativeInput.NormalizeInput(action.Input), Enabled = action.Enabled, IntervalMs = action.IntervalMs, HoldMs = action.HoldMs, Burst = action.Burst, OffsetMs = action.OffsetMs });
            }
            if (enabled == 0) throw new ArgumentException("Enable at least one timer action before exporting.");
            return draft;
        }
        public static void Export(MacroWorkspaceProfile workspace, string path)
        {
            MobileTapDraft draft = CreateDraft(workspace);
            string destination = Path.GetFullPath(path);
            string folder = Path.GetDirectoryName(destination);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing destination folder.");
            string json = new JavaScriptSerializer().Serialize(draft);
            string temporary = Path.Combine(folder, ".farmmacro-mobile-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
