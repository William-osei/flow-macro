using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using FarmMacro;

internal static class MobileProfileExportTests
{
    static readonly string root = Path.Combine(Path.GetTempPath(), "FarmMacroMobileTests-" + Guid.NewGuid().ToString("N"));
    static int passed;
    static string FilePath() { return Path.Combine(root, Guid.NewGuid().ToString("N") + ".json"); }
    static MacroWorkspaceProfile Profile()
    {
        return new MacroWorkspaceProfile {
            Name = "  Café routine  ",
            Timers = new SavedProfile { CountdownSeconds = 3, SessionSeconds = 0, Actions = new List<MacroAction> {
                new MacroAction { Enabled = true, Input = " c ", IntervalMs = 11000, HoldMs = 500, Burst = 1 },
                new MacroAction { Enabled = false, Input = "X", IntervalMs = 15000, HoldMs = 70, Burst = 2, OffsetMs = 2700 },
                new MacroAction { Enabled = true, Input = "Left click", IntervalMs = 1000, HoldMs = 60, Burst = 1, X = 123, Y = 456, OffsetMs = 1300 }
            } },
            // A mobile timer draft must not validate an unrelated sequence editor.
            Sequence = new SavedSequence { Version = 99, Steps = null }
        };
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception("FAILED: " + message); }
    static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    static bool Invalid(MacroWorkspaceProfile profile)
    {
        try { MobileProfileExport.CreateDraft(profile); return false; } catch (ArgumentException) { return true; }
    }
    static Dictionary<string, object> Read(string path)
    {
        return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
    }
    public static int Main()
    {
        Directory.CreateDirectory(root);
        try
        {
            Run("Mobile format preserves independent timers and unlimited sessions", delegate {
                MobileTapDraft draft = MobileProfileExport.CreateDraft(Profile());
                Check(draft.Format == "FarmMacro.Mobile" && draft.Version == 1 && draft.TargetPackage == "dev.farmmacro.app", "mobile drafts open in the general tap test pad until a target is chosen");
                Check(draft.Name == "Café routine" && draft.StartDelayMs == 3000 && draft.SessionLimitMs == 0, "name and unlimited settings");
                Check(draft.Actions.Count == 3 && draft.Actions[0].Label == "C" && draft.Actions[0].IntervalMs == 11000 && draft.Actions[0].HoldMs == 500, "canonical label and timing");
                Check(!draft.Actions[1].Enabled && draft.Actions[1].Burst == 2 && draft.Actions[1].OffsetMs == 2700, "disabled row settings retained");
            });
            Run("Mobile JSON matches Android's null unmapped-point contract", delegate {
                string path = FilePath(); MobileProfileExport.Export(Profile(), path);
                Dictionary<string, object> json = Read(path);
                ArrayList actions = (ArrayList)json["Actions"];
                Check((int)json["ScreenWidth"] == 0 && (int)json["ScreenHeight"] == 0, "no phone calibration transferred");
                foreach (Dictionary<string, object> action in actions)
                    Check(action.ContainsKey("X") && action.ContainsKey("Y") && action["X"] == null && action["Y"] == null, "null coordinates rather than invalid negative numbers");
                Check(File.ReadAllText(path).Contains("\"X\":null"), "explicit JSON null");
            });
            Run("Draft never transfers desktop click points or mutates source", delegate {
                MacroWorkspaceProfile source = Profile(); MobileTapDraft draft = MobileProfileExport.CreateDraft(source);
                Check(source.Name == "  Café routine  " && source.Timers.Actions[0].Input == " c " && source.Timers.Actions[2].X == 123, "source unchanged");
                Check(!draft.Actions[2].X.HasValue && !draft.Actions[2].Y.HasValue, "desktop coordinates cleared");
                draft.Actions[0].HoldMs = 999; source.Timers.Actions.Clear();
                Check(draft.Actions.Count == 3 && draft.Actions[1].HoldMs == 70, "draft detached from source list");
            });
            Run("Timer export is independent of sequence validity", delegate {
                MacroWorkspaceProfile profile = Profile();
                Check(MobileProfileExport.CreateDraft(profile).Actions.Count == 3, "invalid sequence ignored");
                profile.Sequence = null;
                Check(MobileProfileExport.CreateDraft(profile).Actions.Count == 3, "missing sequence ignored");
            });
            Run("Profile names match Android's 1 to 60 character limit", delegate {
                MacroWorkspaceProfile profile = Profile(); profile.Name = new string('a', 60);
                Check(MobileProfileExport.CreateDraft(profile).Name.Length == 60, "60 characters accepted");
                profile.Name += "b"; Check(Invalid(profile), "61 rejected");
                profile.Name = " \tRoutine "; Check(Invalid(profile), "control characters rejected before trimming");
                profile.Name = "Routine\u0085"; Check(Invalid(profile), "C1 control rejected like Android ISO control");
                profile.Name = "  "; Check(MobileProfileExport.CreateDraft(profile).Name == "My mobile routine", "blank default is valid");
                profile.Name = null; Check(MobileProfileExport.CreateDraft(profile).Name == "My mobile routine", "missing name default");
            });
            Run("Countdown and active stop convert seconds to milliseconds", delegate {
                MacroWorkspaceProfile profile = Profile(); profile.Timers.CountdownSeconds = 30; profile.Timers.SessionSeconds = 86400;
                MobileTapDraft draft = MobileProfileExport.CreateDraft(profile);
                Check(draft.StartDelayMs == 30000 && draft.SessionLimitMs == 86400000, "maximum units converted safely");
                profile.Timers.CountdownSeconds = 0; Check(Invalid(profile), "zero delay rejected");
                profile.Timers.CountdownSeconds = 31; Check(Invalid(profile), "overlong delay rejected");
                profile.Timers.CountdownSeconds = 1; profile.Timers.SessionSeconds = -1; Check(Invalid(profile), "negative stop rejected");
                profile.Timers.SessionSeconds = 86401; Check(Invalid(profile), "overlong stop rejected");
            });
            Run("Action count and enabled requirements bound mobile drafts", delegate {
                MacroWorkspaceProfile profile = Profile(); profile.Timers.Actions.Clear(); Check(Invalid(profile), "empty rejected");
                for (int i = 0; i < 32; i++) profile.Timers.Actions.Add(new MacroAction { Enabled = true, Input = "C", IntervalMs = 150, HoldMs = 10, Burst = 1 });
                Check(MobileProfileExport.CreateDraft(profile).Actions.Count == 32, "32 accepted");
                profile.Timers.Actions.Add(profile.Timers.Actions[0]); Check(Invalid(profile), "33 rejected");
                profile = Profile(); foreach (MacroAction action in profile.Timers.Actions) action.Enabled = false; Check(Invalid(profile), "all disabled rejected");
            });
            Run("Unsupported timers, missing rows and unknown input are rejected", delegate {
                MacroWorkspaceProfile profile = Profile(); profile.Timers.Version = 2; Check(Invalid(profile), "timer version rejected");
                profile = Profile(); profile.Timers.Actions[0] = null; Check(Invalid(profile), "null action rejected");
                profile = Profile(); profile.Timers.Actions[0].Input = "Unknown key"; Check(Invalid(profile), "unknown input rejected");
                profile = Profile(); profile.Timers = null; Check(Invalid(profile), "missing timers rejected");
                Check(Invalid(null), "missing workspace rejected");
            });
            Run("Android timing and burst limits include disabled actions", delegate {
                foreach (int hold in new[] { 0, 9, 1001 }) { MacroWorkspaceProfile p = Profile(); p.Timers.Actions[1].HoldMs = hold; Check(Invalid(p), "invalid disabled hold " + hold); }
                foreach (int interval in new[] { 149, 86400001 }) { MacroWorkspaceProfile p = Profile(); p.Timers.Actions[0].IntervalMs = interval; Check(Invalid(p), "interval range " + interval); }
                foreach (int burst in new[] { 0, 21 }) { MacroWorkspaceProfile p = Profile(); p.Timers.Actions[0].Burst = burst; Check(Invalid(p), "burst range " + burst); }
                foreach (int offset in new[] { -1, 86400001 }) { MacroWorkspaceProfile p = Profile(); p.Timers.Actions[0].OffsetMs = offset; Check(Invalid(p), "offset range " + offset); }
                MacroWorkspaceProfile overlap = Profile(); overlap.Timers.Actions[0].IntervalMs = 150; overlap.Timers.Actions[0].HoldMs = 151; Check(Invalid(overlap), "hold cannot exceed interval");
                MacroWorkspaceProfile maximum = Profile(); maximum.Timers.Actions[0].IntervalMs = 86400000; maximum.Timers.Actions[0].HoldMs = 1000; maximum.Timers.Actions[0].Burst = 20; maximum.Timers.Actions[0].OffsetMs = 86400000;
                Check(MobileProfileExport.CreateDraft(maximum).Actions[0].Burst == 20, "inclusive bounds accepted");
            });
            Run("UTF-8 export keeps Unicode names and mobile schema fields", delegate {
                string path = FilePath(); MacroWorkspaceProfile profile = Profile(); profile.Name = "剣 · Café"; MobileProfileExport.Export(profile, path);
                byte[] bytes = File.ReadAllBytes(path); Dictionary<string, object> json = Read(path);
                Check((string)json["Name"] == profile.Name && (string)json["Format"] == "FarmMacro.Mobile" && (int)json["Version"] == 1, "Unicode schema survives");
                Check(!(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf), "portable UTF8 without BOM");
                Check(!json.ContainsKey("Sequence") && !json.ContainsKey("Timers"), "not a desktop workspace file");
            });
            Run("Atomic mobile export replaces destination and cleans temporary files", delegate {
                string path = FilePath(); MacroWorkspaceProfile profile = Profile(); MobileProfileExport.Export(profile, path);
                profile.Timers.Actions[0].IntervalMs = 22000; MobileProfileExport.Export(profile, path);
                Check(File.ReadAllText(path).Contains("\"IntervalMs\":22000") && Directory.GetFiles(root, "*.tmp").Length == 0, "replacement complete and clean");
            });
            Run("Invalid export leaves destination intact and missing folder absent", delegate {
                string path = FilePath(); MobileProfileExport.Export(Profile(), path); byte[] before = File.ReadAllBytes(path);
                MacroWorkspaceProfile invalid = Profile(); invalid.Name = new string('a', 61); bool rejected = false;
                try { MobileProfileExport.Export(invalid, path); } catch (ArgumentException) { rejected = true; }
                Check(rejected && Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(path)), "invalid export preserved destination");
                string missing = Path.Combine(root, "missing"); rejected = false;
                try { MobileProfileExport.Export(Profile(), Path.Combine(missing, "draft.json")); } catch (DirectoryNotFoundException) { rejected = true; }
                Check(rejected && !Directory.Exists(missing) && Directory.GetFiles(root, "*.tmp").Length == 0, "no folder or temporary files created");
            });
            Console.WriteLine(passed + " mobile export tests passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { foreach (string file in Directory.GetFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
}
