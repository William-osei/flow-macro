using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using FarmMacro;

internal static class ProfileTransferTests
{
    private static readonly string root = Path.Combine(Path.GetTempPath(), "FarmMacroTransferTests-" + Guid.NewGuid().ToString("N"));
    private static int passed;
    private static string FilePath() { return Path.Combine(root, Guid.NewGuid().ToString("N") + ".json"); }
    private static MacroWorkspaceProfile Profile()
    {
        return new MacroWorkspaceProfile
        {
            Name = "  Cafe\u0301 / Farm  ",
            Timers = new SavedProfile
            {
                SessionSeconds = 0,
                Actions = new List<MacroAction>
                {
                    new MacroAction { Enabled = true, Input = " c ", IntervalMs = 3000, HoldMs = 60, Burst = 1, OffsetMs = 0 },
                    new MacroAction { Enabled = false, Input = "X", IntervalMs = 5000, HoldMs = 70, Burst = 2, OffsetMs = 800 },
                    new MacroAction { Enabled = true, Input = "Left click", IntervalMs = 1000, HoldMs = 60, Burst = 1, X = 123, Y = 456 }
                }
            },
            Sequence = new SavedSequence
            {
                RepeatCount = 0, SessionSeconds = 0, LoopDelayMs = 1000,
                Steps = new List<MacroStep>
                {
                    new MacroStep { Input = "Shift", IsDown = true, DelayMs = 0 },
                    new MacroStep { Input = "Left click", IsDown = true, DelayMs = 120, X = 321, Y = 654 },
                    new MacroStep { Input = "Left click", IsDown = false, DelayMs = 75 },
                    new MacroStep { Input = "Shift", IsDown = false, DelayMs = 40 },
                    new MacroStep { Input = "v", IsDown = true, DelayMs = 800 },
                    new MacroStep { Input = "V", IsDown = false, DelayMs = 60 }
                }
            }
        };
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAILED: " + message); }
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static bool InvalidImport(string path)
    {
        try { ProfileTransfer.Import(path); return false; } catch (InvalidDataException) { return true; }
    }
    public static int Main()
    {
        Directory.CreateDirectory(root);
        try
        {
            Run("Portable profile preserves holds, click points, disabled rows and unlimited settings", delegate
            {
                string path = FilePath(); ProfileTransfer.Export(Profile(), path); MacroWorkspaceProfile loaded = ProfileTransfer.Import(path);
                Check(loaded.Name == "Café / Farm" && loaded.Timers.Actions[0].Input == "C" && loaded.Sequence.Steps[4].Input == "V", "normalized values");
                Check(!loaded.Timers.Actions[1].Enabled && loaded.Timers.Actions[1].Burst == 2 && loaded.Timers.Actions[1].OffsetMs == 800, "disabled timer row");
                Check(loaded.Timers.Actions[2].X == 123 && loaded.Timers.Actions[2].Y == 456, "timer click point");
                Check(loaded.Sequence.Steps[1].IsDown && loaded.Sequence.Steps[1].X == 321 && loaded.Sequence.Steps[1].Y == 654 &&
                    !loaded.Sequence.Steps[2].IsDown && loaded.Sequence.Steps[2].DelayMs == 75 && loaded.Sequence.Steps[3].DelayMs == 40, "chord/hold and client position");
                Check(loaded.Timers.SessionSeconds == 0 && loaded.Sequence.RepeatCount == 0 && loaded.Sequence.SessionSeconds == 0, "unlimited preserved");
            });
            Run("Export detaches mutable source and uses chosen portable filename", delegate
            {
                string path = Path.Combine(root, "my-macro.json"); MacroWorkspaceProfile source = Profile(); ProfileTransfer.Export(source, path);
                Check(source.Name.StartsWith("  ") && source.Timers.Actions[0].Input == " c ", "source not mutated");
                source.Timers.Actions.Clear(); source.Sequence.Steps.Clear(); MacroWorkspaceProfile loaded = ProfileTransfer.Import(path);
                Check(loaded.Timers.Actions.Count == 3 && loaded.Sequence.Steps.Count == 6 && File.Exists(path), "detached portable file");
            });
            Run("Cooldown row settings survive portable transfer", delegate
            {
                string path = FilePath(); MacroWorkspaceProfile profile = Profile();
                profile.Timers.Actions[0].Input = "Z"; profile.Timers.Actions[0].IntervalMs = 24000;
                profile.Timers.Actions[0].Burst = 1; profile.Timers.Actions[0].KeepCooldownOnResume = true;
                ProfileTransfer.Export(profile, path);
                MacroWorkspaceProfile imported = ProfileTransfer.Import(path);
                Check(imported.Timers.Actions[0].KeepCooldownOnResume && imported.Timers.Actions[0].IntervalMs == 24000, "portable cooldown timing and flag");
                profile.Timers.Actions[0].KeepCooldownOnResume = false;
                Check(ProfileTransfer.Import(path).Timers.Actions[0].KeepCooldownOnResume, "portable snapshot is detached");
            });
            Run("Quick click portable round-trip preserves exact counts and backward defaults", delegate
            {
                string path = FilePath(); MacroWorkspaceProfile source = Profile();
                source.Clicker = new QuickClickSettings { Input = " middle click ", IntervalMs = 500, HoldMs = 80, Burst = 3, X = 17, Y = 29, MaxClicks = 5, CountdownSeconds = 5, SessionSeconds = 120 };
                ProfileTransfer.Export(source, path); MacroWorkspaceProfile loaded = ProfileTransfer.Import(path);
                Check(loaded.Clicker.Input == "Middle click" && loaded.Clicker.IntervalMs == 500 && loaded.Clicker.HoldMs == 80 && loaded.Clicker.Burst == 3 && loaded.Clicker.MaxClicks == 5, "portable click timing/count");
                Check(loaded.Clicker.X == 17 && loaded.Clicker.Y == 29 && loaded.Clicker.CountdownSeconds == 5 && loaded.Clicker.SessionSeconds == 120 && source.Clicker.Input == " middle click ", "portable coordinates and detached normalization");
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> document = (Dictionary<string, object>)serializer.DeserializeObject(File.ReadAllText(path));
                document.Remove("Clicker"); File.WriteAllText(path, serializer.Serialize(document));
                QuickClickSettings legacy = ProfileTransfer.Import(path).Clicker;
                Check(legacy != null && legacy.Input == "Left click" && legacy.HoldMs == 40 && legacy.MaxClicks == 0 && legacy.X == null, "old portable profile gets defaults");
                document["Clicker"] = new Dictionary<string, object> { { "MaxClicks", -1 } };
                File.WriteAllText(path, serializer.Serialize(document)); Check(InvalidImport(path), "invalid imported click total rejected");
            });
            Run("Import creates no library or sibling files and does not rewrite source", delegate
            {
                string path = FilePath(); ProfileTransfer.Export(Profile(), path); byte[] before = File.ReadAllBytes(path);
                int count = Directory.GetFiles(root).Length; MacroWorkspaceProfile loaded = ProfileTransfer.Import(path);
                loaded.Timers.Actions[0].Input = "Z";
                Check(Directory.GetFiles(root).Length == count && Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(path)), "read-only import");
                Check(ProfileTransfer.Import(path).Timers.Actions[0].Input == "C", "fresh detached import");
            });
            Run("UTF-8 BOM is accepted without accepting malformed text", delegate
            {
                string path = FilePath(); ProfileTransfer.Export(Profile(), path); string json = File.ReadAllText(path, Encoding.UTF8);
                File.WriteAllText(path, json, new UTF8Encoding(true)); Check(ProfileTransfer.Import(path).Sequence.Steps.Count == 6, "UTF8 BOM accepted");
                File.WriteAllBytes(path, new byte[] { 0xef, 0xbb, 0xbf, 0xc3, 0x28 }); Check(InvalidImport(path), "invalid UTF8 rejected");
                File.WriteAllText(path, json, Encoding.Unicode); Check(InvalidImport(path), "UTF16 rejected as unsupported encoding");
            });
            Run("Missing and mismatched versions are rejected", delegate
            {
                string path = FilePath(); ProfileTransfer.Export(Profile(), path); string valid = File.ReadAllText(path);
                File.WriteAllText(path, valid.Replace("\"Version\":1", "\"Version\":2")); Check(InvalidImport(path), "unsupported versions");
                File.WriteAllText(path, valid.Replace("\"Version\":1,", "")); Check(InvalidImport(path), "explicit versions required");
                File.WriteAllText(path, "null"); Check(InvalidImport(path), "null document");
            });
            Run("Malformed JSON and numeric conversion failures are isolated", delegate
            {
                string path = FilePath(); ProfileTransfer.Export(Profile(), path); string valid = File.ReadAllText(path);
                foreach (string value in new[] { "2147483648", "1e100", "\"NaN\"" })
                {
                    File.WriteAllText(path, valid.Replace("\"IntervalMs\":3000", "\"IntervalMs\":" + value));
                    Check(InvalidImport(path), "numeric failure rejected");
                }
                File.WriteAllText(path, "{not json}"); Check(InvalidImport(path), "damaged JSON rejected");
            });
            Run("Oversized portable files are rejected before parsing", delegate
            {
                string path = FilePath(); ProfileTransfer.Export(Profile(), path); string valid = File.ReadAllText(path, Encoding.UTF8);
                // Trailing whitespace keeps this valid JSON; rejection must be
                // due to its byte size rather than damaged syntax.
                int padding = 2 * 1024 * 1024 + 1 - Encoding.UTF8.GetByteCount(valid);
                File.WriteAllText(path, valid + new string(' ', padding), new UTF8Encoding(false));
                Check(InvalidImport(path), "2MB byte cap");
            });
            Run("Atomic export replaces chosen file and cleans temporary files", delegate
            {
                string path = FilePath(); MacroWorkspaceProfile profile = Profile(); ProfileTransfer.Export(profile, path);
                profile.Sequence.LoopDelayMs = 4500; ProfileTransfer.Export(profile, path);
                Check(ProfileTransfer.Import(path).Sequence.LoopDelayMs == 4500 && Directory.GetFiles(root, "*.tmp").Length == 0, "atomic replacement");
            });
            Run("Invalid export preserves existing destination and missing folders stay absent", delegate
            {
                string path = FilePath(); ProfileTransfer.Export(Profile(), path); byte[] before = File.ReadAllBytes(path);
                MacroWorkspaceProfile invalid = Profile(); invalid.Version = 3; bool rejected = false;
                try { ProfileTransfer.Export(invalid, path); } catch (ArgumentException) { rejected = true; }
                Check(rejected && Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(path)), "invalid export cannot overwrite");
                string missing = Path.Combine(root, "missing-folder"); rejected = false;
                try { ProfileTransfer.Export(Profile(), Path.Combine(missing, "macro.json")); } catch (DirectoryNotFoundException) { rejected = true; }
                Check(rejected && !Directory.Exists(missing), "no implicit parent creation");
            });
            Run("Portable point routes use version two and round-trip without mutating the caller", delegate
            {
                string path = FilePath(); MacroWorkspaceProfile source = Profile();
                source.Clicker.UseMultiplePoints = true; source.Clicker.Points.Add(new ClickPoint { X = 12, Y = 34 }); source.Clicker.Points.Add(new ClickPoint { X = 56, Y = 78 });
                source.Clicker.Burst = 2; source.Clicker.MaxClicks = 11;
                ProfileTransfer.Export(source, path); MacroWorkspaceProfile imported = ProfileTransfer.Import(path);
                Check(source.Version == 1 && imported.Version == 2 && imported.Clicker.UseMultiplePoints && imported.Clicker.Points.Count == 2, "route writer opts into an older-build rejection version without caller mutation");
                Check(imported.Clicker.Points[0].X == 12 && imported.Clicker.Points[1].Y == 78 && imported.Clicker.Burst == 2 && imported.Clicker.MaxClicks == 11, "route points, order and exact total retained");
                imported.Clicker.Points[0].X = 90; Check(ProfileTransfer.Import(path).Clicker.Points[0].X == 12, "imports own detached point objects");
                source.Clicker.UseMultiplePoints = false; ProfileTransfer.Export(source, path);
                Check(ProfileTransfer.Import(path).Version == 2, "inactive saved route points still use version two");
            });
            Run("Portable route drafts remain editable and version-one route documents reject", delegate
            {
                string path = FilePath(); MacroWorkspaceProfile source = Profile(); source.Clicker.UseMultiplePoints = true;
                ProfileTransfer.Export(source, path); MacroWorkspaceProfile imported = ProfileTransfer.Import(path);
                Check(imported.Version == 2 && imported.Clicker.Points.Count == 0 && imported.Clicker.UseMultiplePoints, "unplaced route is a valid saved draft");
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> document = (Dictionary<string, object>)serializer.DeserializeObject(File.ReadAllText(path));
                document["Version"] = 1; File.WriteAllText(path, serializer.Serialize(document)); Check(InvalidImport(path), "version one cannot carry enabled route mode");
                Dictionary<string, object> clicker = (Dictionary<string, object>)document["Clicker"]; clicker["UseMultiplePoints"] = false;
                clicker["Points"] = new object[] { new Dictionary<string, object> { { "X", 10 }, { "Y", 20 } } };
                File.WriteAllText(path, serializer.Serialize(document)); Check(InvalidImport(path), "version one cannot hide inactive route points either");
            });
            Run("Portable legacy point defaults remain compatible and malformed routes never import", delegate
            {
                string path = FilePath(); ProfileTransfer.Export(Profile(), path); JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> document = (Dictionary<string, object>)serializer.DeserializeObject(File.ReadAllText(path));
                Dictionary<string, object> clicker = (Dictionary<string, object>)document["Clicker"];
                clicker.Remove("Points"); clicker.Remove("UseMultiplePoints"); File.WriteAllText(path, serializer.Serialize(document));
                Check(ProfileTransfer.Import(path).Version == 1 && ProfileTransfer.Import(path).Clicker.Points.Count == 0, "old single-click file defaults");
                clicker["Points"] = null; File.WriteAllText(path, serializer.Serialize(document)); Check(ProfileTransfer.Import(path).Clicker.Points.Count == 0, "null list defaults");
                document["Version"] = 2; clicker["UseMultiplePoints"] = true;
                foreach (object invalid in new object[] { new object[] { null }, new object[] { new Dictionary<string, object> { { "X", -1 }, { "Y", 2 } } }, new object[] { new Dictionary<string, object> { { "X", 2147483648L }, { "Y", 2 } } }, new object[] { new Dictionary<string, object> { { "X", 10 } } }, new object[] { new Dictionary<string, object> { { "X", 1.5 }, { "Y", 2 } } }, new object[] { new Dictionary<string, object> { { "X", "1" }, { "Y", 2 } } } })
                {
                    clicker["Points"] = invalid; File.WriteAllText(path, serializer.Serialize(document)); Check(InvalidImport(path), "invalid route entries fail before loading");
                }
                document.Remove("Clicker"); document["clicker"] = clicker; File.WriteAllText(path, serializer.Serialize(document)); Check(InvalidImport(path), "cased clicker alias cannot bypass validation");
                document.Remove("clicker"); document["Clicker"] = clicker; clicker.Remove("Points");
                clicker["points"] = new object[] { new Dictionary<string, object> { { "X", 10 } } };
                File.WriteAllText(path, serializer.Serialize(document)); Check(InvalidImport(path), "cased point-list alias cannot bypass validation");
                clicker.Remove("points"); clicker["Points"] = new object[] { new Dictionary<string, object> { { "x", 10 }, { "Y", 20 } } };
                File.WriteAllText(path, serializer.Serialize(document)); Check(InvalidImport(path), "cased coordinate alias is not accepted ambiguously");
                clicker["Points"] = new object[] { new Dictionary<string, object> { { "X", 10 }, { "Y", 20 } } }; clicker["usemultiplepoints"] = false;
                File.WriteAllText(path, serializer.Serialize(document)); Check(InvalidImport(path), "duplicate case aliases never choose a different playback mode");
            });
            Console.WriteLine(passed + " portable profile tests passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
        finally
        {
            foreach (string file in Directory.GetFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
    }
}
