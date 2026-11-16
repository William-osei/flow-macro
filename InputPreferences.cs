using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace FarmMacro
{
    public sealed class InputPreferences
    {
        public int Version = 1;
        public KeyboardInputMode KeyboardMode = KeyboardInputMode.VirtualKey;
        static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FarmMacro", "input.json"); } }
        public static InputPreferences Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new InputPreferences();
                InputPreferences result = new JavaScriptSerializer().Deserialize<InputPreferences>(File.ReadAllText(FilePath));
                if (result == null || result.Version != 1 || !Enum.IsDefined(typeof(KeyboardInputMode), result.KeyboardMode)) return new InputPreferences();
                return result;
            }
            catch (IOException) { return new InputPreferences(); }
            catch (UnauthorizedAccessException) { return new InputPreferences(); }
            catch (ArgumentException) { return new InputPreferences(); }
            catch (InvalidOperationException) { return new InputPreferences(); }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException || ex is StackOverflowException || ex is AccessViolationException || ex is System.Threading.ThreadAbortException) throw;
                return new InputPreferences();
            }
        }
        public void Save()
        {
            if (Version != 1 || !Enum.IsDefined(typeof(KeyboardInputMode), KeyboardMode)) throw new ArgumentException("Unsupported input settings.");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null); else File.Move(temporary, FilePath);
        }
    }
}
