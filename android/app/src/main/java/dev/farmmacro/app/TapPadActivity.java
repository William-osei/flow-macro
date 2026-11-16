package dev.farmmacro.app;

import android.app.Activity;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

/** A local receiver for testing mapped touch points before using another app. */
public final class TapPadActivity extends Activity {
    private TextView countLabel;
    private TextView lastTap;
    private long taps;
    private final int background = Color.rgb(14, 20, 34);
    private final int text = Color.rgb(237, 242, 255);

    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        if (saved != null) taps = saved.getLong("taps", 0);
        getWindow().setStatusBarColor(background); getWindow().setNavigationBarColor(background);
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL); root.setBackgroundColor(background);
        root.setPadding(dp(20), dp(16), dp(20), dp(16));
        root.setOnApplyWindowInsetsListener((view, insets) -> {
            view.setPadding(dp(20) + insets.getSystemWindowInsetLeft(), dp(16) + insets.getSystemWindowInsetTop(), dp(20) + insets.getSystemWindowInsetRight(), dp(16) + insets.getSystemWindowInsetBottom());
            return insets;
        });
        setContentView(root); root.requestApplyInsets();
        root.addView(label("Tap test pad", 27, true));
        TextView info = label("Map any numbered zone with the floating Map control, then use Start. These taps stay inside this test app.", 14, false);
        info.setTextColor(Color.rgb(148, 163, 184)); info.setPadding(0, dp(8), 0, dp(14)); root.addView(info);
        countLabel = label("Taps received: " + taps, 20, true); countLabel.setPadding(0, dp(4), 0, dp(8)); root.addView(countLabel);
        lastTap = label("Waiting for your first tap", 13, false); lastTap.setTextColor(Color.rgb(148, 163, 184)); root.addView(lastTap);
        LinearLayout zones = new LinearLayout(this); zones.setOrientation(LinearLayout.VERTICAL); zones.setPadding(0, dp(18), 0, dp(18));
        root.addView(zones, new LinearLayout.LayoutParams(-1, 0, 1));
        LinearLayout first = new LinearLayout(this); zones.addView(first, new LinearLayout.LayoutParams(-1, 0, 1));
        LinearLayout second = new LinearLayout(this); zones.addView(second, new LinearLayout.LayoutParams(-1, 0, 1));
        addZone(first, "1", "First tap point", Color.rgb(37, 83, 80));
        addZone(first, "2", "Second tap point", Color.rgb(61, 57, 103));
        addZone(second, "3", "Third tap point", Color.rgb(54, 70, 101));
        addZone(second, "4", "Fourth tap point", Color.rgb(36, 79, 62));
        Button done = new Button(this); done.setText("Stop & return to editor"); done.setAllCaps(false); done.setTextColor(background); done.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        GradientDrawable finish = new GradientDrawable(); finish.setColor(Color.rgb(93, 222, 186)); finish.setCornerRadius(dp(12)); done.setBackgroundTintList(null); done.setBackground(finish);
        done.setOnClickListener(view -> {
            MacroAccessibilityService service = MacroAccessibilityService.getInstance();
            if (service != null) { service.stopMacro(); service.hideController(); }
            finish();
        });
        root.addView(done, new LinearLayout.LayoutParams(-1, dp(52)));
    }

    @Override protected void onResume() {
        super.onResume();
        MacroAccessibilityService.setEditorVisible(false);
    }
    @Override protected void onPause() {
        MacroAccessibilityService service = MacroAccessibilityService.getInstance();
        if (service != null) service.stopMacro();
        super.onPause();
    }
    @Override protected void onSaveInstanceState(Bundle state) { super.onSaveInstanceState(state); state.putLong("taps", taps); }

    private void addZone(LinearLayout parent, final String key, String subtitle, int color) {
        final TextView zone = label(key + "\n" + subtitle, 21, true); zone.setGravity(Gravity.CENTER);
        GradientDrawable shape = new GradientDrawable(); shape.setColor(color); shape.setCornerRadius(dp(18)); shape.setStroke(dp(1), Color.rgb(88, 109, 140)); zone.setBackground(shape);
        zone.setContentDescription(key + " test tap point");
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0, -1, 1); params.setMargins(dp(5), dp(5), dp(5), dp(5)); parent.addView(zone, params);
        zone.setOnTouchListener((view, event) -> {
            if (event.getActionMasked() == MotionEvent.ACTION_DOWN) {
                taps++; countLabel.setText("Taps received: " + taps);
                lastTap.setText(String.format(java.util.Locale.ROOT, "%s · x %.0f, y %.0f", key, event.getRawX(), event.getRawY()));
                zone.setAlpha(0.65f);
            } else if (event.getActionMasked() == MotionEvent.ACTION_UP || event.getActionMasked() == MotionEvent.ACTION_CANCEL) zone.setAlpha(1f);
            return true;
        });
    }
    private TextView label(String value, int size, boolean bold) { TextView view = new TextView(this); view.setText(value); view.setTextSize(size); view.setTextColor(text); if (bold) view.setTypeface(Typeface.DEFAULT, Typeface.BOLD); return view; }
    private int dp(int value) { return Math.round(getResources().getDisplayMetrics().density * value); }
}
