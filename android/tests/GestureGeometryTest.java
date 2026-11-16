import dev.farmmacro.app.GestureGeometry;

public final class GestureGeometryTest {
    private static int passed;
    private static void check(String name, boolean condition) {
        if (!condition) throw new AssertionError(name);
        passed++; System.out.println("PASS " + name);
    }
    private static boolean crosses(double x, double y, double endX, double endY) {
        return GestureGeometry.intersectsRect(x, y, endX, endY, 40, 40, 60, 60);
    }
    public static void main(String[] args) {
        check("A swipe crossing a window is blocked with both endpoints outside", crosses(0, 50, 100, 50));
        check("A vertical middle-only crossing is blocked", crosses(50, 0, 50, 100));
        check("A diagonal middle-only crossing is blocked", crosses(0, 0, 100, 100));
        check("Reversing the path preserves intersection", crosses(100, 100, 0, 0));
        check("A bounding-box overlap alone does not block a diagonal miss", !crosses(0, 45, 45, 100));
        check("Parallel horizontal and vertical misses are clear", !crosses(0, 39, 100, 39) && !crosses(61, 0, 61, 100));
        check("A tangent rectangle edge is blocked conservatively", crosses(0, 40, 100, 40));
        check("Touching only a corner is blocked conservatively", crosses(0, 80, 80, 0));
        check("A tap inside or on a border is blocked", crosses(50, 50, 50, 50) && crosses(60, 50, 60, 50));
        check("A tap outside is clear", !crosses(0, 0, 0, 0));
        check("Empty window bounds do not cover a gesture", !GestureGeometry.intersectsRect(0, 0, 100, 100, 50, 50, 50, 60));
        check("Unavailable numeric geometry fails closed", crosses(Double.NaN, 0, 100, 100));
        System.out.println(passed + " gesture geometry checks passed.");
    }
}
