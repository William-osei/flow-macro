using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FarmMacro
{
    public sealed class MacroAction
    {
        public bool Enabled { get; set; }
        public string Input { get; set; }
        public int IntervalMs { get; set; }
        public int OffsetMs { get; set; }
        public int HoldMs { get; set; }
        public int Burst { get; set; }
        public bool KeepCooldownOnResume { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }

        public MacroAction Clone() { return (MacroAction)MemberwiseClone(); }
    }

    public interface IInputBackend
    {
        void BindTarget(IntPtr target);
        bool IsTargetForeground(IntPtr target);
        // False means an unsafe mouse position or a lost focus; no input was sent.
        bool Press(IntPtr target, MacroAction action);
        void Release(string input);
        void ReleaseAll();
    }

    // UI-thread state machine: no sleeps, worker threads, or queued catch-up input.
    public sealed class MacroEngine
    {
        public const long MaximumActions = 1000000000L;
        private sealed class ScheduledAction
        {
            public MacroAction Action;
            public long NextAt;
            public long CycleAt;
            public int Remaining;
            public bool HasPressed;
            public long CooldownUntil;
        }

        private sealed class HeldInput
        {
            public string Input;
            public long ReleaseAt;
        }

        private readonly IInputBackend backend;
        private readonly Func<long> clock;
        private readonly List<ScheduledAction> scheduled = new List<ScheduledAction>();
        private readonly List<HeldInput> held = new List<HeldInput>();
        private IntPtr target;
        private long readyAt;
        private long startedAt;
        private long previousTick;
        private long activeMs;
        private long nextPressAt;
        private int limitMs;
        private long actionLimit;
        private int nextRow;
        private bool wasForeground;
        private bool previousWasActive;
        private List<ClickPoint> orderedPoints;
        private int orderedPoint;

        public MacroEngine(IInputBackend backend)
            : this(backend, CreateClock()) { }

        public MacroEngine(IInputBackend backend, Func<long> clockMs)
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

        public void Start(IntPtr targetWindow, IEnumerable<MacroAction> actions, int countdownMs, int sessionLimitMs)
        {
            Start(targetWindow, actions, countdownMs, sessionLimitMs, 0);
        }

        public void Start(IntPtr targetWindow, IEnumerable<MacroAction> actions, int countdownMs, int sessionLimitMs, long maxActions)
        {
            if (IsArmed) Stop();
            orderedPoints = null;
            orderedPoint = 0;
            if (targetWindow == IntPtr.Zero) throw new ArgumentException("Choose a target window.");
            if (countdownMs < 0 || countdownMs > 60000) throw new ArgumentException("Countdown must be 0 to 60 seconds.");
            if (sessionLimitMs < 0) throw new ArgumentException("Session limit cannot be negative.");
            if (maxActions < 0 || maxActions > MaximumActions) throw new ArgumentException("Action limit must be 0 to " + MaximumActions + "; 0 runs indefinitely.");
            if (actions == null) throw new ArgumentNullException("actions");
            List<ScheduledAction> validated = new List<ScheduledAction>();
            foreach (MacroAction source in actions)
            {
                if (source == null || !source.Enabled) continue;
                MacroAction action = source.Clone();
                action.Input = NativeInput.NormalizeInput(action.Input);
                if (action.Input == null) throw new ArgumentException("Choose a supported input for every enabled row.");
                if (action.IntervalMs < 100 || action.IntervalMs > 86400000)
                    throw new ArgumentException("Intervals must be 0.1 to 86400 seconds.");
                if (action.OffsetMs < 0 || action.OffsetMs > 86400000)
                    throw new ArgumentException("Initial delays must be 0 to 86400 seconds.");
                if (action.HoldMs < 10 || action.HoldMs > 5000 || action.HoldMs > action.IntervalMs)
                    throw new ArgumentException("Hold must be 10 to 5000 ms and no longer than the interval.");
                if (action.Burst < 1 || action.Burst > 20) throw new ArgumentException("Burst must be 1 to 20 presses.");
                if (action.KeepCooldownOnResume && action.Burst != 1)
                    throw new ArgumentException("A row that keeps its cooldown after a pause must use a burst of 1.");
                if (action.X.HasValue != action.Y.HasValue) throw new ArgumentException("Enter both X and Y, or leave both blank.");
                if (action.X.HasValue && (action.X.Value < 0 || action.Y.Value < 0))
                    throw new ArgumentException("Mouse coordinates cannot be negative.");
                if (action.X.HasValue && !NativeInput.IsMouseInput(action.Input))
                    throw new ArgumentException("Only mouse-click rows can have coordinates.");
                validated.Add(new ScheduledAction { Action = action });
            }
            if (validated.Count == 0) throw new ArgumentException("Enable at least one action.");
            if (validated.Count > 64) throw new ArgumentException("Use at most 64 enabled actions.");
            backend.BindTarget(targetWindow);
            backend.ReleaseAll();
            target = targetWindow;
            scheduled.Clear();
            scheduled.AddRange(validated);
            held.Clear();
            TotalActions = 0;
            activeMs = 0;
            startedAt = clock();
            readyAt = startedAt + countdownMs;
            previousTick = startedAt;
            limitMs = sessionLimitMs;
            actionLimit = maxActions;
            nextPressAt = readyAt;
            nextRow = 0;
            wasForeground = false;
            previousWasActive = false;
            foreach (ScheduledAction row in scheduled) row.NextAt = readyAt + row.Action.OffsetMs;
            IsArmed = true;
            IsSending = false;
            Status = countdownMs > 0 ? "Starting in " + ((countdownMs + 999) / 1000) + "s" : "Waiting for target focus";
        }

        public void StartOrdered(IntPtr targetWindow, QuickClickSettings source)
        {
            if (source == null) throw new ArgumentNullException("source");
            QuickClickSettings settings = source.Clone();
            settings.Validate();
            if (!settings.UseMultiplePoints) throw new ArgumentException("Choose Multiple points before starting an ordered route.");
            if (settings.Points.Count == 0) throw new ArgumentException("Add at least one click point before starting.");
            settings.UseMultiplePoints = false;
            settings.X = settings.Points[0].X;
            settings.Y = settings.Points[0].Y;
            Start(targetWindow, new[] { settings.ToAction() }, settings.CountdownSeconds * 1000, settings.SessionSeconds * 1000, settings.MaxClicks);
            orderedPoints = settings.Points;
            orderedPoint = 0;
        }

        public void Stop() { Halt("Stopped"); }

        private void Halt(string status)
        {
            IsArmed = false;
            IsSending = false;
            previousWasActive = false;
            wasForeground = false;
            held.Clear();
            Status = status;
            try { backend.ReleaseAll(); }
            catch (Exception ex) { Status = status + " - release failed: " + ex.Message; }
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
            bool foreground = backend.IsTargetForeground(target);
            if (!foreground)
            {
                if (wasForeground || held.Count > 0) backend.ReleaseAll();
                held.Clear();
                wasForeground = false;
                previousWasActive = false;
                IsSending = false;
                if (ActionLimitReached) { Halt("Action limit reached"); return; }
                Status = "Paused - focus the selected window";
                return;
            }
            if (previousWasActive) activeMs += delta;
            if (ActionLimitReached)
            {
                // The last accepted press keeps its normal hold. No new down is
                // allowed while it drains, even if a burst has presses remaining.
                previousWasActive = true;
                ReleaseDueInputs(now);
                if (held.Count == 0) Halt("Action limit reached");
                else { IsSending = true; Status = "Finishing final input"; }
                return;
            }
            // Countdown and time spent with another window focused do not consume
            // the user's active session limit.
            if (limitMs > 0 && activeMs >= limitMs) { Halt("Session limit reached"); return; }
            previousWasActive = true;
            IsSending = true;
            Status = orderedPoints == null ? "Sending input to the selected window" : "Cycling " + orderedPoints.Count + " click points in order";
            if (!wasForeground)
            {
                // Normal rows restart their offsets. Opted-in single presses keep
                // a deadline from their last accepted down, without catch-up debt.
                // A new Start creates fresh rows and deliberately resets deadlines.
                foreach (ScheduledAction row in scheduled)
                {
                    if (orderedPoints != null)
                    {
                        // Keep the next point and any unfinished burst; resuming
                        // stages one press now rather than replaying missed visits.
                        row.CycleAt = now;
                        row.NextAt = now;
                        continue;
                    }
                    row.Remaining = 0;
                    row.NextAt = row.Action.KeepCooldownOnResume && row.HasPressed
                        ? Math.Max(now, row.CooldownUntil) : now + row.Action.OffsetMs;
                }
                nextPressAt = now;
                wasForeground = true;
            }
            ReleaseDueInputs(now);
            if (now < nextPressAt) return;
            for (int offset = 0; offset < scheduled.Count; offset++)
            {
                int index = (nextRow + offset) % scheduled.Count;
                ScheduledAction row = scheduled[index];
                if (row.NextAt > now || IsHeld(row.Action.Input)) continue;
                // Recheck immediately before every input, including bursts.
                if (!backend.IsTargetForeground(target))
                {
                    backend.ReleaseAll();
                    held.Clear();
                    wasForeground = false;
                    previousWasActive = false;
                    IsSending = false;
                    Status = "Paused - focus the selected window";
                    return;
                }
                IInputReadiness readiness = backend as IInputReadiness;
                string waitReason;
                if (readiness != null && !readiness.CanSend(row.Action, out waitReason))
                {
                    backend.ReleaseAll(); held.Clear();
                    wasForeground = false; previousWasActive = false; IsSending = false;
                    Status = waitReason; return;
                }
                if (!backend.Press(target, row.Action))
                {
                    row.NextAt = now + 100;
                    IsSending = false;
                    Status = "Waiting - keep the pointer inside the target";
                    nextRow = (index + 1) % scheduled.Count;
                    return;
                }
                held.Add(new HeldInput { Input = row.Action.Input, ReleaseAt = now + row.Action.HoldMs });
                if (row.Action.KeepCooldownOnResume)
                {
                    row.HasPressed = true;
                    row.CooldownUntil = now + row.Action.IntervalMs;
                }
                TotalActions++;
                if (ActionLimitReached) Status = "Finishing final input";
                if (row.Remaining <= 0)
                {
                    row.CycleAt = now;
                    row.Remaining = row.Action.Burst;
                }
                row.Remaining--;
                int gap = Math.Max(60, row.Action.HoldMs + 30);
                row.NextAt = row.Remaining > 0 ? now + gap : Math.Max(row.CycleAt + row.Action.IntervalMs, now + gap);
                if (orderedPoints != null && row.Remaining == 0)
                {
                    orderedPoint = (orderedPoint + 1) % orderedPoints.Count;
                    row.Action.X = orderedPoints[orderedPoint].X;
                    row.Action.Y = orderedPoints[orderedPoint].Y;
                }
                nextPressAt = now + 20;
                nextRow = (index + 1) % scheduled.Count;
                return;
            }
        }

        private bool ActionLimitReached { get { return actionLimit > 0 && TotalActions >= actionLimit; } }

        private void ReleaseDueInputs(long now)
        {
            for (int i = held.Count - 1; i >= 0; i--)
            {
                if (held[i].ReleaseAt <= now)
                {
                    backend.Release(held[i].Input);
                    held.RemoveAt(i);
                }
            }
        }

        private bool IsHeld(string input)
        {
            bool mouse = NativeInput.IsMouseInput(input);
            foreach (HeldInput item in held)
                if (String.Equals(item.Input, input, StringComparison.OrdinalIgnoreCase) || (mouse && NativeInput.IsMouseInput(item.Input)))
                    return true;
            return false;
        }
    }
}
