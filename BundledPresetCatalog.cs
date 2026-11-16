using System;
using System.Collections.Generic;
using System.IO;

namespace FarmMacro
{
    /// <summary>A validated, read-only index of portable profiles beside the app.</summary>
    public static class BundledPresetCatalog
    {
        public sealed class Entry
        {
            public string Name { get; private set; }
            public string FilePath { get; private set; }
            public string Summary { get; private set; }
            internal Entry(string path, MacroWorkspaceProfile profile)
            {
                FilePath = path;
                Name = profile.Name;
                List<string> inputs = new List<string>();
                int enabled = 0;
                foreach (MacroAction action in profile.Timers.Actions)
                    if (action.Enabled) { enabled++; if (inputs.Count < 3) inputs.Add(action.Input); }
                string timerInputs = inputs.Count == 0 ? "No enabled timers" : String.Join(", ", inputs.ToArray());
                if (enabled > inputs.Count) timerInputs += " +" + (enabled - inputs.Count) + " more";
                Summary = "Mouse: " + profile.Clicker.Input + "  \u2022  " + (profile.Clicker.MaxClicks == 0 ? "No click cap" : profile.Clicker.MaxClicks + " click cap") + "\r\n" +
                    "Timers: " + timerInputs + "  \u2022  " + (profile.Timers.SessionSeconds == 0 ? "Unlimited timer session" : profile.Timers.SessionSeconds + " sec timer session") + "\r\n" +
                    "Recording: " + profile.Sequence.Steps.Count + " events  \u2022  " + (profile.Sequence.RepeatCount == 0 ? "Unlimited repeats" : profile.Sequence.RepeatCount + " repeats");
            }
            public override string ToString() { return Name; }
        }

        public static List<Entry> Read(string folder, out int skipped)
        {
            if (String.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Choose a presets folder.");
            string full = Path.GetFullPath(folder);
            skipped = 0;
            List<Entry> entries = new List<Entry>();
            if (!Directory.Exists(full)) return entries;
            string[] files = Directory.GetFiles(full, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                try { entries.Add(new Entry(file, ProfileTransfer.Import(file))); }
                catch (InvalidDataException) { skipped++; }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
                catch (ArgumentException) { skipped++; }
            }
            entries.Sort(delegate(Entry left, Entry right)
            {
                int names = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
                return names != 0 ? names : StringComparer.OrdinalIgnoreCase.Compare(left.FilePath, right.FilePath);
            });
            return entries;
        }
    }
}
