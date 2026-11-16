using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using FarmMacro;

internal static class BundledPresetTests
{
    static readonly string root = Path.Combine(Path.GetTempPath(), "FlowMacroPresetTests-" + Guid.NewGuid().ToString("N"));
    static int passed;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Run(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    static MacroWorkspaceProfile Profile(string name)
    {
        return new MacroWorkspaceProfile
        {
            Name = name,
            Timers = new SavedProfile { Actions = new List<MacroAction> { new MacroAction { Enabled = true, Input = "Z", IntervalMs = 24000, HoldMs = 60, Burst = 1, OffsetMs = 2600, KeepCooldownOnResume = true } } },
            Sequence = new SavedSequence { Steps = new List<MacroStep>() }
        };
    }
    [STAThread] static int Main(string[] args)
    {
        Directory.CreateDirectory(root);
        try
        {
            string presets = Path.Combine(root, "presets");
            Run("Missing preset folder is read-only and returns an empty catalog", delegate
            {
                int skipped;
                Check(BundledPresetCatalog.Read(presets, out skipped).Count == 0 && skipped == 0 && !Directory.Exists(presets), "Missing catalog created storage or entries.");
            });
            Directory.CreateDirectory(presets);
            string zPath = Path.Combine(presets, "a-z.json");
            string aPath = Path.Combine(presets, "z-a.json");
            ProfileTransfer.Export(Profile("Zeta setup"), zPath);
            ProfileTransfer.Export(Profile("Alpha setup"), aPath);
            string original = File.ReadAllText(aPath);
            Run("Preset names sort independently of portable filenames", delegate
            {
                int skipped;
                List<BundledPresetCatalog.Entry> rows = BundledPresetCatalog.Read(presets, out skipped);
                Check(rows.Count == 2 && rows[0].Name == "Alpha setup" && rows[1].Name == "Zeta setup" && rows[0].FilePath == aPath && skipped == 0, "Friendly-name order or original path differed.");
                Check(rows[0].Summary.Contains("Z") && rows[0].Summary.Contains("Unlimited timer session"), "Validated timer summary was missing.");
            });
            Run("Malformed and oversized profiles are skipped without hiding valid presets", delegate
            {
                File.WriteAllText(Path.Combine(presets, "broken.json"), "{not-json");
                File.WriteAllText(Path.Combine(presets, "huge.json"), new string('x', 2 * 1024 * 1024 + 1));
                int skipped;
                List<BundledPresetCatalog.Entry> rows = BundledPresetCatalog.Read(presets, out skipped);
                Check(rows.Count == 2 && skipped == 2, "Invalid files prevented usable presets or entered the catalog.");
            });
            Run("Preset discovery excludes nested and non-JSON files", delegate
            {
                string nested = Path.Combine(presets, "nested"); Directory.CreateDirectory(nested);
                ProfileTransfer.Export(Profile("Nested setup"), Path.Combine(nested, "nested.json"));
                File.WriteAllText(Path.Combine(presets, "ignore.txt"), "not a preset");
                int skipped;
                Check(BundledPresetCatalog.Read(presets, out skipped).Count == 2 && skipped == 2 && File.ReadAllText(aPath) == original, "Scanning recursed or changed the portable files.");
            });
            Run("Public release includes only the two validated general starter presets", delegate
            {
                string project = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
                int skipped;
                List<BundledPresetCatalog.Entry> rows = BundledPresetCatalog.Read(Path.Combine(project, "presets"), out skipped);
                Check(rows.Count == 2 && skipped == 0, "The public release must contain only its two general starter profiles.");
                bool clicker = false, timer = false;
                foreach (BundledPresetCatalog.Entry row in rows)
                {
                    if (Path.GetFileName(row.FilePath) == "Starter-Gentle-clicker.json") clicker = true;
                    if (Path.GetFileName(row.FilePath) == "Starter-Space-timer.json") timer = true;
                }
                Check(clicker && timer, "The general starter files are missing.");
            });
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ProfileLibrary library = new ProfileLibrary(Path.Combine(root, "library"));
            int captures = 0, applies = 0;
            bool busy = false;
            using (ProfilePanel panel = new ProfilePanel(delegate { captures++; return Profile("Current draft"); }, delegate { applies++; }, delegate { return busy; }, library, presets))
            {
                Run("Adding a bundled preset saves a detached snapshot without applying or capturing", delegate
                {
                    panel.ImportFile(aPath);
                    Check(library.ListNames().Count == 1 && library.Load("Alpha setup").Timers.Actions[0].KeepCooldownOnResume && applies == 0 && captures == 0, "Import changed the workspace or lost cooldown settings.");
                    Check(panel.Status.Contains("Load selected"), "Import did not explain the next load step.");
                });
                Run("Repeated preset imports preserve existing profiles and use unique names", delegate
                {
                    panel.ImportFile(aPath); panel.ImportFile(aPath);
                    Check(library.ListNames().Count == 3 && library.Load("Alpha setup (imported)").Name == "Alpha setup (imported)" && library.Load("Alpha setup (imported 2)").Name == "Alpha setup (imported 2)", "Duplicate imports overwrote a saved setup.");
                    Check(File.ReadAllText(aPath) == original && applies == 0, "Adding a duplicate changed its source or loaded it.");
                });
                Run("Busy import is blocked before any file read or persistence", delegate
                {
                    busy = true;
                    panel.ImportFile(Path.Combine(root, "missing.json"));
                    busy = false;
                    Check(library.ListNames().Count == 3 && applies == 0 && panel.Status.Contains("Stop playback"), "Busy import did not fail closed.");
                });
                Run("Only explicit Load selected applies an imported preset", delegate
                {
                    panel.GetType().GetMethod("LoadSelected", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
                    Check(applies == 1 && captures == 0, "Explicit load did not apply exactly once.");
                });
                TextBox search = (TextBox)panel.GetType().GetField("search", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                ListBox names = (ListBox)panel.GetType().GetField("names", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                Label count = (Label)panel.GetType().GetField("countLabel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                Label empty = (Label)panel.GetType().GetField("emptyLabel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                Button load = (Button)panel.GetType().GetField("loadButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                Run("Find filters profile names case-insensitively without loading or capturing", delegate
                {
                    search.Text = "  IMPORTED 2  ";
                    Check(names.Items.Count == 1 && (string)names.SelectedItem == "Alpha setup (imported 2)" && count.Text == "MATCHES  1 / 3", "Search did not match trimmed text or show filtered totals.");
                    Check(applies == 1 && captures == 0 && library.ListNames().Count == 3, "Filtering applied, captured or rewrote a setup.");
                });
                Run("An unmatched Find query disables loading and clearing restores the library", delegate
                {
                    search.Text = "No such setup";
                    Check(names.Items.Count == 0 && !load.Enabled && empty.Text.Contains("No matching profiles") && count.Text == "MATCHES  0 / 3", "No-match guidance or disabled loading differed.");
                    search.Clear();
                    Check(names.Items.Count == 3 && load.Enabled && count.Text == "SAVED PROFILES  3" && applies == 1 && captures == 0, "Clearing Find did not restore saved setups without loading them.");
                });
                Run("Import clears Find and reveals the newly added profile without loading", delegate
                {
                    search.Text = "No matches";
                    panel.ImportFile(zPath);
                    Check(search.Text.Length == 0 && names.Items.Count == 4 && (string)names.SelectedItem == "Zeta setup" && count.Text == "SAVED PROFILES  4", "The imported setup remained hidden by filtering.");
                    Check(applies == 1 && captures == 0, "Import after filtering started or captured a workspace.");
                });
                Run("Save clears Find and selects the saved current workspace", delegate
                {
                    search.Text = "No matches";
                    TextBox profileName = (TextBox)panel.GetType().GetField("profileName", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
                    profileName.Text = "Fresh draft";
                    panel.GetType().GetMethod("SaveCurrent", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
                    Check(search.Text.Length == 0 && names.Items.Count == 5 && (string)names.SelectedItem == "Fresh draft" && library.Load("Fresh draft").Name == "Fresh draft", "The saved setup remained hidden or lost its name.");
                    Check(applies == 1 && captures == 1, "Saving captured more than once or applied playback.");
                });
            }
            Console.WriteLine(passed + " bundled-preset checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
