package dev.farmmacro.app;

/** Screen-space checks shared by the native gesture guard and host tests. */
public final class GestureGeometry {
    private GestureGeometry() { }

    /** Includes rectangle borders conservatively, including a zero-length tap. */
    public static boolean intersectsRect(double x, double y, double endX, double endY,
            double left, double top, double right, double bottom) {
        double[] values = new double[] { x, y, endX, endY, left, top, right, bottom };
        for (double value : values)
            if (Double.isNaN(value) || Double.isInfinite(value)) return true;
        if (right <= left || bottom <= top) return false;
        // Clip a segment against all four half-planes. A bounding-box-only test
        // would reject diagonal paths that never touch the covering window.
        double dx = endX - x, dy = endY - y;
        double[] p = new double[] { -dx, dx, -dy, dy };
        double[] q = new double[] { x - left, right - x, y - top, bottom - y };
        double first = 0, last = 1;
        for (int index = 0; index < 4; index++) {
            if (p[index] == 0) { if (q[index] < 0) return false; }
            else {
                double crossing = q[index] / p[index];
                if (p[index] < 0) first = Math.max(first, crossing);
                else last = Math.min(last, crossing);
                if (first > last) return false;
            }
        }
        return true;
    }
}
