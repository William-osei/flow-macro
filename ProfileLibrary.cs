using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace FarmMacro
{
    public sealed class MacroWorkspaceProfile
    {
        public int Version = 1;
        public string Name;
        public SavedProfile Timers;
        public SavedSequence Sequence;
        public QuickClickSettings Clicker = new QuickClickSettings();
    }

    // Named, local-only snapshots. Loading never arms either input engine.
    public sealed class ProfileLibrary
    {
        private const int MaxBytes = 2 * 1024 * 1024;
        private const int MaxProfiles = 100;
        private readonly string folder;

        public ProfileLibrary() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FarmMacro", "library")) { }
        public ProfileLibrary(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Choose a library folder.");
            this.folder = Path.GetFullPath(folder);
        }
        public string FolderPath { get { return folder; } }
        public string LastWarning { get; private set; }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 32 };
        }
        private static string NormalizeName(string name)
        {
            if (name == null) throw new ArgumentException("Enter a profile name.");
            foreach (char c in name)
                if (Char.IsControl(c)) throw new ArgumentException("Profile names cannot contain control characters.");
            string normalized = name.Trim().Normalize(NormalizationForm.FormC);
            if (normalized.Length < 1 || normalized.Length > 60) throw new ArgumentException("Profile names must be 1 to 60 characters.");
            return normalized;
        }
        private string ProfilePath(string normalizedName)
        {
            byte[] hash;
            using (SHA256 algorithm = SHA256.Create()) hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(normalizedName));
            StringBuilder filename = new StringBuilder(69);
            foreach (byte b in hash) filename.Append(b.ToString("x2"));
            filename.Append(".json");
            return Path.Combine(folder, filename.ToString());
        }

        public static void Validate(MacroWorkspaceProfile profile)
        {
            if (profile == null || profile.Version != 1) throw new ArgumentException("Unsupported library profile version.");
            NormalizeName(profile.Name);
            if (profile.Timers == null || profile.Sequence == null) throw new ArgumentException("A profile must contain timer and sequence settings.");
            (profile.Clicker ?? new QuickClickSettings()).Validate();
            SavedProfile timers = profile.Timers;
            SavedSequence sequence = profile.Sequence;
            if (timers.Version != 1 || sequence.Version != 1) throw new ArgumentException("Unsupported timer or sequence version.");
            if (timers.Actions == null || timers.Actions.Count > 100) throw new ArgumentException("Use at most 100 timer rows.");
            if (sequence.Steps == null || sequence.Steps.Count > 10000) throw new ArgumentException("Use at most 10000 sequence steps.");
            if (timers.CountdownSeconds < 1 || timers.CountdownSeconds > 30 || timers.SessionSeconds < 0 || timers.SessionSeconds > 86400)
                throw new ArgumentException("Invalid timer session settings.");
            if (sequence.CountdownSeconds < 1 || sequence.CountdownSeconds > 30 || sequence.RepeatCount < 0 || sequence.RepeatCount > 100000 ||
                sequence.LoopDelayMs < 0 || sequence.LoopDelayMs > 60000 || sequence.SessionSeconds < 0 || sequence.SessionSeconds > 86400)
                throw new ArgumentException("Invalid sequence session settings.");
            foreach (MacroAction action in timers.Actions)
            {
                if (action == null || NativeInput.NormalizeInput(action.Input) == null) throw new ArgumentException("A timer row has an unsupported input.");
                if (action.IntervalMs < 100 || action.IntervalMs > 86400000 || action.OffsetMs < 0 || action.OffsetMs > 86400000 ||
                    action.HoldMs < 10 || action.HoldMs > 5000 || action.HoldMs > action.IntervalMs || action.Burst < 1 || action.Burst > 20)
                    throw new ArgumentException("A timer row has invalid timing or burst settings.");
                if (action.KeepCooldownOnResume && action.Burst != 1)
                    throw new ArgumentException("Cooldown-preserving rows must use one press per cycle.");
                if (action.X.HasValue != action.Y.HasValue || action.X.HasValue &&
                    (action.X.Value < 0 || action.Y.Value < 0 || !NativeInput.IsMouseInput(NativeInput.NormalizeInput(action.Input))))
                    throw new ArgumentException("A timer row has invalid click coordinates.");
            }
            long duration = 0;
            foreach (MacroStep step in sequence.Steps)
            {
                if (step == null || NativeInput.NormalizeInput(step.Input) == null || step.DelayMs < 0 || step.DelayMs > 86400000)
                    throw new ArgumentException("A sequence step has invalid input or timing.");
                duration += step.DelayMs;
                if (duration > 86400000) throw new ArgumentException("A sequence can be at most 24 hours long.");
                if (step.X.HasValue != step.Y.HasValue || step.X.HasValue &&
                    (!step.IsDown || step.X.Value < 0 || step.Y.Value < 0 || !NativeInput.IsMouseInput(NativeInput.NormalizeInput(step.Input))))
                    throw new ArgumentException("A sequence step has invalid click coordinates.");
            }
            // Incomplete drafts may be saved. Replay separately validates that
            // every key/button down has exactly one matching release.
        }

        private List<string> ProfileFiles()
        {
            List<string> files = new List<string>();
            if (!Directory.Exists(folder)) return files;
            foreach (string path in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
            {
                files.Add(path);
                if (files.Count > MaxProfiles) throw new IOException("The library contains more than 100 profile files.");
            }
            return files;
        }
        public List<string> ListNames()
        {
            List<string> names = new List<string>();
            int skipped = 0;
            foreach (string path in ProfileFiles())
            {
                try
                {
                    MacroWorkspaceProfile profile = ReadProfile(path);
                    if (!String.Equals(path, ProfilePath(profile.Name), StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                    names.Add(profile.Name);
                }
                catch (InvalidDataException) { skipped++; }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
            names.Sort(delegate(string a, string b)
            {
                int comparison = StringComparer.OrdinalIgnoreCase.Compare(a, b);
                return comparison == 0 ? StringComparer.Ordinal.Compare(a, b) : comparison;
            });
            LastWarning = skipped == 0 ? null : "Skipped " + skipped + " unreadable or invalid profile file(s).";
            return names;
        }

        public void Save(MacroWorkspaceProfile profile)
        {
            Validate(profile);
            JavaScriptSerializer serializer = Serializer();
            // Detach the caller's mutable lists and never rewrite its name.
            MacroWorkspaceProfile snapshot = serializer.Deserialize<MacroWorkspaceProfile>(serializer.Serialize(profile));
            NormalizeSnapshot(snapshot);
            Validate(snapshot);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(serializer.Serialize(snapshot));
            if (bytes.Length > MaxBytes) throw new ArgumentException("A profile file can be at most 2 MB.");
            Directory.CreateDirectory(folder);
            string destination = ProfilePath(snapshot.Name);
            // Serialize writers across app instances. The lock file contains no
            // profile contents and remains local to this library folder.
            using (FileStream libraryLock = new FileStream(Path.Combine(folder, ".library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                List<string> files = ProfileFiles();
                if (!File.Exists(destination) && files.Count >= MaxProfiles) throw new IOException("The library can contain at most 100 profiles.");
                string temporary = Path.Combine(folder, ".profile-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    if (File.Exists(destination)) File.Replace(temporary, destination, null);
                    else File.Move(temporary, destination);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }

        public MacroWorkspaceProfile Load(string name)
        {
            string normalized = NormalizeName(name);
            MacroWorkspaceProfile profile = ReadProfile(ProfilePath(normalized));
            if (!String.Equals(profile.Name, normalized, StringComparison.Ordinal)) throw new InvalidDataException("The profile name does not match its file.");
            return profile;
        }
        private static void RequireVersion(Dictionary<string, object> item)
        {
            object value;
            if (item == null || !item.TryGetValue("Version", out value) || !(value is int) || (int)value != 1)
                throw new ArgumentException("Unsupported or missing profile version.");
        }
        private static MacroWorkspaceProfile ReadProfile(string path)
        {
            string json;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (stream.Length > MaxBytes) throw new InvalidDataException("A profile file can be at most 2 MB.");
                try { using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false, true), true)) json = reader.ReadToEnd(); }
                catch (DecoderFallbackException ex) { throw new InvalidDataException("The profile contains invalid text encoding.", ex); }
            }
            try
            {
                JavaScriptSerializer serializer = Serializer();
                Dictionary<string, object> document = serializer.DeserializeObject(json) as Dictionary<string, object>;
                RequireVersion(document);
                object timerObject, sequenceObject, nameObject;
                if (!document.TryGetValue("Name", out nameObject) || !(nameObject is string) ||
                    !document.TryGetValue("Timers", out timerObject) || !document.TryGetValue("Sequence", out sequenceObject))
                    throw new ArgumentException("A profile must include its name and both modes.");
                Dictionary<string, object> timers = timerObject as Dictionary<string, object>;
                Dictionary<string, object> sequence = sequenceObject as Dictionary<string, object>;
                RequireVersion(timers); RequireVersion(sequence);
                object rows, events;
                if (!timers.TryGetValue("Actions", out rows) || !(rows is IList) || ((IList)rows).Count > 100 ||
                    !sequence.TryGetValue("Steps", out events) || !(events is IList) || ((IList)events).Count > 10000)
                    throw new ArgumentException("Unsupported profile row counts.");
                MacroWorkspaceProfile profile;
                try { profile = serializer.Deserialize<MacroWorkspaceProfile>(json); }
                catch (Exception ex)
                {
                    // JavaScriptSerializer wraps some conversion failures in a
                    // bare Exception instead of ArgumentException (for example
                    // an Int32 overflow). Keep corrupt files isolated to their
                    // own entry while allowing runtime-fatal failures through.
                    if (ex is OutOfMemoryException || ex is StackOverflowException ||
                        ex is AccessViolationException || ex is System.Threading.ThreadAbortException) throw;
                    throw new InvalidDataException("Invalid profile field values.", ex);
                }
                Validate(profile);
                NormalizeSnapshot(profile);
                return profile;
            }
            catch (ArgumentException ex) { throw new InvalidDataException("Invalid profile file: " + ex.Message, ex); }
            catch (InvalidOperationException ex) { throw new InvalidDataException("Invalid profile file: " + ex.Message, ex); }
        }
        private static void NormalizeSnapshot(MacroWorkspaceProfile profile)
        {
            profile.Name = NormalizeName(profile.Name);
            if (profile.Clicker == null) profile.Clicker = new QuickClickSettings();
            profile.Clicker.Input = NativeInput.NormalizeInput(profile.Clicker.Input);
            foreach (MacroAction action in profile.Timers.Actions) action.Input = NativeInput.NormalizeInput(action.Input);
            foreach (MacroStep step in profile.Sequence.Steps) step.Input = NativeInput.NormalizeInput(step.Input);
        }
    }
}
