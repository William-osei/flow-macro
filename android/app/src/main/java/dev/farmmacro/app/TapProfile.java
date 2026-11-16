package dev.farmmacro.app;

import java.util.ArrayList;
import java.util.List;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

public final class TapProfile {
    public String Format = "FarmMacro.Mobile";
    public int Version = 1;
    public String Name = "My tap routine";
    public String TargetPackage = "dev.farmmacro.app";
    public int StartDelayMs = 3000;
    public int SessionLimitMs = 0;
    public int ScreenWidth = 0;
    public int ScreenHeight = 0;
    public List<TapAction> Actions = new ArrayList<TapAction>();

    public static TapProfile defaults() {
        TapProfile profile = new TapProfile();
        TapAction tap = new TapAction(); tap.Label = "Tap 1"; tap.IntervalMs = 1000; tap.HoldMs = 40;
        profile.Actions.add(tap);
        return profile;
    }
    public TapProfile copy() {
        TapProfile result = new TapProfile();
        result.Format = Format; result.Version = Version; result.Name = Name; result.TargetPackage = TargetPackage;
        result.StartDelayMs = StartDelayMs; result.SessionLimitMs = SessionLimitMs;
        result.ScreenWidth = ScreenWidth; result.ScreenHeight = ScreenHeight;
        for (TapAction action : Actions) result.Actions.add(action.copy());
        return result;
    }
    public void validate(boolean requireMapped) {
        if (!"FarmMacro.Mobile".equals(Format) || Version != 1) throw new IllegalArgumentException("Unsupported mobile profile format.");
        if (Name == null || Name.trim().length() < 1 || Name.length() > 60) throw new IllegalArgumentException("Use a profile name of 1 to 60 characters.");
        for (int index = 0; index < Name.length(); index++) if (Character.isISOControl(Name.charAt(index))) throw new IllegalArgumentException("Profile names cannot contain control characters.");
        if (TargetPackage == null || !TargetPackage.matches("[A-Za-z][A-Za-z0-9_]*(\\.[A-Za-z][A-Za-z0-9_]*)+"))
            throw new IllegalArgumentException("Choose a target app.");
        if (StartDelayMs < 1000 || StartDelayMs > 30000 || SessionLimitMs < 0 || SessionLimitMs > 86400000)
            throw new IllegalArgumentException("Use a 1–30 second start delay and a valid session limit.");
        if (ScreenWidth < 0 || ScreenHeight < 0 || (ScreenWidth == 0) != (ScreenHeight == 0)) throw new IllegalArgumentException("Invalid screen calibration.");
        if (Actions == null || Actions.size() > 32) throw new IllegalArgumentException("Use at most 32 actions.");
        int enabled = 0;
        for (TapAction action : Actions) {
            if (action == null) throw new IllegalArgumentException("An action is missing.");
            action.validate(requireMapped);
            if (action.Enabled) enabled++;
        }
        if (requireMapped && (enabled == 0 || ScreenWidth == 0 || ScreenHeight == 0)) throw new IllegalArgumentException("Enable and map at least one action before starting.");
    }
    public String toJson() {
        validate(false);
        try {
            JSONObject root = new JSONObject();
            root.put("Format", Format); root.put("Version", Version); root.put("Name", Name.trim()); root.put("TargetPackage", TargetPackage);
            root.put("StartDelayMs", StartDelayMs); root.put("SessionLimitMs", SessionLimitMs);
            root.put("ScreenWidth", ScreenWidth); root.put("ScreenHeight", ScreenHeight);
            JSONArray actions = new JSONArray();
            for (TapAction action : Actions) {
                JSONObject item = new JSONObject();
                item.put("Label", action.Label); item.put("Enabled", action.Enabled);
                item.put("X", action.isMapped() ? action.X : JSONObject.NULL); item.put("Y", action.isMapped() ? action.Y : JSONObject.NULL);
                item.put("IntervalMs", action.IntervalMs); item.put("HoldMs", action.HoldMs); item.put("Burst", action.Burst); item.put("OffsetMs", action.OffsetMs);
                actions.put(item);
            }
            root.put("Actions", actions);
            return root.toString(2);
        } catch (JSONException error) { throw new IllegalArgumentException("Could not encode the mobile profile.", error); }
    }
    public static TapProfile fromJson(String json, boolean resetCalibration) {
        if (json == null || json.length() > 2 * 1024 * 1024) throw new IllegalArgumentException("Use a mobile profile of at most 2 MB.");
        try {
            JSONObject root = new JSONObject(json);
            TapProfile profile = new TapProfile();
            profile.Format = text(root, "Format"); profile.Version = integer(root, "Version"); profile.Name = text(root, "Name");
            profile.TargetPackage = text(root, "TargetPackage"); profile.StartDelayMs = integer(root, "StartDelayMs"); profile.SessionLimitMs = integer(root, "SessionLimitMs");
            profile.ScreenWidth = integer(root, "ScreenWidth"); profile.ScreenHeight = integer(root, "ScreenHeight");
            Object list = root.get("Actions");
            if (!(list instanceof JSONArray) || ((JSONArray)list).length() > 32) throw new IllegalArgumentException("Use at most 32 actions.");
            JSONArray actions = (JSONArray)list;
            for (int index = 0; index < actions.length(); index++) {
                JSONObject item = actions.getJSONObject(index);
                TapAction action = new TapAction();
                action.Label = text(item, "Label");
                Object enabled = item.get("Enabled"); if (!(enabled instanceof Boolean)) throw new IllegalArgumentException("Invalid enabled value.");
                action.Enabled = (Boolean)enabled;
                boolean nullX = item.isNull("X"), nullY = item.isNull("Y");
                if (nullX != nullY) throw new IllegalArgumentException("A tap point needs both coordinates.");
                if (!nullX) { action.X = coordinate(item, "X"); action.Y = coordinate(item, "Y"); }
                action.IntervalMs = integer(item, "IntervalMs"); action.HoldMs = integer(item, "HoldMs");
                action.Burst = integer(item, "Burst"); action.OffsetMs = integer(item, "OffsetMs");
                profile.Actions.add(action);
            }
            profile.validate(false);
            if (resetCalibration) {
                profile.ScreenWidth = 0; profile.ScreenHeight = 0;
                for (TapAction action : profile.Actions) { action.X = -1; action.Y = -1; }
            }
            return profile;
        } catch (JSONException error) { throw new IllegalArgumentException("Invalid mobile profile: " + error.getMessage(), error); }
    }
    private static String text(JSONObject object, String name) throws JSONException {
        Object value = object.get(name);
        if (!(value instanceof String)) throw new IllegalArgumentException("Invalid " + name + ".");
        return (String)value;
    }
    private static int integer(JSONObject object, String name) throws JSONException {
        Object value = object.get(name);
        if (!(value instanceof Number)) throw new IllegalArgumentException("Invalid " + name + ".");
        double number = ((Number)value).doubleValue();
        if (Double.isNaN(number) || Double.isInfinite(number) || Math.rint(number) != number || number < Integer.MIN_VALUE || number > Integer.MAX_VALUE)
            throw new IllegalArgumentException("Invalid " + name + ".");
        return (int)number;
    }
    private static double coordinate(JSONObject object, String name) throws JSONException {
        Object value = object.get(name);
        if (!(value instanceof Number)) throw new IllegalArgumentException("Invalid tap point.");
        double number = ((Number)value).doubleValue();
        if (Double.isNaN(number) || Double.isInfinite(number) || number < 0 || number > 1) throw new IllegalArgumentException("Invalid tap point.");
        return number;
    }
}
