import dev.farmmacro.app.TapAction;
import dev.farmmacro.app.TapScheduler;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

public final class TapSchedulerTest {
    static int passed;
    static final class Harness implements TapScheduler.Clock, TapScheduler.Backend {
        long now;
        boolean focused = true, accepted = true, automatic = true;
        final List<String> presses = new ArrayList<String>();
        final List<TapAction> gestures = new ArrayList<TapAction>();
        TapScheduler.Completion pending;
        final TapScheduler scheduler = new TapScheduler(this, this);
        public long now() { return now; }
        public boolean isTargetReady() { return focused; }
        public boolean dispatch(TapAction action, TapScheduler.Completion completion) {
            if (!accepted) return false;
            presses.add(action.Label + "@" + now); gestures.add(action.copy()); pending = completion;
            if (automatic) completion.completed();
            return true;
        }
        void tick(long time) { now = time; scheduler.tick(); }
        void start(TapAction... actions) { scheduler.start(Arrays.asList(actions), 0, 0); }
    }
    static TapAction action(String label, int interval) {
        TapAction result = new TapAction(); result.Label = label; result.X = .7; result.Y = .8; result.IntervalMs = interval; return result;
    }
    static TapAction swipe(String label, int interval) {
        TapAction result = action(label, interval); result.Kind = "Swipe"; result.EndX = .2; result.EndY = .3; result.HoldMs = 400; return result;
    }
    static void check(boolean condition, String message) { if (!condition) throw new AssertionError(message); }
    static void test(String name, Runnable body) { body.run(); passed++; System.out.println("PASS " + name); }
    static boolean invalid(TapAction action, boolean requireMapped) {
        try { action.validate(requireMapped); return false; } catch (IllegalArgumentException expected) { return true; }
    }

    public static void main(String[] arguments) {
        test("Countdown prevents all early gestures", new Runnable() { public void run() {
            Harness h = new Harness(); h.scheduler.start(Arrays.asList(action("C", 1000)), 1000, 0);
            h.tick(0); h.tick(999); check(h.presses.isEmpty(), "Countdown sent a tap");
            h.tick(1000); check(h.presses.size() == 1, "Countdown never started");
        } });
        test("Focus loss stops and never automatically resumes", new Runnable() { public void run() {
            Harness h = new Harness(); h.automatic = false; h.start(action("C", 1000)); h.tick(0);
            TapScheduler.Completion old = h.pending; h.focused = false; h.tick(1);
            check(!h.scheduler.isRunning(), "Focus loss did not stop"); old.completed(); h.focused = true; h.tick(20000);
            check(h.presses.size() == 1, "Focus gain restarted without explicit Start");
        } });
        test("Per-action first delays and intervals remain independent", new Runnable() { public void run() {
            Harness h = new Harness(); TapAction c = action("C", 1000), v = action("V", 2000); v.OffsetMs = 800;
            h.start(c, v); h.tick(0); h.tick(799); h.tick(800); h.tick(1000); h.tick(2800);
            check(h.presses.equals(Arrays.asList("C@0", "V@800", "C@1000", "V@2800")), h.presses.toString());
        } });
        test("A gesture callback must complete before the next tap", new Runnable() { public void run() {
            Harness h = new Harness(); h.automatic = false; h.start(action("C", 150), action("V", 150));
            h.tick(0); h.tick(500); check(h.presses.size() == 1, "Concurrent gesture dispatched");
            h.pending.completed(); h.tick(525); check(h.presses.size() == 2, "Completed gesture blocked next action");
        } });
        test("Bursts respect hold time and the next cycle interval", new Runnable() { public void run() {
            Harness h = new Harness(); TapAction c = action("C", 1000); c.Burst = 3;
            h.start(c); h.tick(0); h.tick(119); h.tick(120); h.tick(240); h.tick(999); h.tick(1000);
            check(h.presses.equals(Arrays.asList("C@0", "C@120", "C@240", "C@1000")), h.presses.toString());
        } });
        test("Stopping invalidates late gesture callbacks", new Runnable() { public void run() {
            Harness h = new Harness(); h.automatic = false; h.start(action("C", 1000)); h.tick(0);
            TapScheduler.Completion old = h.pending; h.scheduler.stop("Manual stop"); h.start(action("V", 1000)); h.tick(1);
            old.cancelled(); check(h.scheduler.isRunning(), "A stale callback stopped a new session");
            h.scheduler.stop("Manual stop"); h.pending.completed(); h.tick(5000);
            check(h.presses.size() == 2, "A stopped callback scheduled extra input");
        } });
        test("Active-time limit stops a session", new Runnable() { public void run() {
            Harness h = new Harness(); h.scheduler.start(Arrays.asList(action("C", 150)), 1000, 1000);
            h.tick(0); h.tick(1000); h.tick(1500); h.tick(2000);
            check(!h.scheduler.isRunning() && h.scheduler.getActiveMs() == 1000, "Timed limit did not exclude countdown");
        } });
        test("Unlimited mode runs beyond twenty active seconds", new Runnable() { public void run() {
            Harness h = new Harness(); h.start(action("C", 150));
            for (int now = 0; now <= 65000; now += 25) h.tick(now);
            check(h.scheduler.isRunning() && h.scheduler.getActiveMs() == 65000 && h.scheduler.getTapCount() > 100, "Unlimited session stopped early");
        } });
        test("Long delays never dispatch a catch-up storm", new Runnable() { public void run() {
            Harness h = new Harness(); h.start(action("C", 150)); h.tick(0); h.tick(60000);
            check(h.presses.size() == 2, "Missed cycles were replayed"); h.tick(60000);
            check(h.presses.size() == 2, "Second tick caught up an old cycle");
        } });
        test("Action edits cannot mutate a running snapshot", new Runnable() { public void run() {
            Harness h = new Harness(); TapAction c = action("C", 1000); h.start(c); c.Label = "V"; c.X = -1;
            h.tick(0); check(h.presses.get(0).equals("C@0"), "Source edits changed the session");
        } });
        test("Unmapped actions fail before input can start", new Runnable() { public void run() {
            Harness h = new Harness(); TapAction c = action("C", 1000); c.X = -1; c.Y = -1;
            boolean failed = false; try { h.start(c); } catch (IllegalArgumentException expected) { failed = true; }
            check(failed && !h.scheduler.isRunning() && h.presses.isEmpty(), "An unmapped point was accepted");
        } });
        test("Cancelled and rejected gestures stop safely", new Runnable() { public void run() {
            Harness h = new Harness(); h.automatic = false; h.start(action("C", 1000)); h.tick(0); h.pending.cancelled();
            check(!h.scheduler.isRunning(), "Cancellation did not stop"); h.accepted = false; h.start(action("V", 1000)); h.tick(1);
            check(!h.scheduler.isRunning() && h.presses.size() == 1, "Rejected gesture was counted or retried");
        } });
        test("Legacy taps retain their default kind and need only a start point", new Runnable() { public void run() {
            TapAction tap = action("Tap", 1000); tap.validate(true);
            check("Tap".equals(tap.Kind) && !tap.isSwipe() && tap.hasStartPoint() && !tap.hasEndPoint() && tap.isMapped(), "Tap compatibility changed");
            tap.EndX = .4; tap.EndY = .5;
            check(invalid(tap, false), "Tap retained unexpected swipe coordinates");
        } });
        test("Swipe drafts require two complete different points before playback", new Runnable() { public void run() {
            TapAction swipe = new TapAction(); swipe.Kind = "Swipe"; swipe.validate(false);
            check(!swipe.isMapped() && invalid(swipe, true), "Unmapped swipe was ready");
            swipe.X = .1; swipe.Y = .2; swipe.validate(false);
            check(swipe.hasStartPoint() && !swipe.hasEndPoint() && !swipe.isMapped() && invalid(swipe, true), "Start-only swipe was ready");
            swipe.EndX = .1; swipe.EndY = .2;
            check(!swipe.isMapped() && invalid(swipe, false), "Zero-length swipe was accepted");
            swipe.EndY = .3; swipe.validate(true);
            check(swipe.isMapped(), "A complete swipe stayed unmapped");
        } });
        test("Invalid gesture kinds never silently become taps", new Runnable() { public void run() {
            TapAction swipe = swipe("Swipe", 1000);
            for (String kind : new String[] { null, "", "swipe", "Swipe ", "Pinch", "TapTap" }) {
                swipe.Kind = kind; check(invalid(swipe, false), "Unsupported kind was accepted: " + kind);
            }
        } });
        test("Swipe endpoints reject partial, out-of-range and nonfinite coordinates", new Runnable() { public void run() {
            double[] invalidNumbers = new double[] { -.1, 1.01, Double.NaN, Double.POSITIVE_INFINITY, Double.NEGATIVE_INFINITY };
            for (double coordinate : invalidNumbers) {
                TapAction swipe = swipe("Swipe", 1000); swipe.EndX = coordinate;
                check(invalid(swipe, false), "Invalid end X accepted");
                swipe = swipe("Swipe", 1000); swipe.EndY = coordinate;
                check(invalid(swipe, false), "Invalid end Y accepted");
            }
            TapAction swipe = swipe("Swipe", 1000); swipe.EndY = -1;
            check(invalid(swipe, false), "Half-mapped end pair accepted");
            swipe = swipe("Swipe", 1000); swipe.X = -1; swipe.Y = -1;
            check(invalid(swipe, false), "End-only mapping accepted");
        } });
        test("Gesture snapshots retain swipe data while mapping reset clears both ends", new Runnable() { public void run() {
            TapAction source = swipe("Swipe", 1000); TapAction snapshot = source.copy(); source.clearMapping(); source.Kind = "Tap";
            check(snapshot.isSwipe() && snapshot.isMapped() && snapshot.EndX == .2 && snapshot.EndY == .3 && snapshot.HoldMs == 400, "Snapshot lost swipe settings");
            check(source.X == -1 && source.Y == -1 && source.EndX == -1 && source.EndY == -1 && !source.isMapped(), "Reset left a stale endpoint");
            source.validate(false);
        } });
        test("Swipe duration and repeat bounds fail before dispatch", new Runnable() { public void run() {
            TapAction swipe = swipe("Swipe", 1000);
            for (int duration : new int[] { 0, 9, 1001 }) { swipe.HoldMs = duration; check(invalid(swipe, true), "Invalid swipe duration accepted"); }
            swipe.HoldMs = 400; swipe.IntervalMs = 300;
            check(invalid(swipe, true), "Duration exceeded interval");
            swipe.IntervalMs = 1000; swipe.Burst = 21;
            check(invalid(swipe, true), "Oversized swipe burst accepted");
        } });
        test("Swipes and taps serialize through callbacks and count each gesture once", new Runnable() { public void run() {
            Harness h = new Harness(); h.automatic = false; h.start(swipe("Swipe", 2000), action("Tap", 150));
            h.tick(0); h.tick(1500);
            check(h.gestures.size() == 1 && h.gestures.get(0).isSwipe() && h.gestures.get(0).EndX == .2, "Swipe overlapped another gesture or lost its end");
            h.pending.completed(); h.tick(1500);
            check(h.gestures.size() == 2 && !h.gestures.get(1).isSwipe() && h.scheduler.getGestureCount() == 2 && h.scheduler.getTapCount() == 2, "Completed swipe did not release the next action");
        } });
        test("Swipe burst spacing includes stroke duration and avoids catch-up", new Runnable() { public void run() {
            Harness h = new Harness(); TapAction swipe = swipe("Swipe", 1000); swipe.Burst = 2;
            h.start(swipe); h.tick(0); h.tick(459); h.tick(460); h.tick(999); h.tick(1000); h.tick(60000); h.tick(60000);
            check(h.presses.equals(Arrays.asList("Swipe@0", "Swipe@460", "Swipe@1000", "Swipe@60000")), h.presses.toString());
        } });
        test("Running swipes are detached from later editor changes", new Runnable() { public void run() {
            Harness h = new Harness(); TapAction source = swipe("Swipe", 1000); h.start(source);
            source.Kind = "Tap"; source.EndX = -1; source.EndY = -1; source.HoldMs = 10;
            h.tick(0); h.tick(1000);
            check(h.gestures.size() == 2 && h.gestures.get(0).isSwipe() && h.gestures.get(1).EndY == .3 && h.gestures.get(1).HoldMs == 400, "Editor changed a running swipe");
        } });
        test("Invalid swipe start leaves a valid running session intact", new Runnable() { public void run() {
            Harness h = new Harness(); h.start(action("Tap", 1000));
            TapAction invalidSwipe = swipe("Swipe", 1000); invalidSwipe.EndX = -1; invalidSwipe.EndY = -1;
            boolean failed = false; try { h.start(invalidSwipe); } catch (IllegalArgumentException expected) { failed = true; }
            h.tick(0);
            check(failed && h.scheduler.isRunning() && h.presses.equals(Arrays.asList("Tap@0")), "An invalid swipe replaced a valid session");
        } });
        System.out.println(passed + " Android scheduler tests passed.");
    }
}
