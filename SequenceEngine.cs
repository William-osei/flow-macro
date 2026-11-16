using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FarmMacro
{
    public sealed class MacroStep
    {
        public string Input { get; set; }
        public bool IsDown { get; set; }
        public int DelayMs { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public MacroStep Clone() { return (MacroStep)MemberwiseClone(); }
    }

    public sealed class SequenceEngine
    {
        private readonly IInputBackend backend;
        private readonly Func<long> clock;
        private readonly List<MacroStep> steps = new List<MacroStep>();
        private IntPtr target;
        private long readyAt;
        private long nextAt;
        private long nextPressAt;
        private long previousTick;
        private long activeMs;
        private int index;
        private int completedLoops;
        private int repeats;
        private int loopDelay;
        private int limit;
        private bool wasForeground;
        private bool previousWasActive;

        public SequenceEngine(IInputBackend backend) : this(backend, CreateClock()) { }
        public SequenceEngine(IInputBackend backend, Func<long> clockMs)
        {
            if (backend == null || clockMs == null) throw new ArgumentNullException();
            this.backend = backend;
            clock = clockMs;
            Status = "Stopped";
        }
        private static Func<long> CreateClock()
        {
            Stopwatch watch = Stopwatch.StartNew();
            return delegate { return watch.ElapsedMilliseconds; };
        }

        public bool IsArmed { get; private set; }
        public bool IsSending { get; private set; }
        public string Status { get; private set; }
        public long TotalActions { get; private set; }
        public TimeSpan ActiveDuration { get { return TimeSpan.FromMilliseconds(activeMs); } }

        public void Start(IntPtr targetWindow, IEnumerable<MacroStep> sequence, int countdownMs, int repeatCount, int loopDelayMs, int sessionLimitMs)
        {
            if (IsArmed) Stop();
            if (targetWindow == IntPtr.Zero) throw new ArgumentException("Choose a target window.");
            if (countdownMs < 0 || countdownMs > 60000) throw new ArgumentException("Countdown must be 0 to 60 seconds.");
            if (repeatCount < 0 || repeatCount > 1000000) throw new ArgumentException("Repeat count must be 0 to 1000000; 0 repeats forever.");
            if (loopDelayMs < 0 || loopDelayMs > 86400000) throw new ArgumentException("Loop delay must be 0 to 86400 seconds.");
            if (sessionLimitMs < 0) throw new ArgumentException("Session limit cannot be negative.");
            if (sequence == null) throw new ArgumentNullException("sequence");
            List<MacroStep> validated = new List<MacroStep>();
            HashSet<string> held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long duration = 0;
            foreach (MacroStep source in sequence)
            {
                if (source == null) throw new ArgumentException("Sequence steps cannot be empty.");
                MacroStep step = source.Clone();
                step.Input = NativeInput.NormalizeInput(step.Input);
                if (step.Input == null) throw new ArgumentException("Choose a supported input for every step.");
                if (step.DelayMs < 0 || step.DelayMs > 86400000) throw new ArgumentException("Step delays must be 0 to 86400 seconds.");
                duration += step.DelayMs;
                if (duration > 86400000) throw new ArgumentException("A sequence can be at most 24 hours long.");
                if (step.X.HasValue != step.Y.HasValue) throw new ArgumentException("Enter both X and Y, or leave both blank.");
                if (step.X.HasValue && (!step.IsDown || !NativeInput.IsMouseInput(step.Input) || step.X.Value < 0 || step.Y.Value < 0))
                    throw new ArgumentException("Only mouse-down steps can have nonnegative coordinates.");
                if (step.IsDown)
                {
                    if (!held.Add(step.Input)) throw new ArgumentException(step.Input + " is pressed twice without a release.");
                }
                else if (!held.Remove(step.Input)) throw new ArgumentException(step.Input + " is released without a preceding press.");
                validated.Add(step);
                if (validated.Count > 10000) throw new ArgumentException("Use at most 10000 sequence steps.");
            }
            if (validated.Count == 0) throw new ArgumentException("Add or record at least one press and release.");
            if (held.Count > 0) throw new ArgumentException("Every pressed input must have a release before the end of the sequence.");
            backend.BindTarget(targetWindow);
            backend.ReleaseAll();
            steps.Clear(); steps.AddRange(validated);
            target = targetWindow;
            repeats = repeatCount;
            loopDelay = Math.Max(100, loopDelayMs);
            limit = sessionLimitMs;
            completedLoops = 0;
            index = 0;
            TotalActions = 0;
            activeMs = 0;
            long now = clock();
            readyAt = now + countdownMs;
            nextAt = readyAt + steps[0].DelayMs;
            nextPressAt = readyAt;
            previousTick = now;
            wasForeground = false;
            previousWasActive = false;
            IsArmed = true;
            IsSending = false;
            Status = countdownMs > 0 ? "Starting in " + ((countdownMs + 999) / 1000) + "s" : "Waiting for target focus";
        }

        public void Stop() { Halt("Stopped"); }
        private void Halt(string status)
        {
            IsArmed = false;
            IsSending = false;
            wasForeground = false;
            previousWasActive = false;
            Status = status;
            try { backend.ReleaseAll(); }
            catch (Exception ex) { Status += " - release failed: " + ex.Message; }
        }
        public void Tick()
        {
            if (!IsArmed) return;
            try { TickCore(); }
            catch (Exception ex) { Halt("Stopped - " + ex.Message); }
        }
        private void TickCore()
        {
            long now = clock();
            long delta = Math.Max(0, now - previousTick);
            previousTick = now;
            if (now < readyAt)
            {
                Status = "Starting in " + ((readyAt - now + 999) / 1000) + "s";
                return;
            }
            if (!backend.IsTargetForeground(target))
            {
                PauseForFocus();
                return;
            }
            if (previousWasActive) activeMs += delta;
            if (limit > 0 && activeMs >= limit) { Halt("Session limit reached"); return; }
            previousWasActive = true;
            if (!wasForeground)
            {
                index = 0;
                nextAt = now + steps[0].DelayMs;
                nextPressAt = now;
                wasForeground = true;
            }
            IsSending = true;
            Status = "Playing loop " + (completedLoops + 1) + " - step " + (index + 1) + "/" + steps.Count;
            bool pressedThisTick = false;
            while (IsArmed && now >= nextAt)
            {
                MacroStep step = steps[index];
                if (step.IsDown)
                {
                    if (pressedThisTick || now < nextPressAt) return;
                    if (!backend.IsTargetForeground(target)) { PauseForFocus(); return; }
                    MacroAction action = new MacroAction { Enabled = true, Input = step.Input, IntervalMs = 100, HoldMs = 10, Burst = 1, X = step.X, Y = step.Y };
                    IInputReadiness readiness = backend as IInputReadiness;
                    string waitReason;
                    if (readiness != null && !readiness.CanSend(action, out waitReason))
                    {
                        backend.ReleaseAll();
                        wasForeground = false; previousWasActive = false; IsSending = false;
                        Status = waitReason; return;
                    }
                    if (!backend.Press(target, action))
                    {
                        if (!backend.IsTargetForeground(target)) { PauseForFocus(); return; }
                        // An occluded/out-of-range click must not leave earlier
                        // chord keys held or continue a partially executed loop.
                        Halt("Stopped - click is outside or covered by another window");
                        return;
                    }
                    TotalActions++;
                    nextPressAt = now + 20;
                    pressedThisTick = true;
                }
                else backend.Release(step.Input);
                index++;
                if (index == steps.Count)
                {
                    completedLoops++;
                    if (repeats > 0 && completedLoops >= repeats)
                    {
                        Halt("Completed " + completedLoops + (completedLoops == 1 ? " loop" : " loops"));
                        return;
                    }
                    index = 0;
                    nextAt = now + loopDelay + steps[0].DelayMs;
                    return;
                }
                // Schedule from actual execution, rather than issuing overdue
                // input in a catch-up burst after a delayed UI tick.
                nextAt = now + steps[index].DelayMs;
            }
        }
        private void PauseForFocus()
        {
            if (wasForeground) backend.ReleaseAll();
            wasForeground = false;
            previousWasActive = false;
            IsSending = false;
            Status = "Paused - focus the selected window; current loop restarts";
        }
    }
}
