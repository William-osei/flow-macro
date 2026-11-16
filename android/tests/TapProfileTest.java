import dev.farmmacro.app.TapAction;
import dev.farmmacro.app.TapProfile;
import org.json.JSONObject;

/** Host codec checks. org.json is a test-only dependency, never packaged in the app. */
public final class TapProfileTest {
    static int passed;
    static void check(boolean condition, String message) { if (!condition) throw new AssertionError(message); }
    static void test(String name, Runnable body) { body.run(); passed++; System.out.println("PASS " + name); }
    static TapProfile tapProfile() {
        TapProfile profile = TapProfile.defaults(); profile.Name = "A general routine";
        profile.ScreenWidth = 1080; profile.ScreenHeight = 1920;
        TapAction tap = profile.Actions.get(0); tap.X = .25; tap.Y = .75;
        return profile;
    }
    static TapProfile swipeProfile() {
        TapProfile profile = tapProfile();
        TapAction swipe = new TapAction(); swipe.Label = "Swipe 2"; swipe.Kind = "Swipe";
        swipe.X = .9; swipe.Y = .8; swipe.EndX = .1; swipe.EndY = .2;
        swipe.HoldMs = 450; swipe.IntervalMs = 2000; swipe.Burst = 2; swipe.OffsetMs = 600;
        profile.Actions.add(swipe);
        return profile;
    }
    static boolean invalid(String json) {
        try { TapProfile.fromJson(json, false); return false; } catch (IllegalArgumentException expected) { return true; }
    }
    static JSONObject encodedSwipe() { return new JSONObject(swipeProfile().toJson()); }
    static JSONObject swipeItem(JSONObject root) { return root.getJSONArray("Actions").getJSONObject(1); }
    public static void main(String[] arguments) {
        test("Tap-only exports preserve the legacy version and field shape", new Runnable() { public void run() {
            TapProfile source = tapProfile(); JSONObject json = new JSONObject(source.toJson());
            JSONObject item = json.getJSONArray("Actions").getJSONObject(0);
            check(json.getInt("Version") == 1 && !item.has("Kind") && !item.has("EndX") && !item.has("EndY"), "Tap export changed the old contract");
            TapProfile restored = TapProfile.fromJson(json.toString(), false);
            check(restored.Version == 1 && "Tap".equals(restored.Actions.get(0).Kind) && restored.Actions.get(0).isMapped(), "Legacy tap import failed");
            restored.validate(true);
        } });
        test("Swipe exports opt into version two without changing the caller", new Runnable() { public void run() {
            TapProfile source = swipeProfile(); String encoded = source.toJson(); JSONObject json = new JSONObject(encoded);
            check(source.Version == 1 && source.Actions.get(1).EndX == .1 && json.getInt("Version") == 2, "Encoding mutated the draft or omitted version two");
            TapProfile restored = TapProfile.fromJson(encoded, false); restored.validate(true);
            TapAction swipe = restored.Actions.get(1);
            check(restored.Version == 2 && swipe.isSwipe() && swipe.X == .9 && swipe.Y == .8 && swipe.EndX == .1 && swipe.EndY == .2 &&
                swipe.HoldMs == 450 && swipe.Burst == 2 && swipe.OffsetMs == 600 && restored.ScreenWidth == 1080, "Swipe profile did not round-trip");
        } });
        test("Old-version swipe documents fail closed including disabled actions", new Runnable() { public void run() {
            JSONObject json = encodedSwipe(); json.put("Version", 1);
            check(invalid(json.toString()), "Version-one swipe became a tap");
            swipeItem(json).put("Enabled", false);
            check(invalid(json.toString()), "Disabled swipe bypassed the version guard");
        } });
        test("Import calibration reset clears both endpoints on every action", new Runnable() { public void run() {
            TapProfile source = swipeProfile(); source.Actions.get(1).Enabled = false;
            TapProfile restored = TapProfile.fromJson(source.toJson(), true);
            check(restored.ScreenWidth == 0 && restored.ScreenHeight == 0, "Screen calibration survived import");
            for (TapAction action : restored.Actions)
                check(action.X == -1 && action.Y == -1 && action.EndX == -1 && action.EndY == -1 && !action.isMapped(), "A point survived remapping reset");
            check(restored.Actions.get(1).isSwipe() && !restored.Actions.get(1).Enabled && restored.Actions.get(1).HoldMs == 450, "Reset changed non-coordinate settings");
        } });
        test("Partly mapped swipe drafts preserve their start and require a new end", new Runnable() { public void run() {
            TapProfile source = swipeProfile(); TapAction swipe = source.Actions.get(1); swipe.EndX = -1; swipe.EndY = -1;
            JSONObject json = new JSONObject(source.toJson()); JSONObject item = swipeItem(json);
            check(item.getDouble("X") == .9 && item.getDouble("Y") == .8 && item.isNull("EndX") && item.isNull("EndY"), "Draft encoding erased the start");
            TapAction restored = TapProfile.fromJson(json.toString(), false).Actions.get(1);
            boolean blocked = false; try { restored.validate(true); } catch (IllegalArgumentException expected) { blocked = true; }
            check(restored.hasStartPoint() && !restored.hasEndPoint() && blocked, "Half-mapped swipe was allowed to run");
        } });
        test("Unknown and null gesture kinds cannot import as taps", new Runnable() { public void run() {
            for (Object kind : new Object[] { "swipe", "Pinch", "Swipe ", "", JSONObject.NULL }) {
                JSONObject json = encodedSwipe(); swipeItem(json).put("Kind", kind);
                check(invalid(json.toString()), "Unsupported kind was accepted: " + kind);
            }
        } });
        test("Missing and mismatched endpoint pairs are rejected before calibration reset", new Runnable() { public void run() {
            JSONObject json = encodedSwipe(); swipeItem(json).remove("EndY");
            check(invalid(json.toString()), "One missing endpoint coordinate was accepted");
            json = encodedSwipe(); swipeItem(json).put("EndX", JSONObject.NULL);
            check(invalid(json.toString()), "Mixed null and numeric endpoint was accepted");
            json = encodedSwipe(); swipeItem(json).remove("EndX"); swipeItem(json).remove("EndY");
            check(invalid(json.toString()), "Both missing endpoint keys were accepted");
            boolean resetBlocked = false;
            try { TapProfile.fromJson(json.toString(), true); } catch (IllegalArgumentException expected) { resetBlocked = true; }
            check(resetBlocked, "Reset calibration hid malformed input");
        } });
        test("Coordinate ranges types and identical swipe points are rejected", new Runnable() { public void run() {
            for (Object coordinate : new Object[] { -.01, 1.01, "0.5", true, "NaN", "Infinity" }) {
                JSONObject json = encodedSwipe(); swipeItem(json).put("EndX", coordinate);
                check(invalid(json.toString()), "Invalid coordinate accepted: " + coordinate);
            }
            JSONObject json = encodedSwipe(); swipeItem(json).put("EndX", .9); swipeItem(json).put("EndY", .8);
            check(invalid(json.toString()), "Identical swipe points were accepted");
        } });
        test("Tap documents reject swipe endpoints and incomplete start coordinates", new Runnable() { public void run() {
            JSONObject json = new JSONObject(tapProfile().toJson()); JSONObject item = json.getJSONArray("Actions").getJSONObject(0);
            item.put("EndX", .4); item.put("EndY", .5);
            check(invalid(json.toString()), "A tap silently ignored swipe endpoint coordinates");
            json = new JSONObject(tapProfile().toJson()); json.getJSONArray("Actions").getJSONObject(0).remove("Y");
            check(invalid(json.toString()), "Missing tap Y was accepted");
        } });
        test("Profile copies detach swipe points and canonical export downgrades tap-only drafts", new Runnable() { public void run() {
            TapProfile source = swipeProfile(); TapProfile copied = source.copy(); copied.Actions.get(1).EndX = .6;
            check(source.Actions.get(1).EndX == .1 && copied.Actions.get(1).isSwipe(), "Copy shared action data");
            source.Version = 2; source.Actions.remove(1);
            check(new JSONObject(source.toJson()).getInt("Version") == 1 && source.Version == 2, "Tap-only export retained a needless v2 requirement");
        } });
        test("Unsupported profile versions and format never enter the action parser", new Runnable() { public void run() {
            for (Object version : new Object[] { -1, 0, 3, 1.5, "2", JSONObject.NULL }) {
                JSONObject json = encodedSwipe(); json.put("Version", version);
                check(invalid(json.toString()), "Unsupported version accepted: " + version);
            }
            JSONObject json = encodedSwipe(); json.put("Format", "Other.Mobile");
            check(invalid(json.toString()), "Unknown format accepted");
        } });
        test("Unicode names and empty unmapped swipe drafts remain portable", new Runnable() { public void run() {
            TapProfile source = swipeProfile(); source.Name = "  Gestures \u2014 \u00e9  "; source.Actions.get(1).clearMapping();
            TapProfile restored = TapProfile.fromJson(source.toJson(), false);
            check("Gestures \u2014 \u00e9".equals(restored.Name) && restored.Actions.get(1).isSwipe() && !restored.Actions.get(1).hasStartPoint(), "Unicode or empty draft changed");
        } });
        System.out.println(passed + " Android profile codec tests passed.");
    }
}
