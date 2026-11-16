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
        TapScheduler.Completion pending;
        final TapScheduler scheduler = new TapScheduler(this, this);
        public long now() { return now; }
        public boolean isTargetReady() { return focused; }
        public boolean dispatch(TapAction action, TapScheduler.Completion completion) {
            if (!accepted) return false;
            presses.add(action.Label + "@" + now); pending = completion;
            if (automatic) completion.completed();
            return true;
        }
        void tick(long time) { now = time; scheduler.tick(); }
        void start(TapAction... actions) { scheduler.start(Arrays.asList(actions), 0, 0); }
    }
    static TapAction action(String label, int interval) {
        TapAction result = new TapAction(); result.Label = label; result.X = .7; result.Y = .8; result.IntervalMs = interval; return result;
    }
    static void check(boolean condition, String message) { if (!condition) throw new AssertionError(message); }
    static void test(String name, Runnable body) { body.run(); passed++; System.out.println("PASS " + name); }

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
        System.out.println(passed + " Android scheduler tests passed.");
    }
}
