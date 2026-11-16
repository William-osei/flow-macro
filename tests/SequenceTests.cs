using System;
using System.Collections.Generic;
using FarmMacro;

internal sealed class SequenceInput : IInputBackend, IInputReadiness
{
    public bool Focus = true;
    public bool Accept = true;
    public bool Ready = true;
    public bool CanSend(MacroAction action, out string reason) { reason = "Waiting - release modifiers"; return Ready; }
    public bool FailPress;
    public int BindCount;
    public readonly List<string> Events = new List<string>();
    public readonly HashSet<string> Held = new HashSet<string>();
    public void BindTarget(IntPtr target) { BindCount++; }
    public bool IsTargetForeground(IntPtr target) { return Focus; }
    public bool Press(IntPtr target, MacroAction action)
    {
        if (FailPress) throw new InvalidOperationException("press failure");
        if (!Accept) return false;
        Events.Add("down:" + action.Input); Held.Add(action.Input); return true;
    }
    public void Release(string input) { Events.Add("up:" + input); Held.Remove(input); }
    public void ReleaseAll() { foreach (string input in new List<string>(Held)) Release(input); }
}

internal sealed class RecordingSource : IRecordingSource
{
    public bool Focus = true;
    public bool SafePoint = true;
    public bool DropFocusOnRead;
    public HashSet<string> Pressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public void BindTarget(IntPtr target) { }
    public bool IsTargetForeground(IntPtr target) { return Focus; }
    public HashSet<string> ReadPressedInputs()
    {
        if (DropFocusOnRead) Focus = false;
        return new HashSet<string>(Pressed, StringComparer.OrdinalIgnoreCase);
    }
    public bool TryGetMousePosition(IntPtr target, out int x, out int y) { x = 123; y = 456; return SafePoint; }
}

internal static class SequenceTests
{
    private static long time;
    private static int passed;
    private static MacroStep Step(string input, bool down, int delay)
    {
        return new MacroStep { Input = input, IsDown = down, DelayMs = delay };
    }
    private static SequenceEngine Engine(SequenceInput backend)
    {
        time = 0; return new SequenceEngine(backend, delegate { return time; });
    }
    private static MacroRecorder Recorder(RecordingSource source)
    {
        time = 0; return new MacroRecorder(source, delegate { return time; });
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAILED: " + message); }
    private static void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static MacroStep[] Tap(string input, int hold)
    {
        return new[] { Step(input, true, 0), Step(input, false, hold) };
    }
    public static int Main()
    {
        try
        {
            Run("Sequence countdown, explicit hold, and C/V pacing", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Step("C", true, 0), Step("C", false, 100), Step("V", true, 800), Step("V", false, 50) }, 1000, 1, 0, 0);
                time = 999; engine.Tick(); Check(engine.TotalActions == 0, "no countdown input");
                time = 1000; engine.Tick(); Check(fake.Held.Contains("C"), "C starts");
                time = 1099; engine.Tick(); Check(fake.Held.Contains("C"), "C held");
                time = 1100; engine.Tick(); Check(fake.Held.Count == 0, "C released");
                time = 1899; engine.Tick(); Check(engine.TotalActions == 1, "V waits");
                time = 1900; engine.Tick(); Check(fake.Held.Contains("V"), "V delayed down");
                time = 1950; engine.Tick(); Check(!engine.IsArmed && fake.Held.Count == 0 && engine.TotalActions == 2, "completed safely");
            });
            Run("Sequence chord preserves held modifier", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Step("Ctrl", true, 0), Step("C", true, 0), Step("C", false, 50), Step("Ctrl", false, 0) }, 0, 1, 0, 0);
                engine.Tick(); Check(fake.Held.SetEquals(new[] { "Ctrl" }), "modifier first");
                time = 20; engine.Tick(); Check(fake.Held.SetEquals(new[] { "Ctrl", "C" }), "chord down");
                time = 70; engine.Tick(); Check(fake.Held.Count == 0 && !engine.IsArmed, "chord released");
            });
            Run("Focus pause releases and restarts current sequence loop", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Step("C", true, 100), Step("C", false, 500) }, 0, 1, 0, 0);
                engine.Tick(); time = 100; engine.Tick(); fake.Focus = false; time = 120; engine.Tick();
                Check(fake.Held.Count == 0 && engine.IsArmed, "pause releases");
                time = 10000; engine.Tick(); fake.Focus = true; engine.Tick();
                Check(engine.TotalActions == 1, "initial delay restarted");
                time = 10100; engine.Tick(); Check(engine.TotalActions == 2 && fake.Held.Contains("C"), "restarted at first step");
            });
            Run("Unexpected modifiers release and restart a sequence safely", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Step("Ctrl", true, 0), Step("C", true, 20), Step("C", false, 150), Step("Ctrl", false, 0) }, 0, 1, 0, 0);
                engine.Tick(); Check(fake.Held.Contains("Ctrl"), "intentional modifier held");
                fake.Ready = false; time = 20; engine.Tick();
                Check(fake.Held.Count == 0 && !engine.IsSending && engine.IsArmed, "wait releases owned holds");
                fake.Ready = true; time = 10000; engine.Tick();
                Check(fake.Held.Contains("Ctrl") && !fake.Held.Contains("C"), "restarts current sequence");
                engine.Stop(); Check(fake.Held.Count == 0, "manual stop releases");
            });
            Run("Repeat count and minimum loop gap", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), Tap("C", 0), 0, 2, 0, 0);
                engine.Tick(); Check(engine.TotalActions == 1 && engine.IsArmed && fake.Held.Count == 0, "loop one");
                time = 99; engine.Tick(); Check(engine.TotalActions == 1, "minimum loop delay");
                time = 100; engine.Tick(); Check(engine.TotalActions == 2 && !engine.IsArmed, "repeat completion");
            });
            Run("Unlimited repeat runs beyond 20 seconds and still stops safely", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), Tap("Z", 500), 0, 0, 100, 0);
                for (time = 0; time <= 65000; time += 20) engine.Tick();
                Check(engine.IsArmed && engine.TotalActions > 100 && engine.ActiveDuration.TotalSeconds >= 65,
                    "zero repeat count and session limit stay active beyond 20 seconds");
                Check(fake.Held.Contains("Z"), "test stops during a held input after many loops");
                engine.Stop(); Check(fake.Held.Count == 0 && !engine.IsArmed, "manual stop releases after unlimited playback");
            });
            Run("Sequence session limit excludes focus pauses", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), Tap("C", 1000), 0, 0, 100, 500);
                engine.Tick(); time = 100; engine.Tick(); fake.Focus = false; time = 200; engine.Tick();
                time = 5000; engine.Tick(); Check(engine.IsArmed && engine.ActiveDuration.TotalMilliseconds == 100, "pause time excluded");
                fake.Focus = true; engine.Tick(); time = 5400; engine.Tick();
                Check(!engine.IsArmed && engine.ActiveDuration.TotalMilliseconds == 500 && fake.Held.Count == 0, "active limit");
            });
            Run("Sequence rejects unbalanced or duplicate downs before binding", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake); int failures = 0;
                foreach (MacroStep[] invalid in new[] { new[] { Step("C", true, 0) }, new[] { Step("C", false, 0) }, new[] { Step("C", true, 0), Step("C", true, 0), Step("C", false, 0) } })
                {
                    try { engine.Start(new IntPtr(1), invalid, 0, 1, 0, 0); } catch (ArgumentException) { failures++; }
                }
                Check(failures == 3 && fake.BindCount == 0, "balanced validation");
            });
            Run("Sequence snapshots editable steps", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake); MacroStep[] steps = Tap("C", 100);
                engine.Start(new IntPtr(1), steps, 0, 1, 0, 0); steps[0].Input = "V";
                engine.Tick(); Check(fake.Held.Contains("C") && !fake.Held.Contains("V"), "snapshot");
            });
            Run("Sequence limits due presses to one per tick", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Step("C", true, 0), Step("V", true, 0), Step("C", false, 0), Step("V", false, 0) }, 0, 1, 0, 0);
                engine.Tick(); Check(engine.TotalActions == 1, "one first down");
                time = 10; engine.Tick(); Check(engine.TotalActions == 1, "bounded press rate");
                time = 20; engine.Tick(); Check(engine.TotalActions == 2 && !engine.IsArmed && fake.Held.Count == 0, "due releases drain");
            });
            Run("Unsafe mouse click aborts and releases preceding chord", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Step("Shift", true, 0), Step("Left click", true, 100), Step("Left click", false, 50), Step("Shift", false, 0) }, 0, 1, 0, 0);
                engine.Tick(); fake.Accept = false; time = 100; engine.Tick();
                Check(!engine.IsArmed && fake.Held.Count == 0, "unsafe click releases");
            });
            Run("Sequence input error stops and releases", delegate
            {
                SequenceInput fake = new SequenceInput(); SequenceEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Step("C", true, 0), Step("V", true, 100), Step("V", false, 50), Step("C", false, 0) }, 0, 1, 0, 0);
                engine.Tick(); fake.FailPress = true; time = 100; engine.Tick();
                Check(!engine.IsArmed && fake.Held.Count == 0 && engine.Status.Contains("press failure"), "input error");
            });
            Run("Recorder ignores initially held keys and balances stop", delegate
            {
                RecordingSource fake = new RecordingSource(); fake.Pressed.Add("C"); MacroRecorder recorder = Recorder(fake);
                recorder.Start(new IntPtr(1)); recorder.Tick(); time = 100; recorder.Tick(); Check(recorder.Steps.Count == 0, "initial hold ignored");
                fake.Pressed.Clear(); time = 200; recorder.Tick(); fake.Pressed.Add("C"); time = 300; recorder.Tick();
                time = 400; recorder.Tick(); recorder.Stop(); List<MacroStep> result = recorder.Steps;
                Check(result.Count == 2 && result[0].IsDown && !result[1].IsDown && result[1].DelayMs == 100, "stop creates release");
            });
            Run("Recorder orders simultaneous modifiers before chord keys", delegate
            {
                RecordingSource fake = new RecordingSource(); MacroRecorder recorder = Recorder(fake); recorder.Start(new IntPtr(1)); recorder.Tick();
                fake.Pressed.Add("Ctrl"); fake.Pressed.Add("C"); time = 100; recorder.Tick();
                fake.Pressed.Clear(); time = 200; recorder.Tick(); recorder.Stop(); List<MacroStep> result = recorder.Steps;
                Check(result.Count == 4 && result[0].Input == "Ctrl" && result[1].Input == "C" && result[2].Input == "C" && result[3].Input == "Ctrl", "chord order");
            });
            Run("Recorder focus pause balances holds and drops off-focus input", delegate
            {
                RecordingSource fake = new RecordingSource(); MacroRecorder recorder = Recorder(fake); recorder.Start(new IntPtr(1)); recorder.Tick();
                fake.Pressed.Add("C"); time = 100; recorder.Tick(); fake.Focus = false; time = 120; recorder.Tick();
                fake.Pressed.Clear(); fake.Pressed.Add("V"); time = 10000; recorder.Tick(); Check(recorder.Steps.Count == 2, "only target down plus forced release");
                fake.Focus = true; recorder.Tick(); time = 10100; recorder.Tick(); Check(recorder.Steps.Count == 2, "off-focus held V ignored");
                fake.Pressed.Clear(); time = 10200; recorder.Tick(); fake.Pressed.Add("V"); time = 10300; recorder.Tick(); recorder.Stop();
                List<MacroStep> result = recorder.Steps; Check(result.Count == 4 && result[2].Input == "V" && result[2].DelayMs == 300, "off-focus delay removed");
            });
            Run("Recorder captures safe client click positions only", delegate
            {
                RecordingSource fake = new RecordingSource(); MacroRecorder recorder = Recorder(fake); recorder.Start(new IntPtr(1)); recorder.Tick();
                fake.SafePoint = false; fake.Pressed.Add("Left click"); time = 100; recorder.Tick();
                fake.SafePoint = true; time = 200; recorder.Tick(); Check(recorder.Steps.Count == 0, "unsafe held click remains ignored");
                fake.Pressed.Clear(); time = 300; recorder.Tick(); fake.Pressed.Add("Left click"); time = 400; recorder.Tick();
                fake.SafePoint = false; fake.Pressed.Clear(); time = 500; recorder.Tick(); recorder.Stop(); List<MacroStep> result = recorder.Steps;
                Check(result.Count == 2 && result[0].X == 123 && result[0].Y == 456 && !result[1].X.HasValue, "position only on down");
            });
            Run("Recorder snapshots cannot mutate recording", delegate
            {
                RecordingSource fake = new RecordingSource(); MacroRecorder recorder = Recorder(fake); recorder.Start(new IntPtr(1)); recorder.Tick();
                fake.Pressed.Add("C"); time = 100; recorder.Tick(); List<MacroStep> snapshot = recorder.Steps; snapshot[0].Input = "V"; snapshot.Clear();
                Check(recorder.Steps.Count == 1 && recorder.Steps[0].Input == "C", "deep snapshot"); recorder.Stop();
            });
            Run("Recorder auto-stop reserves balanced release slots", delegate
            {
                RecordingSource fake = new RecordingSource(); MacroRecorder recorder = Recorder(fake); recorder.Start(new IntPtr(1)); recorder.Tick();
                for (int i = 0; i < 5001 && recorder.IsRecording; i++)
                {
                    fake.Pressed.Add("C"); time += 20; recorder.Tick();
                    fake.Pressed.Clear(); time += 20; recorder.Tick();
                }
                List<MacroStep> result = recorder.Steps;
                Check(!recorder.IsRecording && result.Count == 10000 && !result[result.Count - 1].IsDown, "recording bounded and balanced");
            });
            Run("Recorder time limit returns balanced steps", delegate
            {
                RecordingSource fake = new RecordingSource(); MacroRecorder recorder = Recorder(fake); recorder.Start(new IntPtr(1)); recorder.Tick();
                fake.Pressed.Add("C"); time = 100; recorder.Tick(); time = 864000000; recorder.Tick();
                Check(!recorder.IsRecording && recorder.Steps.Count == 2 && !recorder.Steps[1].IsDown, "recording time bound");
                Check(recorder.Steps[0].DelayMs + recorder.Steps[1].DelayMs == 1800000, "long sleep does not exceed duration bound");
            });
            Run("Recorder discards a poll if focus changes during sampling", delegate
            {
                RecordingSource fake = new RecordingSource(); MacroRecorder recorder = Recorder(fake); recorder.Start(new IntPtr(1)); recorder.Tick();
                fake.Pressed.Add("C"); fake.DropFocusOnRead = true; time = 100; recorder.Tick();
                Check(recorder.IsRecording && recorder.Steps.Count == 0 && recorder.Status.Contains("paused"), "changed-focus poll discarded");
            });
            Console.WriteLine(passed + " sequence/recorder tests passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
