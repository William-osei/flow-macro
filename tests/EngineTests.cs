using System;
using System.Collections.Generic;
using FarmMacro;

internal sealed class FakeInput : IInputBackend, IInputReadiness
{
    public bool Focus = true;
    public bool Accept = true;
    public bool Ready = true;
    public bool CanSend(MacroAction action, out string reason) { reason = "Waiting - release modifiers"; return Ready; }
    public bool FailPress;
    public bool FailRelease;
    public List<string> Events = new List<string>();
    public List<MacroAction> Presses = new List<MacroAction>();
    public HashSet<string> Held = new HashSet<string>();
    public void BindTarget(IntPtr target) { Events.Add("bind"); }
    public bool IsTargetForeground(IntPtr target) { return Focus; }
    public bool Press(IntPtr target, MacroAction action)
    {
        if (FailPress) throw new InvalidOperationException("press failure");
        if (!Accept) return false;
        Events.Add("down:" + action.Input);
        Presses.Add(action.Clone());
        Held.Add(action.Input);
        return true;
    }
    public void Release(string input)
    {
        Events.Add("up:" + input);
        if (FailRelease) throw new InvalidOperationException("release failure");
        Held.Remove(input);
    }
    public void ReleaseAll()
    {
        foreach (string input in new List<string>(Held)) Release(input);
    }
}

internal static class EngineTests
{
    private static long time;
    private static int passed;
    private static MacroAction Action(string input, int interval, int hold, int burst)
    {
        return new MacroAction { Enabled = true, Input = input, IntervalMs = interval, HoldMs = hold, Burst = burst };
    }
    private static MacroEngine Engine(FakeInput backend)
    {
        time = 0;
        return new MacroEngine(backend, delegate { return time; });
    }
    private static QuickClickSettings Route(int burst = 1, int hold = 40, int interval = 100)
    {
        return new QuickClickSettings {
            UseMultiplePoints = true, Burst = burst, HoldMs = hold, IntervalMs = interval, CountdownSeconds = 1,
            Points = new List<ClickPoint> { new ClickPoint { X = 10, Y = 20 }, new ClickPoint { X = 30, Y = 40 } }
        };
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAILED: " + label);
    }
    private static void Run(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }
    public static int Main()
    {
        try
        {
            Run("Countdown sends nothing until ready", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("C", 1000, 60, 1) }, 3000, 0);
                engine.Tick(); time = 2999; engine.Tick(); Check(engine.TotalActions == 0, "no early key");
                time = 3000; engine.Tick(); Check(engine.TotalActions == 1 && fake.Held.Contains("C"), "ready key");
            });
            Run("Focus loss releases and resume has no catch-up", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("C", 100, 50, 5) }, 0, 0);
                engine.Tick(); fake.Focus = false; time = 20; engine.Tick();
                Check(fake.Held.Count == 0 && engine.IsArmed && !engine.IsSending, "released on pause");
                time = 20000; engine.Tick(); Check(engine.TotalActions == 1, "paused no input");
                fake.Focus = true; engine.Tick(); Check(engine.TotalActions == 2, "one on resume");
                engine.Tick(); Check(engine.TotalActions == 2, "no same-time catch-up");
                Check(engine.ActiveDuration.TotalMilliseconds == 0, "paused time excluded");
            });
            Run("Stop immediately releases a held input", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("V", 3000, 1000, 1) }, 0, 0);
                engine.Tick(); engine.Stop();
                Check(!engine.IsArmed && !engine.IsSending && fake.Held.Count == 0, "stop releases");
                time = 9999; engine.Tick(); Check(engine.TotalActions == 1, "no later input");
            });
            Run("Independent key intervals remain independent", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("C", 1000, 10, 1), Action("V", 2000, 10, 1) }, 0, 0);
                for (time = 0; time <= 2040; time += 20) engine.Tick();
                Check(fake.Events.FindAll(delegate(string e) { return e == "down:C"; }).Count == 3, "C cadence");
                Check(fake.Events.FindAll(delegate(string e) { return e == "down:V"; }).Count == 2, "V cadence");
            });
            Run("Unexpected modifiers postpone timer input without catch-up", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                fake.Ready = false;
                engine.Start(new IntPtr(1), new[] { Action("C", 1000, 150, 1) }, 0, 0);
                engine.Tick(); time = 30000; engine.Tick();
                Check(engine.TotalActions == 0 && !engine.IsSending && engine.IsArmed, "wait before sending");
                fake.Ready = true; engine.Tick();
                Check(engine.TotalActions == 1 && fake.Held.Contains("C"), "single input after release");
                engine.Stop(); Check(fake.Held.Count == 0, "stops safely");
            });
            Run("Session limit counts active time and excludes focus pauses", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("C", 1000, 60, 1) }, 1000, 500);
                time = 1000; engine.Tick(); time = 1100; engine.Tick();
                fake.Focus = false; time = 1200; engine.Tick();
                time = 20000; engine.Tick();
                Check(engine.IsArmed && engine.ActiveDuration.TotalMilliseconds == 100, "focus pause excluded from limit");
                fake.Focus = true; engine.Tick(); time = 20400; engine.Tick();
                Check(!engine.IsArmed && fake.Held.Count == 0 && engine.Status == "Session limit reached", "session stop");
                Check(engine.ActiveDuration.TotalMilliseconds == 500, "active duration at stop");
            });
            Run("Input error stops and releases other held keys", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("C", 1000, 500, 1), Action("V", 1000, 500, 1) }, 0, 0);
                engine.Tick(); fake.FailPress = true; time = 20; engine.Tick();
                Check(!engine.IsArmed && fake.Held.Count == 0 && engine.Status.Contains("press failure"), "failure contained");
            });
            Run("Release failure is contained and reports error", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("C", 1000, 10, 1) }, 0, 0);
                engine.Tick(); fake.FailRelease = true; time = 20; engine.Tick();
                Check(!engine.IsArmed && engine.Status.Contains("release failure"), "release error reported");
            });
            Run("Multiple points on the same mouse button serialize", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("Left click", 100, 50, 1), Action("Left click", 100, 50, 1) }, 0, 0);
                engine.Tick(); time = 20; engine.Tick(); Check(engine.TotalActions == 1, "held mouse blocks next point");
                time = 50; engine.Tick(); Check(engine.TotalActions == 2, "next point after release");
            });
            Run("Unsafe pointer rejection sends no input", delegate
            {
                FakeInput fake = new FakeInput(); fake.Accept = false; MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("Left click", 100, 50, 1) }, 0, 0);
                for (time = 0; time <= 1000; time += 20) engine.Tick();
                Check(engine.TotalActions == 0 && fake.Held.Count == 0 && engine.IsArmed, "pointer rejected");
            });
            Run("Start snapshots action rows", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake); MacroAction row = Action("C", 1000, 60, 1);
                engine.Start(new IntPtr(1), new[] { row }, 0, 0); row.Input = "V";
                engine.Tick(); Check(fake.Held.Contains("C") && !fake.Held.Contains("V"), "snapshot immutable");
            });
            Run("Invalid actions fail before binding", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake); bool rejected = false;
                try { engine.Start(new IntPtr(1), new[] { Action("F9", 1000, 60, 1) }, 0, 0); }
                catch (ArgumentException) { rejected = true; }
                Check(rejected && fake.Events.Count == 0, "hotkey input rejected");
            });
            Run("Long tick gaps never queue missed cycles", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("C", 100, 10, 1) }, 0, 0);
                engine.Tick(); time = 50000; engine.Tick(); Check(engine.TotalActions == 2, "single new action");
                engine.Tick(); Check(engine.TotalActions == 2, "bounded gap");
            });
            Run("Initial offsets pace attacks without changing independent intervals", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                MacroAction c = Action("C", 1000, 60, 1);
                MacroAction v = Action("V", 2000, 60, 1); v.OffsetMs = 800;
                engine.Start(new IntPtr(1), new[] { c, v }, 0, 0);
                engine.Tick(); Check(engine.TotalActions == 1 && fake.Events.Contains("down:C"), "C starts at zero");
                time = 60; engine.Tick(); time = 799; engine.Tick();
                Check(!fake.Events.Contains("down:V"), "V respects initial delay");
                time = 800; engine.Tick(); Check(fake.Held.Contains("V"), "V at 800ms");
                for (time = 820; time <= 2820; time += 20) engine.Tick();
                Check(fake.Events.FindAll(delegate(string e) { return e == "down:C"; }).Count == 3, "C independent cadence");
                Check(fake.Events.FindAll(delegate(string e) { return e == "down:V"; }).Count == 2, "V independent cadence");
            });
            Run("Focus resume reapplies attack offsets without catch-up", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                MacroAction c = Action("C", 1000, 60, 1);
                MacroAction v = Action("V", 2000, 60, 1); v.OffsetMs = 800;
                engine.Start(new IntPtr(1), new[] { c, v }, 1000, 0);
                time = 1000; engine.Tick(); fake.Focus = false; time = 1020; engine.Tick();
                fake.Focus = true; time = 5000; engine.Tick();
                Check(engine.TotalActions == 2 && !fake.Events.Contains("down:V"), "C starts fresh on resume");
                time = 5799; engine.Tick(); Check(!fake.Events.Contains("down:V"), "V delay reset after pause");
                time = 5800; engine.Tick(); Check(engine.TotalActions == 3 && fake.Held.Contains("V"), "V after resumed offset");
            });
            Run("Invalid initial offset fails before target binding", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                MacroAction action = Action("C", 1000, 60, 1); action.OffsetMs = -1;
                bool rejected = false;
                try { engine.Start(new IntPtr(1), new[] { action }, 0, 0); }
                catch (ArgumentException) { rejected = true; }
                Check(rejected && fake.Events.Count == 0, "negative offset rejected");
            });
            Run("Click total stops partway through a burst without overshoot", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("Left click", 1000, 40, 3) }, 0, 0, 5);
                for (time = 0; time <= 5000; time += 10) engine.Tick();
                Check(engine.TotalActions == 5 && fake.Events.FindAll(delegate(string e) { return e == "down:Left click"; }).Count == 5, "exact individual total");
                Check(!engine.IsArmed && fake.Held.Count == 0 && engine.Status == "Action limit reached", "completed after final release");
            });
            Run("Count completion preserves the final hold and all overlapping holds", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("C", 1000, 500, 1), Action("V", 1000, 40, 1) }, 0, 0, 2);
                engine.Tick(); time = 20; engine.Tick();
                time = 59; engine.Tick(); Check(engine.IsArmed && fake.Held.Count == 2 && engine.TotalActions == 2, "final hold not truncated");
                time = 60; engine.Tick(); Check(engine.IsArmed && fake.Held.Contains("C") && !fake.Held.Contains("V"), "earlier longer hold still drains");
                time = 499; engine.Tick(); Check(fake.Held.Contains("C") && engine.TotalActions == 2, "no extra press during drain");
                time = 500; engine.Tick(); Check(!engine.IsArmed && fake.Held.Count == 0, "complete once every hold releases");
            });
            Run("Focus pauses preserve the click total and final focus loss releases safely", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("Left click", 100, 80, 20) }, 0, 0, 3);
                engine.Tick(); fake.Focus = false; time = 20; engine.Tick();
                time = 1000; engine.Tick(); Check(engine.TotalActions == 1 && fake.Held.Count == 0, "pause releases without consuming count");
                fake.Focus = true; engine.Tick(); fake.Focus = false; time = 1001; engine.Tick();
                fake.Focus = true; time = 2000; engine.Tick(); Check(engine.TotalActions == 3 && fake.Held.Count == 1, "remaining exact total after pauses");
                fake.Focus = false; time = 2001; engine.Tick();
                Check(!engine.IsArmed && fake.Held.Count == 0 && engine.TotalActions == 3, "focus loss ends final hold safely");
                fake.Focus = true; time = 99999; engine.Tick(); Check(engine.TotalActions == 3, "count completion never resumes");
            });
            Run("Emergency stop releases the last counted hold immediately", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("Left click", 1000, 500, 20) }, 0, 0, 1);
                engine.Tick(); time = 1; engine.Stop(); time = 1000; engine.Tick();
                Check(!engine.IsArmed && fake.Held.Count == 0 && engine.TotalActions == 1 && engine.Status == "Stopped", "explicit stop overrides drain");
            });
            Run("Rejected presses do not consume the click limit", delegate
            {
                FakeInput fake = new FakeInput(); fake.Accept = false; MacroEngine engine = Engine(fake);
                engine.Start(new IntPtr(1), new[] { Action("Left click", 100, 40, 20) }, 0, 0, 1);
                for (time = 0; time < 1000; time += 20) engine.Tick();
                Check(engine.TotalActions == 0 && engine.IsArmed, "unsafe points do not count");
                fake.Accept = true; time = 1000; engine.Tick(); time = 1040; engine.Tick();
                Check(engine.TotalActions == 1 && !engine.IsArmed && fake.Held.Count == 0, "one successful full click");
            });
            Run("Action count bounds reject before binding and zero remains unlimited", delegate
            {
                foreach (long limit in new[] { -1L, MacroEngine.MaximumActions + 1 })
                {
                    FakeInput invalid = new FakeInput(); MacroEngine engine = Engine(invalid); bool rejected = false;
                    try { engine.Start(new IntPtr(1), new[] { Action("Left click", 100, 10, 1) }, 0, 0, limit); }
                    catch (ArgumentException) { rejected = true; }
                    Check(rejected && invalid.Events.Count == 0, "invalid count rejected before binding");
                }
                FakeInput fake = new FakeInput(); MacroEngine unlimited = Engine(fake);
                unlimited.Start(new IntPtr(1), new[] { Action("Left click", 100, 10, 1) }, 0, 0, 0);
                for (time = 0; time <= 30000; time += 20) unlimited.Tick();
                Check(unlimited.IsArmed && unlimited.TotalActions > 200, "zero count limit runs beyond twenty seconds");
                unlimited.Stop();
            });
            Run("Quick click defaults and detached action normalize only supported mouse buttons", delegate
            {
                QuickClickSettings defaults = new QuickClickSettings(); defaults.Validate();
                Check(defaults.IntervalMs == 1000 && defaults.HoldMs == 40 && defaults.Burst == 1 && defaults.MaxClicks == 0 && defaults.SessionSeconds == 0, "friendly indefinite defaults");
                QuickClickSettings settings = defaults.Clone(); settings.Input = " right CLICK "; settings.X = 123; settings.Y = 456; settings.MaxClicks = 7;
                MacroAction action = settings.ToAction(); settings.X = 999; settings.Input = "Left click";
                Check(action.Enabled && action.Input == "Right click" && action.X == 123 && action.Y == 456 && defaults.X == null, "normalized detached click row");
            });
            Run("Quick click validation rejects invalid totals, timing and coordinates", delegate
            {
                QuickClickSettings[] invalid = {
                    new QuickClickSettings { Input = "C" }, new QuickClickSettings { MaxClicks = -1 },
                    new QuickClickSettings { MaxClicks = QuickClickSettings.MaximumClicks + 1 },
                    new QuickClickSettings { IntervalMs = 99 }, new QuickClickSettings { HoldMs = 1001 },
                    new QuickClickSettings { Burst = 0 }, new QuickClickSettings { Burst = 21 },
                    new QuickClickSettings { X = 1 }, new QuickClickSettings { X = -1, Y = 2 },
                    new QuickClickSettings { CountdownSeconds = 0 }, new QuickClickSettings { SessionSeconds = 86401 }
                };
                foreach (QuickClickSettings settings in invalid)
                {
                    bool rejected = false; try { settings.Validate(); } catch (ArgumentException) { rejected = true; }
                    Check(rejected, "invalid quick click settings");
                }
                new QuickClickSettings { Input = " middle click ", IntervalMs = 86400000, HoldMs = 5000, Burst = 20, MaxClicks = QuickClickSettings.MaximumClicks, SessionSeconds = 86400, CountdownSeconds = 30 }.Validate();
            });
            Run("Preserved Z cooldown survives focus resume while C and V keep normal offsets", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                MacroAction c = Action("C", 12300, 500, 1);
                MacroAction v = Action("V", 12300, 500, 1); v.OffsetMs = 1800;
                MacroAction z = Action("Z", 24000, 60, 1); z.OffsetMs = 2600; z.KeepCooldownOnResume = true;
                engine.Start(new IntPtr(1), new[] { c, v, z }, 0, 0);
                engine.Tick(); time = 500; engine.Tick(); time = 1800; engine.Tick(); time = 2300; engine.Tick();
                time = 2599; engine.Tick(); Check(!fake.Events.Contains("down:Z"), "first Z respects its offset");
                time = 2600; engine.Tick(); time = 2660; engine.Tick();
                fake.Focus = false; time = 5000; engine.Tick(); fake.Focus = true; time = 10000; engine.Tick();
                Check(fake.Held.Contains("C"), "C resumes at the usual zero offset");
                time = 10500; engine.Tick(); time = 11799; engine.Tick();
                Check(fake.Events.FindAll(delegate(string e) { return e == "down:V"; }).Count == 1, "V keeps resumed first delay");
                time = 11800; engine.Tick(); Check(fake.Held.Contains("V"), "V resumes at 1800 ms");
                time = 12300; engine.Tick(); time = 12600; engine.Tick();
                Check(fake.Events.FindAll(delegate(string e) { return e == "down:Z"; }).Count == 1, "resume cannot recast Z early");
                time = 22300; engine.Tick(); time = 22800; engine.Tick(); time = 24100; engine.Tick(); time = 24600; engine.Tick();
                time = 26599; engine.Tick(); Check(!fake.Held.Contains("Z"), "Z waits until last down plus 24 seconds");
                time = 26600; engine.Tick(); Check(fake.Held.Contains("Z") && fake.Events.FindAll(delegate(string e) { return e == "down:Z"; }).Count == 2, "Z is ready at its preserved deadline");
            });
            Run("An expired preserved cooldown resumes once without missed-cycle catch-up", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                MacroAction z = Action("Z", 24000, 60, 1); z.OffsetMs = 2600; z.KeepCooldownOnResume = true;
                engine.Start(new IntPtr(1), new[] { z }, 0, 0); engine.Tick(); time = 2600; engine.Tick(); time = 2660; engine.Tick();
                fake.Focus = false; time = 5000; engine.Tick(); fake.Focus = true; time = 100000; engine.Tick();
                Check(engine.TotalActions == 2 && fake.Held.Contains("Z"), "expired deadline permits one immediate down without initial delay");
                engine.Tick(); engine.Tick(); Check(engine.TotalActions == 2, "same-time ticks never replay missed cycles");
                time = 100060; engine.Tick(); time = 123999; engine.Tick(); Check(engine.TotalActions == 2, "next cooldown starts from actual resumed down");
                time = 124000; engine.Tick(); Check(engine.TotalActions == 3, "normal interval resumes");
            });
            Run("Readiness pauses preserve an independent cooldown deadline", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                MacroAction z = Action("Z", 24000, 60, 1); z.OffsetMs = 2600; z.KeepCooldownOnResume = true;
                engine.Start(new IntPtr(1), new[] { Action("C", 1000, 40, 1), z }, 0, 0);
                engine.Tick(); time = 40; engine.Tick(); time = 2600; engine.Tick(); time = 2660; engine.Tick();
                fake.Ready = false; time = 5000; engine.Tick(); Check(!engine.IsSending, "modifier readiness pauses the scheduler");
                fake.Ready = true; time = 10000; engine.Tick(); time = 12600; engine.Tick();
                time = 26000; engine.Tick(); time = 26040; engine.Tick(); time = 26599; engine.Tick();
                Check(fake.Events.FindAll(delegate(string e) { return e == "down:Z"; }).Count == 1, "readiness resume cannot reset Z to an early offset");
                time = 26600; engine.Tick(); Check(fake.Held.Contains("Z"), "preserved deadline survives readiness pause");
            });
            Run("Rejected presses never consume a preserved cooldown", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                MacroAction z = Action("Z", 24000, 60, 1); z.KeepCooldownOnResume = true;
                fake.Accept = false; engine.Start(new IntPtr(1), new[] { z }, 0, 0); engine.Tick(); time = 100; engine.Tick();
                Check(engine.TotalActions == 0, "initial rejected attempts do not count");
                fake.Accept = true; time = 500; engine.Tick(); fake.Focus = false; time = 1000; engine.Tick();
                fake.Focus = true; time = 2000; engine.Tick(); time = 24499; engine.Tick(); Check(engine.TotalActions == 1, "deadline follows first accepted down at 500");
                fake.Accept = false; time = 24500; engine.Tick(); Check(engine.TotalActions == 1, "expired rejected retry consumes no cooldown");
                fake.Focus = false; time = 24600; engine.Tick(); fake.Focus = true; fake.Accept = true; time = 25000; engine.Tick();
                Check(engine.TotalActions == 2 && fake.Held.Contains("Z"), "a resumed successful retry is immediate after rejection");
                time = 25060; engine.Tick(); time = 48999; engine.Tick(); Check(engine.TotalActions == 2, "successful retry starts its own full cooldown");
                time = 49000; engine.Tick(); Check(engine.TotalActions == 3, "retry deadline is exact");
            });
            Run("Losing focus during a protected hold releases but retains its cooldown", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                MacroAction z = Action("Z", 24000, 500, 1); z.KeepCooldownOnResume = true;
                engine.Start(new IntPtr(1), new[] { z }, 0, 0); engine.Tick();
                fake.Focus = false; time = 10; engine.Tick(); Check(fake.Held.Count == 0 && engine.IsArmed, "focus loss releases immediately");
                fake.Focus = true; time = 1000; engine.Tick(); time = 23999; engine.Tick();
                Check(engine.TotalActions == 1, "a down accepted at clock zero still preserves its deadline");
                time = 24000; engine.Tick(); Check(engine.TotalActions == 2, "interrupted hold does not bypass cooldown");
            });
            Run("Explicit stop and new Start reset protected session deadlines", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                MacroAction z = Action("Z", 24000, 60, 1); z.KeepCooldownOnResume = true;
                engine.Start(new IntPtr(1), new[] { z }, 0, 0); engine.Tick(); time = 10; engine.Stop();
                engine.Start(new IntPtr(1), new[] { z }, 0, 0); engine.Tick();
                Check(engine.TotalActions == 1 && fake.Events.FindAll(delegate(string e) { return e == "down:Z"; }).Count == 2, "new session deliberately starts a fresh cooldown history");
            });
            Run("Protected cooldown rows reject bursts before target binding", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                MacroAction z = Action("Z", 24000, 60, 2); z.KeepCooldownOnResume = true; bool rejected = false;
                try { engine.Start(new IntPtr(1), new[] { z }, 0, 0); } catch (ArgumentException) { rejected = true; }
                Check(rejected && fake.Events.Count == 0, "protected bursts must not bind or send input");
            });
            Run("Ordered mouse points wait for countdown then visit every point and wrap", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake); QuickClickSettings settings = Route(1, 10);
                settings.Points.Add(new ClickPoint { X = 50, Y = 60 });
                engine.StartOrdered(new IntPtr(1), settings); engine.Tick(); time = 999; engine.Tick();
                Check(engine.TotalActions == 0, "ordered countdown sends nothing");
                for (time = 1000; time <= 1300; time += 100) engine.Tick();
                Check(fake.Presses.Count == 4 && fake.Presses[0].X == 10 && fake.Presses[1].X == 30 && fake.Presses[2].X == 50 && fake.Presses[3].X == 10, "strict 1-2-3-1 order");
                Check(fake.Presses[0].Y == 20 && fake.Presses[1].Y == 40 && fake.Presses[2].Y == 60, "target-relative point pairs retained");
            });
            Run("Ordered bursts finish a point and exact totals can stop partway through the next", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake); QuickClickSettings settings = Route(3, 20);
                settings.MaxClicks = 5; engine.StartOrdered(new IntPtr(1), settings);
                foreach (int stamp in new[] { 1000, 1020, 1060, 1080, 1120, 1140, 1180, 1200, 1240, 1259 }) { time = stamp; engine.Tick(); }
                Check(engine.TotalActions == 5 && engine.IsArmed && fake.Held.Count == 1, "final accepted down keeps its full hold");
                Check(fake.Presses[0].X == 10 && fake.Presses[1].X == 10 && fake.Presses[2].X == 10 && fake.Presses[3].X == 30 && fake.Presses[4].X == 30, "burst is completed before changing points");
                time = 1260; engine.Tick(); time = 99999; engine.Tick();
                Check(!engine.IsArmed && fake.Held.Count == 0 && engine.TotalActions == 5, "no extra click after the partial final burst");
            });
            Run("Ordered routes release a long hold before moving to the next point", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.StartOrdered(new IntPtr(1), Route(1, 100)); time = 1000; engine.Tick();
                time = 1099; engine.Tick(); Check(fake.Presses.Count == 1 && fake.Held.Count == 1, "pointer stays at the held point");
                time = 1100; engine.Tick(); time = 1129; engine.Tick();
                Check(fake.Held.Count == 0 && fake.Presses.Count == 1, "release and minimum gap happen before moving");
                time = 1130; engine.Tick(); Check(fake.Presses.Count == 2 && fake.Presses[1].X == 30, "next point receives the next down");
            });
            Run("Focus pauses preserve an ordered point's unfinished burst without catch-up", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.StartOrdered(new IntPtr(1), Route(2, 40, 200)); time = 1000; engine.Tick();
                fake.Focus = false; time = 1020; engine.Tick(); time = 10000; engine.Tick();
                Check(fake.Held.Count == 0 && engine.TotalActions == 1, "focus loss releases and sends nothing while away");
                fake.Focus = true; engine.Tick(); engine.Tick();
                Check(engine.TotalActions == 2 && fake.Presses[1].X == 10, "resume completes only the remaining first-point click");
                time = 10040; engine.Tick(); time = 10200; engine.Tick(); time = 10270; engine.Tick();
                Check(fake.Presses.Count == 4 && fake.Presses[2].X == 30 && fake.Presses[3].X == 30, "second point gets its own complete burst");
            });
            Run("Focus after a completed visit resumes at the next point and respects the total", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake); QuickClickSettings settings = Route(); settings.MaxClicks = 2;
                engine.StartOrdered(new IntPtr(1), settings); time = 1000; engine.Tick();
                fake.Focus = false; time = 1010; engine.Tick(); fake.Focus = true; time = 10000; engine.Tick();
                Check(fake.Presses.Count == 2 && fake.Presses[1].X == 30 && engine.IsArmed, "resume does not restart point one");
                time = 10040; engine.Tick(); Check(!engine.IsArmed && fake.Held.Count == 0 && engine.TotalActions == 2, "counted completion releases the second point");
            });
            Run("Rejected ordered clicks retry the same point without consuming the total", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake); fake.Accept = false;
                engine.StartOrdered(new IntPtr(1), Route()); time = 1000; engine.Tick(); time = 1099; engine.Tick();
                Check(engine.TotalActions == 0 && fake.Presses.Count == 0, "unsafe attempts do not count");
                fake.Accept = true; time = 1100; engine.Tick(); time = 1200; engine.Tick();
                Check(fake.Presses.Count == 2 && fake.Presses[0].X == 10 && fake.Presses[1].X == 30, "successful retry precedes the next point");
            });
            Run("Readiness pauses retain ordered points without advancing or replaying visits", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake); fake.Ready = false;
                engine.StartOrdered(new IntPtr(1), Route()); time = 1000; engine.Tick(); time = 3000; engine.Tick();
                Check(engine.TotalActions == 0, "modifiers postpone the first point");
                fake.Ready = true; engine.Tick(); time = 3040; engine.Tick(); fake.Ready = false; time = 4000; engine.Tick();
                fake.Ready = true; time = 5000; engine.Tick(); engine.Tick();
                Check(fake.Presses.Count == 2 && fake.Presses[0].X == 10 && fake.Presses[1].X == 30, "resume sends only the next due point");
            });
            Run("Ordered playback snapshots the point objects and mouse settings", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake); QuickClickSettings source = Route(); source.Input = "Right click";
                engine.StartOrdered(new IntPtr(1), source);
                source.Points[0].X = 999; source.Points.Clear(); source.Input = "Middle click"; source.Burst = 20;
                time = 1000; engine.Tick(); time = 1100; engine.Tick();
                Check(fake.Presses.Count == 2 && fake.Presses[0].X == 10 && fake.Presses[1].X == 30 && fake.Presses[1].Input == "Right click", "active route is detached from edits");
                engine.Stop(); Check(fake.Held.Count == 0 && !engine.IsArmed, "emergency stop releases the route's button");
            });
            Run("Ordered active-time limits exclude focus pauses and stop held input", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake); QuickClickSettings settings = Route(); settings.SessionSeconds = 1;
                engine.StartOrdered(new IntPtr(1), settings); time = 1000; engine.Tick(); time = 1100; engine.Tick();
                fake.Focus = false; time = 1200; engine.Tick(); time = 10000; engine.Tick(); fake.Focus = true; engine.Tick();
                Check(engine.IsArmed && engine.TotalActions == 3 && engine.ActiveDuration.TotalMilliseconds == 100, "paused time is excluded");
                time = 10900; engine.Tick(); Check(!engine.IsArmed && fake.Held.Count == 0 && engine.ActiveDuration.TotalMilliseconds == 1000, "active session limit stops safely");
            });
            Run("Ordered routes run indefinitely beyond twenty seconds and normal Start resets route mode", delegate
            {
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake);
                engine.StartOrdered(new IntPtr(1), Route(1, 40, 1000));
                for (time = 1000; time <= 31000; time += 1000) engine.Tick();
                Check(engine.IsArmed && engine.TotalActions == 31 && fake.Presses[30].X == 10, "unlimited ordered loop remains active");
                engine.Stop(); Check(fake.Held.Count == 0, "stop releases before another mode");
                engine.Start(new IntPtr(1), new[] { Action("Space", 1000, 60, 1) }, 0, 0); engine.Tick();
                Check(engine.TotalActions == 1 && fake.Presses[fake.Presses.Count - 1].Input == "Space" && !fake.Presses[fake.Presses.Count - 1].X.HasValue, "normal routine start clears ordered state");
            });
            Run("Multiple-point settings clone deeply and invalid routes fail before binding", delegate
            {
                QuickClickSettings source = Route(); QuickClickSettings copy = source.Clone(); copy.Points[0].X = 70; copy.Points.Add(new ClickPoint());
                Check(source.Points.Count == 2 && source.Points[0].X == 10, "settings clone owns the list and each point");
                source.Points = null; source.Validate(); Check(source.Points.Count == 0 && source.Clone().Points.Count == 0, "legacy null lists normalize to editable empty drafts");
                FakeInput fake = new FakeInput(); MacroEngine engine = Engine(fake); bool rejected = false;
                try { engine.StartOrdered(new IntPtr(1), source); } catch (ArgumentException) { rejected = true; }
                Check(rejected && fake.Events.Count == 0, "empty routes never bind");
                source.Points.Add(new ClickPoint { X = -1 }); rejected = false;
                try { engine.StartOrdered(new IntPtr(1), source); } catch (ArgumentException) { rejected = true; }
                Check(rejected && fake.Events.Count == 0, "negative point never binds");
                source.Points.Clear(); source.Points.Add(null); rejected = false;
                try { source.Validate(); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "null point rejected");
                source.Points.Clear(); for (int index = 0; index <= QuickClickSettings.MaximumPoints; index++) source.Points.Add(new ClickPoint()); rejected = false;
                try { source.Validate(); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "point limit enforced");
                source.Points.RemoveAt(source.Points.Count - 1); engine.StartOrdered(new IntPtr(1), source);
                time = 1000; engine.Tick(); Check(engine.IsArmed && engine.TotalActions == 1, "exactly one hundred points is supported"); engine.Stop();
                source.Points.Clear(); rejected = false; try { source.ToAction(); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "multi-point mode cannot silently become a single-click action");
            });
            Console.WriteLine(passed + " tests passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
}
