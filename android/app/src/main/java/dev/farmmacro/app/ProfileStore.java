package dev.farmmacro.app;

import android.content.Context;
import android.content.SharedPreferences;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import org.json.JSONException;
import org.json.JSONObject;

/** Local profiles only. There is no network or account integration. */
public final class ProfileStore {
    private static final String PREFS = "farm_macro_profiles";
    private ProfileStore() { }
    private static SharedPreferences prefs(Context context) { return context.getApplicationContext().getSharedPreferences(PREFS, Context.MODE_PRIVATE); }
    private static JSONObject document(Context context) {
        try { return new JSONObject(prefs(context).getString("profiles", "{}")); }
        catch (JSONException error) { throw new IllegalArgumentException("The local profile library is damaged.", error); }
    }
    public static synchronized void save(Context context, TapProfile source) {
        source.validate(false);
        TapProfile profile = source.copy(); profile.Name = profile.Name.trim();
        JSONObject all = document(context);
        if (!all.has(profile.Name) && all.length() >= 100) throw new IllegalArgumentException("The library can hold at most 100 profiles.");
        try {
            all.put(profile.Name, new JSONObject(profile.toJson()));
            if (all.toString().length() > 2 * 1024 * 1024) throw new IllegalArgumentException("The local profile library is full.");
            prefs(context).edit().putString("profiles", all.toString()).putString("current", profile.Name).apply();
        } catch (JSONException error) { throw new IllegalArgumentException("Could not save the profile.", error); }
    }
    public static TapProfile loadCurrent(Context context) {
        String current = prefs(context).getString("current", "");
        if (current.length() == 0) return TapProfile.defaults();
        try { return load(context, current); }
        catch (IllegalArgumentException error) { return TapProfile.defaults(); }
    }
    public static TapProfile load(Context context, String name) {
        try {
            JSONObject all = document(context);
            if (!all.has(name)) throw new IllegalArgumentException("Choose a saved profile.");
            return TapProfile.fromJson(all.getJSONObject(name).toString(), false);
        } catch (JSONException error) { throw new IllegalArgumentException("Could not read the profile.", error); }
    }
    public static List<String> listNames(Context context) {
        JSONObject all = document(context);
        List<String> names = new ArrayList<String>();
        java.util.Iterator<String> iterator = all.keys();
        while (iterator.hasNext()) names.add(iterator.next());
        Collections.sort(names, String.CASE_INSENSITIVE_ORDER);
        return names;
    }
    public static void select(Context context, String name) {
        load(context, name);
        prefs(context).edit().putString("current", name).apply();
    }
    public static TapProfile importJson(Context context, String json) {
        TapProfile profile = TapProfile.fromJson(json, true);
        List<String> names = listNames(context);
        String original = profile.Name.trim();
        if (contains(names, original)) {
            for (int suffix = 1; suffix <= 100; suffix++) {
                String tail = suffix == 1 ? " (imported)" : " (imported " + suffix + ")";
                int end = Math.min(original.length(), 60 - tail.length());
                if (end > 0 && Character.isHighSurrogate(original.charAt(end - 1))) end--;
                String candidate = original.substring(0, end).trim() + tail;
                if (!contains(names, candidate)) { profile.Name = candidate; break; }
            }
        }
        save(context, profile);
        return profile.copy();
    }
    private static boolean contains(List<String> names, String name) {
        for (String value : names) if (value.equalsIgnoreCase(name)) return true;
        return false;
    }
    public static String exportJson(TapProfile profile) { return profile.toJson(); }
}
