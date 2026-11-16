package dev.farmmacro.app;

/** An explicit user-defined screen tap. Coordinates are normalized to the screen. */
public final class TapAction {
    public String Label = "Tap";
    public boolean Enabled = true;
    public double X = -1;
    public double Y = -1;
    public int IntervalMs = 3000;
    public int HoldMs = 60;
    public int Burst = 1;
    public int OffsetMs = 0;

    public TapAction copy() {
        TapAction result = new TapAction();
        result.Label = Label; result.Enabled = Enabled; result.X = X; result.Y = Y;
        result.IntervalMs = IntervalMs; result.HoldMs = HoldMs; result.Burst = Burst; result.OffsetMs = OffsetMs;
        return result;
    }
    public boolean isMapped() {
        return !Double.isNaN(X) && !Double.isInfinite(X) && !Double.isNaN(Y) && !Double.isInfinite(Y)
            && X >= 0 && X <= 1 && Y >= 0 && Y <= 1;
    }
    public void validate(boolean requireMapped) {
        if (Label == null || Label.trim().length() == 0 || Label.length() > 50) throw new IllegalArgumentException("Use an action name of 1 to 50 characters.");
        for (int index = 0; index < Label.length(); index++) if (Character.isISOControl(Label.charAt(index))) throw new IllegalArgumentException("Action names cannot contain control characters.");
        if (IntervalMs < 150 || IntervalMs > 86400000 || HoldMs < 10 || HoldMs > 1000 || HoldMs > IntervalMs
            || Burst < 1 || Burst > 20 || OffsetMs < 0 || OffsetMs > 86400000)
            throw new IllegalArgumentException("Check the interval (150 ms minimum), hold (10–1000 ms), repeats (1–20) and first delay.");
        if (!isMapped() && !(X == -1 && Y == -1)) throw new IllegalArgumentException("Choose a valid tap point or leave it unmapped.");
        if (requireMapped && Enabled && !isMapped()) throw new IllegalArgumentException("Map the enabled action “" + Label + "” first.");
    }
}
