package dev.farmmacro.app;

/** An explicit user-defined tap or straight swipe, with normalized screen points. */
public final class TapAction {
    public String Label = "Tap";
    public String Kind = "Tap";
    public boolean Enabled = true;
    public double X = -1;
    public double Y = -1;
    public double EndX = -1;
    public double EndY = -1;
    public int IntervalMs = 3000;
    public int HoldMs = 60;
    public int Burst = 1;
    public int OffsetMs = 0;

    public TapAction copy() {
        TapAction result = new TapAction();
        result.Label = Label; result.Kind = Kind; result.Enabled = Enabled; result.X = X; result.Y = Y;
        result.EndX = EndX; result.EndY = EndY;
        result.IntervalMs = IntervalMs; result.HoldMs = HoldMs; result.Burst = Burst; result.OffsetMs = OffsetMs;
        return result;
    }
    public boolean isSwipe() { return "Swipe".equals(Kind); }
    private static boolean isPoint(double x, double y) {
        return !Double.isNaN(x) && !Double.isInfinite(x) && !Double.isNaN(y) && !Double.isInfinite(y)
            && x >= 0 && x <= 1 && y >= 0 && y <= 1;
    }
    private static boolean isUnmapped(double x, double y) { return x == -1 && y == -1; }
    public boolean hasStartPoint() { return isPoint(X, Y); }
    public boolean hasEndPoint() { return isPoint(EndX, EndY); }
    public boolean isMapped() { return hasStartPoint() && (!isSwipe() || hasEndPoint() && (X != EndX || Y != EndY)); }
    public void clearMapping() { X = -1; Y = -1; EndX = -1; EndY = -1; }
    public void validate(boolean requireMapped) {
        if (Label == null || Label.trim().length() == 0 || Label.length() > 50) throw new IllegalArgumentException("Use an action name of 1 to 50 characters.");
        for (int index = 0; index < Label.length(); index++) if (Character.isISOControl(Label.charAt(index))) throw new IllegalArgumentException("Action names cannot contain control characters.");
        if (!"Tap".equals(Kind) && !"Swipe".equals(Kind)) throw new IllegalArgumentException("Choose Tap or Swipe for each action.");
        if (IntervalMs < 150 || IntervalMs > 86400000 || HoldMs < 10 || HoldMs > 1000 || HoldMs > IntervalMs
            || Burst < 1 || Burst > 20 || OffsetMs < 0 || OffsetMs > 86400000)
            throw new IllegalArgumentException("Check the interval (150 ms minimum), hold (10–1000 ms), repeats (1–20) and first delay.");
        if (!hasStartPoint() && !isUnmapped(X, Y)) throw new IllegalArgumentException("Choose a valid start point or leave it unmapped.");
        if (!hasEndPoint() && !isUnmapped(EndX, EndY)) throw new IllegalArgumentException("Choose a valid swipe end point or leave it unmapped.");
        if (!isSwipe() && !isUnmapped(EndX, EndY)) throw new IllegalArgumentException("A tap cannot have a swipe end point.");
        if (isSwipe() && hasEndPoint() && !hasStartPoint()) throw new IllegalArgumentException("Map the swipe start point before its end point.");
        if (isSwipe() && hasStartPoint() && hasEndPoint() && X == EndX && Y == EndY) throw new IllegalArgumentException("Choose two different points for a swipe.");
        if (requireMapped && Enabled && !isMapped()) throw new IllegalArgumentException("Map the enabled action “" + Label + "”" + (isSwipe() ? " at both ends" : "") + " first.");
    }
}
