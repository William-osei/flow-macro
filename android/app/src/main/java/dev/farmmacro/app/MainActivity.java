package dev.farmmacro.app;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.ResolveInfo;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.StateListDrawable;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.WindowInsets;
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

/** User-driven editor. Opening this activity always stops the native tap player. */
public final class MainActivity extends Activity {
    private static final int BACKGROUND = Color.rgb(14, 20, 34);
    private static final int SURFACE = Color.rgb(25, 34, 55);
    private static final int RAISED = Color.rgb(34, 47, 72);
    private static final int TEXT = Color.rgb(237, 242, 255);
    private static final int MUTED = Color.rgb(148, 163, 184);
    private static final int MINT = Color.rgb(93, 222, 186);
    private static final int ERROR = Color.rgb(255, 171, 171);
    private static final int IMPORT_PROFILE = 20;
    private static final int EXPORT_PROFILE = 21;
    private static final int MAX_FILE_BYTES = 2 * 1024 * 1024;

    private LinearLayout body;
    private LinearLayout actionsHost;
    private EditText name;
    private EditText startDelay;
    private EditText stopSeconds;
    private Spinner targets;
    private Spinner mapAction;
    private CheckBox indefinitely;
    private TextView status;
    private TextView serviceState;
    private final List<Target> targetChoices = new ArrayList<>();
    private final List<ActionEditor> editors = new ArrayList<>();
    private TapProfile current;
    private boolean binding;
    private boolean receiverRegistered;
    private String pendingExport;

    private final BroadcastReceiver statusReceiver = new BroadcastReceiver() {
        @Override public void onReceive(Context context, Intent intent) {
            if (!MacroAccessibilityService.ACTION_STATUS.equals(intent.getAction())) return;
            String message = intent.getStringExtra("status");
            long count = intent.getLongExtra("count", 0);
            if (message != null) showStatus(message + "  ·  " + count + " taps", false);
            refreshServiceState();
        }
    };

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().setStatusBarColor(BACKGROUND);
        getWindow().setNavigationBarColor(BACKGROUND);
        if (state != null) pendingExport = state.getString("pendingExport");
        buildInterface();
        try { current = ProfileStore.loadCurrent(this); }
        catch (Exception error) { current = null; }
        if (current == null) current = defaultProfile();
        bind(current);
    }

    @Override protected void onStart() {
        super.onStart();
        IntentFilter filter = new IntentFilter(MacroAccessibilityService.ACTION_STATUS);
        if (Build.VERSION.SDK_INT >= 33) registerReceiver(statusReceiver, filter, Context.RECEIVER_NOT_EXPORTED);
        else registerReceiver(statusReceiver, filter);
        receiverRegistered = true;
    }

    @Override protected void onResume() {
        super.onResume();
        MacroAccessibilityService.setEditorVisible(true);
        MacroAccessibilityService service = MacroAccessibilityService.getInstance();
        if (service != null) service.stopMacro();
        try {
            TapProfile saved = ProfileStore.loadCurrent(this);
            if (saved != null) bind(saved);
        } catch (Exception error) {
            showStatus("Could not reload the saved draft: " + friendly(error), true);
        }
        refreshServiceState();
    }

    @Override protected void onPause() {
        // Preserve a valid draft before opening the target, Settings or a file picker.
        try { ProfileStore.save(this, capture()); } catch (Exception ignored) { }
        MacroAccessibilityService.setEditorVisible(false);
        super.onPause();
    }

    @Override protected void onStop() {
        if (receiverRegistered) { unregisterReceiver(statusReceiver); receiverRegistered = false; }
        super.onStop();
    }

    @Override protected void onSaveInstanceState(Bundle state) {
        super.onSaveInstanceState(state);
        state.putString("pendingExport", pendingExport);
    }

    private void buildInterface() {
        final LinearLayout outer = column();
        outer.setBackgroundColor(BACKGROUND);
        outer.setPadding(dp(18), dp(8), dp(18), dp(12));
        outer.setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
            @Override public WindowInsets onApplyWindowInsets(View view, WindowInsets insets) {
                view.setPadding(dp(18) + insets.getSystemWindowInsetLeft(),
                        dp(8) + insets.getSystemWindowInsetTop(),
                        dp(18) + insets.getSystemWindowInsetRight(),
                        dp(12) + insets.getSystemWindowInsetBottom());
                return insets;
            }
        });
        setContentView(outer);
        outer.requestApplyInsets();

        LinearLayout header = row();
        header.setGravity(Gravity.CENTER_VERTICAL);
        ImageView mark = new ImageView(this);
        mark.setImageResource(R.drawable.ic_launcher);
        mark.setContentDescription("Flow Macro icon");
        header.addView(mark, new LinearLayout.LayoutParams(dp(44), dp(44)));
        LinearLayout heading = column();
        heading.setPadding(dp(12), 0, 0, 0);
        heading.addView(text("Flow Macro", 26, TEXT, true));
        heading.addView(text("ANDROID  ·  LOCAL TOUCH ROUTINES", 10, MUTED, false));
        header.addView(heading, weighted());
        outer.addView(header, fullHeight(66));

        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setClipToPadding(false);
        body = column();
        body.setPadding(0, dp(4), 0, dp(12));
        scroll.addView(body, new ScrollView.LayoutParams(-1, -2));
        outer.addView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));

        LinearLayout setup = card("Connect touch controls");
        setup.addView(paragraph("When you enable the accessibility service, Flow Macro can perform your saved taps and check which app is active. It does not save screen text or send it anywhere. You choose when every routine starts."));
        serviceState = text("Service not connected", 13, MUTED, true);
        serviceState.setPadding(0, dp(10), 0, dp(4));
        setup.addView(serviceState);
        setup.addView(button("Open accessibility settings", false, new View.OnClickListener() {
            @Override public void onClick(View view) {
                saveDraft(false);
                startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));
            }
        }));
        setup.addView(paragraph("Enable Flow Macro manually in Android Settings. Some phones restrict services for apps installed from a file. The app cannot enable this access for you."));

        LinearLayout profiles = card("Your profile");
        profiles.addView(label("PROFILE NAME"));
        name = edit("Routine name", false);
        name.setSingleLine(true);
        name.setFilters(new android.text.InputFilter[] { new android.text.InputFilter.LengthFilter(60) });
        profiles.addView(name, fieldParams());
        profiles.addView(label("TARGET APP"));
        targets = spinner();
        discoverTargets();
        profiles.addView(targets, fieldParams());
        targets.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> parent, View view, int position, long id) {
                if (binding || current == null || position >= targetChoices.size() || position != targets.getSelectedItemPosition()) return;
                String selected = targetChoices.get(position).packageName;
                if (!selected.equals(current.TargetPackage)) {
                    current.TargetPackage = selected;
                    current.ScreenWidth = 0; current.ScreenHeight = 0;
                    for (ActionEditor editor : editors) {
                        editor.action.X = -1; editor.action.Y = -1; editor.updatePoint();
                    }
                    showStatus("Target changed. Map each enabled action in the new app.", false);
                }
            }
            @Override public void onNothingSelected(AdapterView<?> parent) { }
        });
        LinearLayout saves = row();
        addWeightedButton(saves, "Save", false, new View.OnClickListener() {
            @Override public void onClick(View view) { saveDraft(true); }
        });
        addWeightedButton(saves, "Load", false, new View.OnClickListener() {
            @Override public void onClick(View view) { loadProfile(); }
        });
        profiles.addView(saves);
        LinearLayout transfers = row();
        addWeightedButton(transfers, "Import JSON", false, new View.OnClickListener() {
            @Override public void onClick(View view) { importProfile(); }
        });
        addWeightedButton(transfers, "Export JSON", false, new View.OnClickListener() {
            @Override public void onClick(View view) { exportProfile(); }
        });
        profiles.addView(transfers);

        LinearLayout timing = card("Run controls");
        indefinitely = new CheckBox(this);
        indefinitely.setText("Run indefinitely");
        indefinitely.setTextColor(TEXT);
        indefinitely.setTextSize(15);
        indefinitely.setButtonTintList(android.content.res.ColorStateList.valueOf(MINT));
        indefinitely.setMinHeight(dp(48));
        timing.addView(indefinitely);
        LinearLayout timingFields = row();
        startDelay = edit("3", true);
        stopSeconds = edit("0", true);
        timingFields.addView(field("Start delay · seconds", startDelay), weighted());
        timingFields.addView(field("Stop after · active seconds", stopSeconds), weighted());
        timing.addView(timingFields);
        indefinitely.setOnCheckedChangeListener((buttonView, checked) -> {
            stopSeconds.setEnabled(!checked);
            if (!binding) {
                if (checked) stopSeconds.setText("0");
                else if (stopSeconds.getText().toString().trim().equals("0")) stopSeconds.setText("60");
            }
        });
        timing.addView(paragraph("The floating Start button begins your routine only while the target app is active. Leaving that app, locking the screen or rotating the phone stops it. Press Start again after checking your points."));

        LinearLayout mapping = card("Map your tap points");
        mapping.addView(paragraph("Choose an action, open the target, then use Map on the floating controller and touch the desired point once. That touch saves a location without clicking through. Start with the tap test pad before choosing another app."));
        mapAction = spinner();
        mapping.addView(mapAction, fieldParams());
        mapping.addView(button("Map selected action", false, new View.OnClickListener() {
            @Override public void onClick(View view) { openTargetWithControls(true); }
        }));
        mapping.addView(button("Open tap test pad", false, new View.OnClickListener() {
            @Override public void onClick(View view) { openTapPad(); }
        }));

        LinearLayout actionsTitle = row();
        actionsTitle.setGravity(Gravity.CENTER_VERTICAL);
        actionsTitle.addView(text("Tap actions", 20, TEXT, true), weighted());
        Button add = button("+ Add", false, new View.OnClickListener() {
            @Override public void onClick(View view) {
                if (editors.size() >= 32) { showStatus("Use at most 32 tap actions.", true); return; }
                TapAction action = new TapAction();
                action.Label = "Tap " + (editors.size() + 1);
                addAction(action); refreshMapActions();
            }
        });
        actionsTitle.addView(add, new LinearLayout.LayoutParams(dp(104), dp(48)));
        body.addView(actionsTitle, new LinearLayout.LayoutParams(-1, dp(58)));
        actionsHost = column(); body.addView(actionsHost);

        status = text("Save a draft, map your points, then open the target.", 12, MUTED, false);
        status.setPadding(dp(4), dp(10), dp(4), dp(8));
        outer.addView(status, new LinearLayout.LayoutParams(-1, -2));
        LinearLayout footer = row();
        addWeightedButton(footer, "Open target & controls", true, new View.OnClickListener() {
            @Override public void onClick(View view) { openTargetWithControls(false); }
        });
        Button stop = button("Stop", false, new View.OnClickListener() {
            @Override public void onClick(View view) {
                MacroAccessibilityService service = MacroAccessibilityService.getInstance();
                if (service != null) service.stopMacro();
                showStatus("Stopped. No routine starts from this editor.", false);
            }
        });
        footer.addView(stop, new LinearLayout.LayoutParams(dp(88), dp(50)));
        outer.addView(footer);
    }

    private void discoverTargets() {
        targetChoices.clear();
        targetChoices.add(new Target("Flow Macro · tap test", getPackageName()));
        Intent launcher = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        List<ResolveInfo> installed = getPackageManager().queryIntentActivities(launcher, 0);
        ArrayList<Target> found = new ArrayList<>();
        for (ResolveInfo info : installed) {
            String packageName = info.activityInfo.packageName;
            if (packageName.equals(getPackageName())) continue;
            boolean exists = false;
            for (Target target : found) if (target.packageName.equals(packageName)) { exists = true; break; }
            if (!exists) found.add(new Target(String.valueOf(info.loadLabel(getPackageManager())), packageName));
        }
        Collections.sort(found, Comparator.comparing(target -> target.label.toLowerCase(java.util.Locale.ROOT)));
        targetChoices.addAll(found);
        refreshTargets();
    }

    private int ensureTarget(String packageName) {
        for (int index = 0; index < targetChoices.size(); index++)
            if (targetChoices.get(index).packageName.equals(packageName)) return index;
        targetChoices.add(new Target(packageName + " · unavailable", packageName));
        return targetChoices.size() - 1;
    }

    private void refreshTargets() { setSpinnerItems(targets, targetChoices); }

    private TapProfile defaultProfile() {
        return TapProfile.defaults();
    }

    private void bind(TapProfile profile) {
        binding = true;
        try {
            current = profile.copy();
            name.setText(current.Name);
            int selected = ensureTarget(current.TargetPackage); refreshTargets(); targets.setSelection(selected);
            startDelay.setText(seconds(current.StartDelayMs));
            stopSeconds.setText(seconds(current.SessionLimitMs));
            indefinitely.setChecked(current.SessionLimitMs == 0);
            stopSeconds.setEnabled(current.SessionLimitMs != 0);
            editors.clear(); actionsHost.removeAllViews();
            for (TapAction action : current.Actions) addAction(action.copy());
            refreshMapActions();
        } finally { binding = false; }
    }

    private TapProfile capture() {
        TapProfile profile = current == null ? new TapProfile() : current.copy();
        profile.Name = name.getText().toString().trim();
        Target target = (Target) targets.getSelectedItem();
        if (target == null) throw new IllegalArgumentException("Choose a target app.");
        profile.TargetPackage = target.packageName;
        profile.StartDelayMs = milliseconds(startDelay, "Start delay");
        profile.SessionLimitMs = indefinitely.isChecked() ? 0 : milliseconds(stopSeconds, "Timed stop");
        profile.Actions.clear();
        for (ActionEditor editor : editors) profile.Actions.add(editor.read());
        profile.validate(false);
        return profile;
    }

    private boolean saveDraft(boolean report) {
        try {
            current = capture(); ProfileStore.save(this, current);
            refreshMapActions();
            if (report) showStatus("Saved “" + current.Name + "” on this phone.", false);
            return true;
        } catch (Exception error) {
            showStatus(friendly(error), true); return false;
        }
    }

    private void loadProfile() {
        try {
            final List<String> names = ProfileStore.listNames(this);
            if (names.isEmpty()) { showStatus("No saved profiles yet. Name a routine and choose Save.", false); return; }
            new AlertDialog.Builder(this).setTitle("Load profile")
                    .setItems(names.toArray(new String[0]), (dialog, index) -> {
                        try {
                            ProfileStore.select(this, names.get(index));
                            bind(ProfileStore.load(this, names.get(index)));
                            showStatus("Loaded. Review the target and mapped points before starting.", false);
                        } catch (Exception error) { showStatus(friendly(error), true); }
                    }).setNegativeButton("Cancel", null).show();
        } catch (Exception error) { showStatus(friendly(error), true); }
    }

    private void openTargetWithControls(boolean mapping) {
        if (!saveDraft(false)) return;
        MacroAccessibilityService service = MacroAccessibilityService.getInstance();
        if (service == null) { showStatus("Enable Flow Macro in accessibility settings first.", true); return; }
        Intent launch = targetIntent(current.TargetPackage);
        if (launch == null) { showStatus("That target app is not available. Install it or choose another target.", true); return; }
        try {
            service.showController(current.copy());
            service.setMapIndex(Math.max(0, mapAction.getSelectedItemPosition()));
            startActivity(launch);
            Toast.makeText(this, mapping ? "In the target app, use Map on the floating controller." : "Check your points, then use the floating Start button.", Toast.LENGTH_LONG).show();
        } catch (Exception error) { showStatus(friendly(error), true); }
    }

    private Intent targetIntent(String packageName) {
        if (packageName.equals(getPackageName())) return new Intent(this, TapPadActivity.class);
        return getPackageManager().getLaunchIntentForPackage(packageName);
    }

    private void openTapPad() {
        try {
            TapProfile profile = capture();
            profile.TargetPackage = getPackageName();
            profile.ScreenWidth = 0; profile.ScreenHeight = 0;
            for (TapAction action : profile.Actions) { action.X = -1; action.Y = -1; }
            bind(profile); ProfileStore.save(this, profile);
            MacroAccessibilityService service = MacroAccessibilityService.getInstance();
            if (service != null) { service.showController(profile); service.setMapIndex(0); }
            startActivity(new Intent(this, TapPadActivity.class));
        } catch (Exception error) { showStatus(friendly(error), true); }
    }

    private void importProfile() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("application/json");
        try { startActivityForResult(intent, IMPORT_PROFILE); }
        catch (Exception error) { showStatus("No file picker is available: " + friendly(error), true); }
    }

    private void exportProfile() {
        try {
            TapProfile profile = capture();
            pendingExport = ProfileStore.exportJson(profile);
            Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE); intent.setType("application/json");
            intent.putExtra(Intent.EXTRA_TITLE, exportName(profile.Name));
            startActivityForResult(intent, EXPORT_PROFILE);
        } catch (Exception error) { pendingExport = null; showStatus(friendly(error), true); }
    }

    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request, result, data);
        if (result != RESULT_OK || data == null || data.getData() == null) {
            if (request == EXPORT_PROFILE) pendingExport = null;
            return;
        }
        try {
            if (request == IMPORT_PROFILE) {
                String json;
                try (InputStream stream = getContentResolver().openInputStream(data.getData())) {
                    if (stream == null) throw new IllegalArgumentException("Could not open this file.");
                    ByteArrayOutputStream bytes = new ByteArrayOutputStream();
                    byte[] buffer = new byte[8192]; int count;
                    while ((count = stream.read(buffer)) != -1) {
                        if (bytes.size() + count > MAX_FILE_BYTES) throw new IllegalArgumentException("Use a profile file smaller than 2 MB.");
                        bytes.write(buffer, 0, count);
                    }
                    json = StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT)
                            .onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes.toByteArray())).toString();
                    if (json.startsWith("\ufeff")) json = json.substring(1);
                }
                TapProfile profile = ProfileStore.importJson(this, json);
                bind(profile);
                showStatus("Imported. Map all enabled points on this phone before running.", false);
            } else if (request == EXPORT_PROFILE) {
                if (pendingExport == null) throw new IllegalArgumentException("Choose Export again to prepare the profile.");
                try (OutputStream stream = getContentResolver().openOutputStream(data.getData(), "wt")) {
                    if (stream == null) throw new IllegalArgumentException("Could not write this file.");
                    stream.write(pendingExport.getBytes(StandardCharsets.UTF_8));
                }
                pendingExport = null;
                showStatus("Exported a mobile profile. Re-map its points when importing on another phone.", false);
            }
        } catch (Exception error) { pendingExport = null; showStatus("Could not transfer the profile: " + friendly(error), true); }
    }

    private void addAction(TapAction action) {
        ActionEditor editor = new ActionEditor(action);
        editors.add(editor); actionsHost.addView(editor.view);
    }

    private void refreshMapActions() {
        int previous = mapAction.getSelectedItemPosition();
        ArrayList<String> labels = new ArrayList<>();
        for (int index = 0; index < editors.size(); index++) {
            String title = editors.get(index).label.getText().toString().trim();
            labels.add((index + 1) + " · " + (title.isEmpty() ? "Untitled tap" : title));
        }
        setSpinnerItems(mapAction, labels);
        if (!labels.isEmpty()) mapAction.setSelection(Math.min(Math.max(previous, 0), labels.size() - 1));
    }

    private void refreshServiceState() {
        if (serviceState == null) return;
        boolean ready = MacroAccessibilityService.isAvailable();
        serviceState.setText(ready ? "Connected · waiting for your Start" : "Service not connected");
        serviceState.setTextColor(ready ? MINT : MUTED);
    }

    private final class ActionEditor {
        final TapAction action;
        final LinearLayout view;
        final EditText label, interval, hold, burst, offset;
        final CheckBox enabled;
        final TextView point;
        ActionEditor(TapAction source) {
            action = source.copy();
            view = standaloneCard();
            LinearLayout titleRow = row();
            label = edit("Tap label", false); label.setText(action.Label);
            label.setSingleLine(true);
            label.setFilters(new android.text.InputFilter[] { new android.text.InputFilter.LengthFilter(60) });
            titleRow.addView(label, weighted());
            Button remove = button("Remove", false, v -> {
                editors.remove(this); actionsHost.removeView(view); refreshMapActions();
            });
            titleRow.addView(remove, new LinearLayout.LayoutParams(dp(96), dp(48)));
            view.addView(titleRow);
            enabled = new CheckBox(MainActivity.this);
            enabled.setText("Enabled"); enabled.setTextColor(TEXT); enabled.setChecked(action.Enabled);
            enabled.setButtonTintList(android.content.res.ColorStateList.valueOf(MINT));
            enabled.setMinHeight(dp(44)); view.addView(enabled);
            point = text("", 12, MUTED, false); point.setPadding(0, 0, 0, dp(10)); view.addView(point); updatePoint();
            LinearLayout first = row();
            interval = edit("3", true); interval.setText(seconds(action.IntervalMs));
            offset = edit("0", true); offset.setText(seconds(action.OffsetMs));
            first.addView(field("Every · seconds", interval), weighted());
            first.addView(field("First after · seconds", offset), weighted()); view.addView(first);
            LinearLayout second = row();
            hold = edit("60", true); hold.setText(String.valueOf(action.HoldMs));
            burst = edit("1", true); burst.setText(String.valueOf(action.Burst));
            second.addView(field("Hold · milliseconds", hold), weighted());
            second.addView(field("Taps per cycle", burst), weighted()); view.addView(second);
        }
        void updatePoint() {
            boolean mapped = action.X >= 0 && action.Y >= 0;
            point.setText(mapped ? String.format(java.util.Locale.ROOT, "Mapped point · %.1f%% across, %.1f%% down", action.X * 100, action.Y * 100) : "Point not mapped · choose this action above, then Map");
            point.setTextColor(mapped ? MINT : MUTED);
        }
        TapAction read() {
            TapAction value = action.copy(); value.Label = label.getText().toString().trim();
            value.Enabled = enabled.isChecked(); value.IntervalMs = milliseconds(interval, "Tap interval");
            value.OffsetMs = milliseconds(offset, "Initial delay");
            value.HoldMs = integer(hold, "Hold time"); value.Burst = integer(burst, "Taps per cycle");
            return value;
        }
    }

    private static final class Target {
        final String label, packageName;
        Target(String label, String packageName) { this.label = label; this.packageName = packageName; }
        @Override public String toString() { return label; }
    }

    private LinearLayout column() { LinearLayout view = new LinearLayout(this); view.setOrientation(LinearLayout.VERTICAL); return view; }
    private LinearLayout row() { LinearLayout view = new LinearLayout(this); view.setOrientation(LinearLayout.HORIZONTAL); return view; }
    private LinearLayout.LayoutParams weighted() { LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, -2, 1); p.setMargins(0, 0, dp(8), 0); return p; }
    private LinearLayout.LayoutParams fullHeight(int height) { return new LinearLayout.LayoutParams(-1, dp(height)); }
    private LinearLayout.LayoutParams fieldParams() { LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1, dp(48)); p.setMargins(0, dp(4), 0, dp(8)); return p; }
    private TextView text(String value, int sp, int color, boolean bold) {
        TextView view = new TextView(this); view.setText(value); view.setTextSize(sp); view.setTextColor(color);
        if (bold) view.setTypeface(Typeface.DEFAULT, Typeface.BOLD); return view;
    }
    private TextView label(String value) { TextView view = text(value, 11, MUTED, true); view.setPadding(0, dp(8), 0, dp(2)); return view; }
    private TextView paragraph(String value) { TextView view = text(value, 13, MUTED, false); view.setLineSpacing(dp(3), 1); view.setPadding(0, dp(6), 0, dp(6)); return view; }
    private LinearLayout standaloneCard() {
        LinearLayout card = column(); card.setPadding(dp(16), dp(12), dp(16), dp(14)); card.setBackground(shape(SURFACE, 16, Color.rgb(53, 68, 94)));
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1, -2); p.setMargins(0, 0, 0, dp(14)); card.setLayoutParams(p); return card;
    }
    private LinearLayout card(String title) { LinearLayout view = standaloneCard(); view.addView(text(title, 18, TEXT, true)); body.addView(view); return view; }
    private LinearLayout field(String title, EditText editor) { LinearLayout field = column(); field.addView(label(title)); field.addView(editor, fieldParams()); return field; }
    private EditText edit(String hint, boolean numeric) {
        EditText view = new EditText(this); view.setHint(hint); view.setTextColor(TEXT); view.setHintTextColor(MUTED); view.setTextSize(15); view.setPadding(dp(12), dp(8), dp(12), dp(8));
        view.setSingleLine(true); view.setBackground(shape(RAISED, 9, Color.rgb(53, 68, 94)));
        if (numeric) view.setInputType(InputType.TYPE_CLASS_NUMBER | InputType.TYPE_NUMBER_FLAG_DECIMAL);
        return view;
    }
    private Button button(String title, boolean primary, View.OnClickListener click) {
        Button view = new Button(this); view.setText(title); view.setTextSize(13); view.setAllCaps(false); view.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        view.setTextColor(primary ? BACKGROUND : TEXT); view.setMinHeight(dp(48)); view.setMinimumHeight(dp(48)); view.setPadding(dp(10), 0, dp(10), 0);
        StateListDrawable states = new StateListDrawable(); states.addState(new int[] { android.R.attr.state_pressed }, shape(primary ? Color.rgb(120, 238, 207) : Color.rgb(48, 65, 95), 10, primary ? MINT : Color.rgb(53, 68, 94)));
        states.addState(new int[] {}, shape(primary ? MINT : RAISED, 10, primary ? MINT : Color.rgb(53, 68, 94)));
        view.setBackgroundTintList(null); view.setBackground(states); view.setOnClickListener(click);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(-1, dp(48)); p.setMargins(0, dp(4), 0, dp(5)); view.setLayoutParams(p); return view;
    }
    private void addWeightedButton(LinearLayout row, String title, boolean primary, View.OnClickListener click) {
        Button button = button(title, primary, click); LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, dp(50), 1); p.setMargins(0, dp(4), dp(8), dp(5)); row.addView(button, p);
    }
    private GradientDrawable shape(int color, int radius, int border) { GradientDrawable shape = new GradientDrawable(); shape.setColor(color); shape.setCornerRadius(dp(radius)); shape.setStroke(dp(1), border); return shape; }
    private Spinner spinner() { Spinner view = new Spinner(this); view.setPadding(dp(10), 0, dp(8), 0); view.setBackground(shape(RAISED, 9, Color.rgb(53, 68, 94))); view.setPopupBackgroundDrawable(shape(SURFACE, 9, Color.rgb(53, 68, 94))); return view; }
    private <T> void setSpinnerItems(Spinner spinner, List<T> values) {
        ArrayAdapter<T> adapter = new ArrayAdapter<T>(this, android.R.layout.simple_spinner_item, values) {
            @Override public View getView(int position, View recycled, android.view.ViewGroup parent) { return style(super.getView(position, recycled, parent)); }
            @Override public View getDropDownView(int position, View recycled, android.view.ViewGroup parent) { return style(super.getDropDownView(position, recycled, parent)); }
            private View style(View view) { TextView label = (TextView) view; label.setTextColor(TEXT); label.setTextSize(14); label.setPadding(dp(12), dp(12), dp(12), dp(12)); label.setBackgroundColor(SURFACE); return label; }
        };
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item); spinner.setAdapter(adapter);
    }
    private int dp(int value) { return Math.round(getResources().getDisplayMetrics().density * value); }
    private void showStatus(String message, boolean error) { if (status != null) { status.setText(message); status.setTextColor(error ? ERROR : MUTED); } }
    private static String friendly(Exception error) { return error.getMessage() == null ? "Check the current settings and try again." : error.getMessage(); }
    private static String seconds(int ms) { return java.math.BigDecimal.valueOf(ms, 3).stripTrailingZeros().toPlainString(); }
    private static int milliseconds(EditText field, String title) {
        try {
            java.math.BigDecimal value = new java.math.BigDecimal(field.getText().toString().trim().replace(',', '.'));
            if (value.signum() < 0) throw new NumberFormatException();
            return value.multiply(java.math.BigDecimal.valueOf(1000)).intValueExact();
        } catch (Exception error) { throw new IllegalArgumentException(title + " must be a valid number of seconds, with at most three decimal places."); }
    }
    private static int integer(EditText field, String title) {
        try { return Integer.parseInt(field.getText().toString().trim()); }
        catch (Exception error) { throw new IllegalArgumentException(title + " must be a whole number."); }
    }
    private static String exportName(String name) {
        StringBuilder clean = new StringBuilder("FlowMacro-");
        for (char value : name.toCharArray()) clean.append(value < 32 || "\\/:*?\"<>|".indexOf(value) >= 0 ? '_' : value);
        return clean.append(".json").toString();
    }
}
