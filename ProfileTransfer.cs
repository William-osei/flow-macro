using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace FarmMacro
{
    // Portable files are detached from the named library. Import validates only;
    // it never persists settings, selects a target, or starts playback.
    public static class ProfileTransfer
    {
        private const int MaxBytes = 2 * 1024 * 1024;

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 32 };
        }
        private static bool IsFatal(Exception ex)
        {
            return ex is OutOfMemoryException || ex is StackOverflowException ||
                ex is AccessViolationException || ex is System.Threading.ThreadAbortException;
        }
        private static void NormalizeSnapshot(MacroWorkspaceProfile profile)
        {
            profile.Name = profile.Name.Trim().Normalize(NormalizationForm.FormC);
            if (profile.Clicker == null) profile.Clicker = new QuickClickSettings();
            profile.Version = profile.Clicker.UseMultiplePoints || profile.Clicker.Points.Count > 0 ? 2 : 1;
            profile.Clicker.Input = NativeInput.NormalizeInput(profile.Clicker.Input);
            foreach (MacroAction action in profile.Timers.Actions) action.Input = NativeInput.NormalizeInput(action.Input);
            foreach (MacroStep step in profile.Sequence.Steps) step.Input = NativeInput.NormalizeInput(step.Input);
        }
        private static string FullPath(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Choose a profile file.");
            string full = Path.GetFullPath(filePath);
            if (String.IsNullOrEmpty(Path.GetFileName(full))) throw new ArgumentException("Choose a profile filename.");
            return full;
        }

        public static void Export(MacroWorkspaceProfile profile, string filePath)
        {
            ProfileLibrary.Validate(profile);
            string destination = FullPath(filePath);
            string folder = Path.GetDirectoryName(destination);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Choose an existing destination folder.");
            JavaScriptSerializer serializer = Serializer();
            MacroWorkspaceProfile snapshot = serializer.Deserialize<MacroWorkspaceProfile>(serializer.Serialize(profile));
            NormalizeSnapshot(snapshot);
            ProfileLibrary.Validate(snapshot);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(serializer.Serialize(snapshot));
            if (bytes.Length > MaxBytes) throw new ArgumentException("A portable profile can be at most 2 MB.");

            string temporary = Path.Combine(folder, ".farmmacro-export-" + Guid.NewGuid().ToString("N") + ".tmp");
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

        public static MacroWorkspaceProfile Import(string filePath)
        {
            string path = FullPath(filePath);
            byte[] bytes;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (stream.Length > MaxBytes) throw new InvalidDataException("A portable profile can be at most 2 MB.");
                bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int count = stream.Read(bytes, offset, bytes.Length - offset);
                    if (count == 0) throw new InvalidDataException("The profile file ended before it was fully read.");
                    offset += count;
                }
            }
            int bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            string json;
            try { json = new UTF8Encoding(false, true).GetString(bytes, bom, bytes.Length - bom); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException("The profile must contain valid UTF-8 text.", ex); }

            try
            {
                JavaScriptSerializer serializer = Serializer();
                Dictionary<string, object> document = serializer.DeserializeObject(json) as Dictionary<string, object>;
                RequireVersion(document, true);
                ProfileLibrary.RequirePointDocument(document);
                object name, timerObject, sequenceObject;
                if (!document.TryGetValue("Name", out name) || !(name is string) ||
                    !document.TryGetValue("Timers", out timerObject) || !document.TryGetValue("Sequence", out sequenceObject))
                    throw new ArgumentException("The profile must include its name and both modes.");
                Dictionary<string, object> timers = timerObject as Dictionary<string, object>;
                Dictionary<string, object> sequence = sequenceObject as Dictionary<string, object>;
                RequireVersion(timers); RequireVersion(sequence);
                object actions, steps;
                if (!timers.TryGetValue("Actions", out actions) || !(actions is IList) || ((IList)actions).Count > 100 ||
                    !sequence.TryGetValue("Steps", out steps) || !(steps is IList) || ((IList)steps).Count > 10000)
                    throw new ArgumentException("The profile contains unsupported row counts.");
                MacroWorkspaceProfile profile = serializer.Deserialize<MacroWorkspaceProfile>(json);
                ProfileLibrary.Validate(profile);
                ProfileLibrary.RequireRouteVersion(profile);
                NormalizeSnapshot(profile);
                return profile;
            }
            catch (Exception ex)
            {
                if (IsFatal(ex)) throw;
                throw new InvalidDataException("Invalid portable profile: " + ex.Message, ex);
            }
        }

        private static void RequireVersion(Dictionary<string, object> document, bool workspace = false)
        {
            object version;
            if (document == null || !document.TryGetValue("Version", out version) || !(version is int) ||
                ((int)version != 1 && (!workspace || (int)version != 2)))
                throw new ArgumentException("Unsupported or missing profile version.");
        }
    }
}
