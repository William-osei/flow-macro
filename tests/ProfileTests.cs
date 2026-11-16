using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using FarmMacro;

internal static class ProfileTests
{
    private static readonly string root = Path.Combine(Path.GetTempPath(), "FarmMacroProfileTests-" + Guid.NewGuid().ToString("N"));
    private static readonly List<string> folders = new List<string>();
    private static int passed;
    private static string Folder()
    {
        string folder = Path.Combine(root, "case-" + folders.Count);
        folders.Add(folder); return folder;
    }
    private static MacroWorkspaceProfile Profile(string name)
    {
        return new MacroWorkspaceProfile
        {
            Name = name,
            Timers = new SavedProfile
            {
                SessionSeconds = 0,
                Actions = new List<MacroAction>
                {
                    new MacroAction { Enabled = true, Input = "C", IntervalMs = 3000, HoldMs = 60, Burst = 1 },
                    new MacroAction { Enabled = false, Input = "X", IntervalMs = 5000, HoldMs = 60, Burst = 1, OffsetMs = 800 }
                }
            },
            Sequence = new SavedSequence
            {
                RepeatCount = 0, SessionSeconds = 0,
                Steps = new List<MacroStep>
                {
                    new MacroStep { Input = "C", IsDown = true, DelayMs = 0 },
                    new MacroStep { Input = "C", IsDown = false, DelayMs = 60 },
                    new MacroStep { Input = "V", IsDown = true, DelayMs = 800 },
                    new MacroStep { Input = "V", IsDown = false, DelayMs = 60 }
                }
            }
        };
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAILED: " + message); }
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static bool Rejects(Action action)
    {
        try { action(); return false; } catch (ArgumentException) { return true; } catch (InvalidDataException) { return true; }
    }
    public static int Main()
    {
        try
        {
            Run("Library reads do not create its storage folder", delegate
            {
                string path = Folder(); ProfileLibrary library = new ProfileLibrary(path);
                Check(library.ListNames().Count == 0 && !Directory.Exists(path), "read-only constructor/list");
            });
            Run("Unicode names round-trip with safe hashed filenames", delegate
            {
                string path = Folder(); ProfileLibrary library = new ProfileLibrary(path); MacroWorkspaceProfile profile = Profile("  夜狩 / Farm: α  ");
                library.Save(profile); MacroWorkspaceProfile loaded = library.Load("夜狩 / Farm: α");
                Check(loaded.Name == "夜狩 / Farm: α" && profile.Name.StartsWith("  "), "normalized copy without caller mutation");
                string[] files = Directory.GetFiles(path, "*.json");
                Check(files.Length == 1 && Regex.IsMatch(Path.GetFileName(files[0]), "^[a-f0-9]{64}\\.json$"), "hash-only filename");
                Check(loaded.Timers.Actions.Count == 2 && !loaded.Timers.Actions[1].Enabled && loaded.Timers.Actions[1].OffsetMs == 800, "disabled row preserved");
                Check(loaded.Sequence.RepeatCount == 0 && loaded.Sequence.SessionSeconds == 0 && loaded.Timers.SessionSeconds == 0, "indefinite settings preserved");
            });
            Run("Canonical Unicode names address the same preset", delegate
            {
                string path = Folder(); ProfileLibrary library = new ProfileLibrary(path);
                library.Save(Profile("Cafe\u0301")); Check(library.Load("Café").Name == "Café", "NFC address");
                library.Save(Profile("Café")); Check(Directory.GetFiles(path, "*.json").Length == 1, "canonical overwrite");
            });
            Run("Cooldown preservation survives profile saves and rejects bursts", delegate
            {
                ProfileLibrary library = new ProfileLibrary(Folder()); MacroWorkspaceProfile profile = Profile("Cooldown task");
                profile.Timers.Actions[0].Input = "Z"; profile.Timers.Actions[0].IntervalMs = 24000;
                profile.Timers.Actions[0].KeepCooldownOnResume = true;
                library.Save(profile);
                Check(library.Load(profile.Name).Timers.Actions[0].KeepCooldownOnResume, "cooldown option survives local save");
                profile.Timers.Actions[0].Burst = 2;
                Check(Rejects(delegate { library.Save(profile); }), "cooldown bursts reject before replacing the file");
                Check(library.Load(profile.Name).Timers.Actions[0].Burst == 1, "existing cooldown setup preserved");
            });
            Run("Named profiles preserve detached quick click settings and normalize mouse input", delegate
            {
                ProfileLibrary library = new ProfileLibrary(Folder()); MacroWorkspaceProfile profile = Profile("Click task");
                profile.Clicker = new QuickClickSettings { Input = " right CLICK ", IntervalMs = 250, HoldMs = 70, Burst = 4, X = 123, Y = 456, CountdownSeconds = 4, SessionSeconds = 90, MaxClicks = 7 };
                library.Save(profile); profile.Clicker.MaxClicks = 99;
                MacroWorkspaceProfile loaded = library.Load("Click task");
                Check(loaded.Clicker.Input == "Right click" && loaded.Clicker.IntervalMs == 250 && loaded.Clicker.HoldMs == 70 && loaded.Clicker.Burst == 4, "click timing persisted");
                Check(loaded.Clicker.X == 123 && loaded.Clicker.Y == 456 && loaded.Clicker.CountdownSeconds == 4 && loaded.Clicker.SessionSeconds == 90 && loaded.Clicker.MaxClicks == 7, "click point and exact total persisted");
                Check(profile.Clicker.Input == " right CLICK ", "save never normalizes the caller in place");
            });
            Run("Older named profiles without a clicker receive safe stopped defaults", delegate
            {
                string folder = Folder(); ProfileLibrary library = new ProfileLibrary(folder); library.Save(Profile("Older profile"));
                string file = Directory.GetFiles(folder, "*.json")[0]; JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> document = (Dictionary<string, object>)serializer.DeserializeObject(File.ReadAllText(file));
                document.Remove("Clicker"); File.WriteAllText(file, serializer.Serialize(document));
                MacroWorkspaceProfile loaded = library.Load("Older profile");
                Check(loaded.Clicker != null && loaded.Clicker.Input == "Left click" && loaded.Clicker.MaxClicks == 0 && loaded.Clicker.CountdownSeconds == 3 && loaded.Clicker.X == null, "backward-compatible clicker defaults");
            });
            Run("Invalid quick click settings cannot overwrite a named profile", delegate
            {
                ProfileLibrary library = new ProfileLibrary(Folder()); MacroWorkspaceProfile profile = Profile("Click task"); library.Save(profile);
                profile.Clicker.MaxClicks = -1;
                Check(Rejects(delegate { library.Save(profile); }) && library.Load("Click task").Clicker.MaxClicks == 0, "invalid total preserves existing file");
                profile.Clicker = null; library.Save(profile);
                Check(profile.Clicker == null && library.Load("Click task").Clicker.Input == "Left click", "legacy null clicker becomes a detached default");
            });
            Run("Atomic overwrite updates both modes and leaves no temporary files", delegate
            {
                string path = Folder(); ProfileLibrary library = new ProfileLibrary(path); MacroWorkspaceProfile profile = Profile("Farm");
                library.Save(profile); profile.Timers.Actions[0].IntervalMs = 4200; profile.Sequence.LoopDelayMs = 900;
                library.Save(profile); MacroWorkspaceProfile loaded = library.Load("Farm");
                Check(loaded.Timers.Actions[0].IntervalMs == 4200 && loaded.Sequence.LoopDelayMs == 900, "both modes replaced");
                Check(Directory.GetFiles(path, "*.json").Length == 1 && Directory.GetFiles(path, "*.tmp").Length == 0, "no orphan temporary");
            });
            Run("Loaded snapshots do not mutate persisted profiles", delegate
            {
                ProfileLibrary library = new ProfileLibrary(Folder()); library.Save(Profile("Farm"));
                MacroWorkspaceProfile copy = library.Load("Farm"); copy.Timers.Actions[0].Input = "V"; copy.Sequence.Steps.Clear();
                MacroWorkspaceProfile fresh = library.Load("Farm"); Check(fresh.Timers.Actions[0].Input == "C" && fresh.Sequence.Steps.Count == 4, "detached data");
            });
            Run("Invalid names and versions are rejected without overwriting", delegate
            {
                ProfileLibrary library = new ProfileLibrary(Folder()); library.Save(Profile("Farm"));
                Check(Rejects(delegate { library.Save(Profile("\n")); }) && Rejects(delegate { library.Save(Profile(new string('x', 61))); }), "name bounds");
                Check(Rejects(delegate { library.Save(Profile("\nFarm")); }), "control characters rejected before trim");
                MacroWorkspaceProfile wrong = Profile("Farm"); wrong.Version = 2;
                Check(Rejects(delegate { library.Save(wrong); }) && library.Load("Farm").Version == 1, "version rejection preserves existing data");
            });
            Run("Validation bounds row counts and preserves empty drafts", delegate
            {
                MacroWorkspaceProfile profile = Profile("Draft"); profile.Timers.Actions.Clear(); profile.Sequence.Steps.Clear(); ProfileLibrary.Validate(profile);
                for (int i = 0; i < 101; i++) profile.Timers.Actions.Add(new MacroAction());
                Check(Rejects(delegate { ProfileLibrary.Validate(profile); }), "timer row cap");
                profile.Timers.Actions.Clear(); for (int i = 0; i < 10001; i++) profile.Sequence.Steps.Add(new MacroStep());
                Check(Rejects(delegate { ProfileLibrary.Validate(profile); }), "step count cap");
            });
            Run("Snapshots canonicalize compatible input names without caller mutation", delegate
            {
                ProfileLibrary library = new ProfileLibrary(Folder()); MacroWorkspaceProfile profile = Profile("Farm");
                profile.Timers.Actions[0].Input = " c "; profile.Sequence.Steps[0].Input = "c";
                library.Save(profile); MacroWorkspaceProfile loaded = library.Load("Farm");
                Check(loaded.Timers.Actions[0].Input == "C" && loaded.Sequence.Steps[0].Input == "C" && profile.Timers.Actions[0].Input == " c ", "canonical saved copy");
            });
            Run("Malformed and missing-version JSON cannot load", delegate
            {
                string path = Folder(); ProfileLibrary library = new ProfileLibrary(path); library.Save(Profile("Farm"));
                string file = Directory.GetFiles(path, "*.json")[0]; string valid = File.ReadAllText(file);
                File.WriteAllText(file, valid.Replace("\"Version\":1,", "")); Check(Rejects(delegate { library.Load("Farm"); }), "explicit versions required");
                File.WriteAllText(file, "{ broken"); Check(Rejects(delegate { library.Load("Farm"); }), "invalid JSON rejected");
                Check(library.ListNames().Count == 0 && library.LastWarning != null, "invalid file skipped with warning");
            });
            Run("Oversized and invalid-encoding files are bounded and skipped", delegate
            {
                string path = Folder(); ProfileLibrary library = new ProfileLibrary(path); library.Save(Profile("Farm")); string file = Directory.GetFiles(path, "*.json")[0];
                using (FileStream stream = new FileStream(file, FileMode.Create)) stream.SetLength(2 * 1024 * 1024 + 1);
                Check(Rejects(delegate { library.Load("Farm"); }) && library.ListNames().Count == 0, "oversized file rejected");
                File.WriteAllBytes(file, new byte[] { 0xff, 0xfe, 0xff });
                Check(Rejects(delegate { library.Load("Farm"); }) && library.ListNames().Count == 0, "invalid encoded content rejected");
            });
            Run("Corrupt numeric fields do not break the profile list", delegate
            {
                string path = Folder(); ProfileLibrary library = new ProfileLibrary(path); library.Save(Profile("Farm"));
                string file = Directory.GetFiles(path, "*.json")[0]; string valid = File.ReadAllText(file);
                foreach (string value in new[] { "2147483648", "1e100", "\"NaN\"" })
                {
                    File.WriteAllText(file, valid.Replace("\"IntervalMs\":3000", "\"IntervalMs\":" + value));
                    Check(Rejects(delegate { library.Load("Farm"); }), "invalid numeric value rejected");
                    Check(library.ListNames().Count == 0 && library.LastWarning != null, "numeric conversion failure isolated");
                }
            });
            Run("Library list is sorted and ignores misnamed files", delegate
            {
                string path = Folder(); ProfileLibrary library = new ProfileLibrary(path); library.Save(Profile("zeta")); library.Save(Profile("Alpha"));
                File.WriteAllText(Path.Combine(path, "not-a-profile-hash.json"), new JavaScriptSerializer().Serialize(Profile("Other")));
                List<string> names = library.ListNames(); Check(names.Count == 2 && names[0] == "Alpha" && names[1] == "zeta" && library.LastWarning != null, "sorted verified names");
            });
            Run("Library caps new profiles at 100 while allowing replacement", delegate
            {
                string path = Folder(); ProfileLibrary library = new ProfileLibrary(path);
                for (int i = 0; i < 100; i++) library.Save(Profile("Preset " + i));
                bool rejected = false; try { library.Save(Profile("Preset overflow")); } catch (IOException) { rejected = true; }
                Check(rejected && library.ListNames().Count == 100, "capacity enforced");
                MacroWorkspaceProfile replacement = Profile("Preset 0"); replacement.Sequence.LoopDelayMs = 3000; library.Save(replacement);
                Check(library.Load("Preset 0").Sequence.LoopDelayMs == 3000, "replacement works at capacity");
            });
            Console.WriteLine(passed + " profile library tests passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
        finally
        {
            // Only these test-created, unique temporary folders are removed.
            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
            if (Directory.Exists(root)) Directory.Delete(root);
        }
    }
}
