package dev.farmmacro.app;

import java.util.ArrayList;
import java.util.List;

/** A serialized, callback-driven scheduler. This class has no Android dependency. */
public final class TapScheduler {
    public interface Clock { long now(); }
    public interface Completion { void completed(); void cancelled(); }
    public interface Backend {
        boolean isTargetReady();
        boolean dispatch(TapAction action, Completion completion);
    }
    private static final class Row {
        TapAction action; long nextAt; long cycleAt; int remaining;
    }
    private final Clock clock;
    private final Backend backend;
    private final List<Row> rows = new ArrayList<Row>();
    private long readyAt, previousTick, activeMs, tapCount;
    private int sessionLimitMs, cursor, generation;
    private boolean running, inFlight, previouslyActive;
    private String status = "Stopped";

    public TapScheduler(Clock clock, Backend backend) {
        if (clock == null || backend == null) throw new IllegalArgumentException("A clock and tap backend are required.");
        this.clock = clock; this.backend = backend;
    }
    public void start(List<TapAction> actions, int delayMs, int limitMs) {
        if (actions == null || actions.size() > 32 || delayMs < 0 || delayMs > 30000 || limitMs < 0 || limitMs > 86400000)
            throw new IllegalArgumentException("Invalid macro settings.");
        List<Row> validated = new ArrayList<Row>();
        for (TapAction source : actions) {
            if (source == null) throw new IllegalArgumentException("An action is missing.");
            if (!source.Enabled) continue;
            TapAction action = source.copy(); action.validate(true);
            Row row = new Row(); row.action = action; validated.add(row);
        }
        if (validated.isEmpty()) throw new IllegalArgumentException("Enable and map an action first.");
        stop("Stopped");
        rows.clear(); rows.addAll(validated);
        readyAt = clock.now() + delayMs; previousTick = clock.now();
        activeMs = 0; tapCount = 0; sessionLimitMs = limitMs; cursor = 0;
        for (Row row : rows) row.nextAt = readyAt + row.action.OffsetMs;
        running = true; previouslyActive = false; inFlight = false;
        status = delayMs > 0 ? "Starting in " + ((delayMs + 999) / 1000) + "s" : "Running";
    }
    public void stop(String reason) {
        running = false; inFlight = false; previouslyActive = false; generation++;
        status = reason == null ? "Stopped" : reason;
    }
    public boolean isRunning() { return running; }
    public boolean isInFlight() { return inFlight; }
    public String getStatus() { return status; }
    public long getTapCount() { return tapCount; }
    public long getActiveMs() { return activeMs; }

    public void tick() {
        if (!running) return;
        try {
            long now = clock.now();
            long delta = Math.max(0, now - previousTick); previousTick = now;
            if (!backend.isTargetReady()) { stop("Stopped: selected app is not in front"); return; }
            if (now < readyAt) { status = "Starting in " + ((readyAt - now + 999) / 1000) + "s"; return; }
            if (previouslyActive) activeMs += delta;
            previouslyActive = true;
            if (sessionLimitMs > 0 && activeMs >= sessionLimitMs) { stop("Timed session finished"); return; }
            status = "Running";
            if (inFlight) return;
            for (int offset = 0; offset < rows.size(); offset++) {
                int index = (cursor + offset) % rows.size();
                final Row row = rows.get(index);
                if (row.nextAt > now) continue;
                if (!backend.isTargetReady()) { stop("Stopped: selected app is not in front"); return; }
                final int token = generation;
                inFlight = true;
                boolean accepted = backend.dispatch(row.action.copy(), new Completion() {
                    public void completed() { if (running && generation == token) inFlight = false; }
                    public void cancelled() { if (running && generation == token) stop("Stopped: Android cancelled a tap"); }
                });
                if (!accepted) { stop("Stopped: Android did not accept a tap"); return; }
                tapCount++;
                if (row.remaining <= 0) { row.remaining = row.action.Burst; row.cycleAt = now; }
                row.remaining--;
                long gap = row.action.HoldMs + 60L;
                row.nextAt = row.remaining > 0 ? now + gap : Math.max(row.cycleAt + row.action.IntervalMs, now + gap);
                cursor = (index + 1) % rows.size();
                return;
            }
        } catch (RuntimeException error) { stop("Stopped: " + error.getMessage()); }
    }
}
