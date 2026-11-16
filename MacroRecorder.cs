using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FarmMacro
{
    public interface IRecordingSource
    {
        void BindTarget(IntPtr target);
        bool IsTargetForeground(IntPtr target);
        HashSet<string> ReadPressedInputs();
        bool TryGetMousePosition(IntPtr target, out int x, out int y);
    }

    // Foreground-only polling recorder. It installs no hooks and records no text
    // or input from other apps; very short taps between UI ticks may be missed.
    public sealed class MacroRecorder
    {
        private readonly IRecordingSource source;
        private readonly Func<long> clock;
        private readonly List<MacroStep> steps = new List<MacroStep>();
        private readonly HashSet<string> held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private IntPtr target;
        private long activeMs;
        private long lastEventAt;
        private long previousTick;
        private bool wasForeground;

        public MacroRecorder(NativeInput backend) : this((IRecordingSource)backend, CreateClock()) { }
        public MacroRecorder(IRecordingSource backend, Func<long> clockMs)
        {
            if (backend == null || clockMs == null) throw new ArgumentNullException();
            source = backend;
            clock = clockMs;
            Status = "Not recording";
        }
        private static Func<long> CreateClock()
        {
            Stopwatch watch = Stopwatch.StartNew();
            return delegate { return watch.ElapsedMilliseconds; };
        }
        public bool IsRecording { get; private set; }
        public string Status { get; private set; }
        public int StepCount { get { return steps.Count; } }
        public TimeSpan ActiveDuration { get { return TimeSpan.FromMilliseconds(activeMs); } }
        public List<MacroStep> Steps
        {
            get
            {
                List<MacroStep> snapshot = new List<MacroStep>();
                foreach (MacroStep step in steps) snapshot.Add(step.Clone());
                return snapshot;
            }
        }
        public void Start(IntPtr targetWindow)
        {
            if (IsRecording) Stop();
            if (targetWindow == IntPtr.Zero) throw new ArgumentException("Choose a target window.");
            source.BindTarget(targetWindow);
            target = targetWindow;
            steps.Clear(); held.Clear(); ignored.Clear();
            if (source.IsTargetForeground(target))
                foreach (string input in source.ReadPressedInputs()) ignored.Add(input);
            activeMs = 0;
            lastEventAt = 0;
            previousTick = clock();
            wasForeground = false;
            IsRecording = true;
            Status = "Recording - focus the selected window";
        }
        public void Stop()
        {
            if (!IsRecording) return;
            Finish("Recording stopped");
        }
        private void Finish(string reason)
        {
            AppendReleases();
            IsRecording = false;
            wasForeground = false;
            Status = reason + " - " + steps.Count + " steps";
        }
        public void Tick()
        {
            if (!IsRecording) return;
            try { TickCore(); }
            catch (Exception ex) { Finish("Recording stopped - " + ex.Message); }
        }
        private void TickCore()
        {
            long now = clock();
            long delta = Math.Max(0, now - previousTick);
            previousTick = now;
            bool foreground = source.IsTargetForeground(target);
            if (!foreground)
            {
                PauseForFocus();
                return;
            }
            HashSet<string> pressed = source.ReadPressedInputs();
            if (!source.IsTargetForeground(target)) { PauseForFocus(); return; }
            if (!wasForeground)
            {
                // Ignore keys held before the target acquired focus, including
                // keys typed in another app while recording was paused.
                ignored.Clear();
                foreach (string input in pressed) ignored.Add(input);
            }
            else activeMs = Math.Min(1800000, activeMs + delta);
            wasForeground = true;
            if (activeMs >= 1800000) { Finish("Recording reached the 30-minute limit"); return; }
            ignored.RemoveWhere(delegate(string input) { return !pressed.Contains(input); });

            // End non-modifiers first, modifiers last; start modifiers first so
            // a chord sampled in one tick replays in a useful order.
            foreach (string input in OrderedInputs(false))
            {
                if (held.Contains(input) && !pressed.Contains(input)) Append(input, false, null, null);
            }
            foreach (string input in OrderedInputs(true))
            {
                if (!pressed.Contains(input) || held.Contains(input) || ignored.Contains(input)) continue;
                if (!source.IsTargetForeground(target)) { PauseForFocus(); return; }
                int? x = null; int? y = null;
                if (NativeInput.IsMouseInput(input))
                {
                    int cursorX, cursorY;
                    if (!source.TryGetMousePosition(target, out cursorX, out cursorY))
                    {
                        ignored.Add(input);
                        continue;
                    }
                    x = cursorX; y = cursorY;
                }
                // Reserve a release slot for every down, so auto-stop always
                // returns a balanced sequence within the 10000-step limit.
                if (steps.Count + held.Count + 2 > 10000)
                {
                    Finish("Recording reached the 10000-step limit");
                    return;
                }
                Append(input, true, x, y);
            }
            Status = "Recording - " + steps.Count + " steps";
        }
        private void PauseForFocus()
        {
            if (wasForeground) AppendReleases();
            wasForeground = false;
            Status = "Recording paused - focus the selected window";
        }
        private static bool IsModifier(string input)
        {
            return input == "Ctrl" || input == "Alt" || input == "Shift";
        }
        private static List<string> OrderedInputs(bool down)
        {
            List<string> result = new List<string>();
            foreach (string input in NativeInput.SupportedInputs)
                if (IsModifier(input) == down) result.Add(input);
            foreach (string input in NativeInput.SupportedInputs)
                if (IsModifier(input) != down) result.Add(input);
            return result;
        }
        private void Append(string input, bool down, int? x, int? y)
        {
            int delay = (int)Math.Min(86400000, Math.Max(0, activeMs - lastEventAt));
            steps.Add(new MacroStep { Input = input, IsDown = down, DelayMs = delay, X = x, Y = y });
            lastEventAt = activeMs;
            if (down) held.Add(input); else held.Remove(input);
        }
        private void AppendReleases()
        {
            foreach (string input in OrderedInputs(false))
                if (held.Contains(input)) Append(input, false, null, null);
        }
    }
}
