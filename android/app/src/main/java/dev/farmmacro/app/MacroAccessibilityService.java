package dev.farmmacro.app;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.GestureDescription;
import android.app.KeyguardManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.PowerManager;
import android.os.SystemClock;
import android.util.DisplayMetrics;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.List;

/** User-enabled, local-only accessibility gestures. No screen text is read or retained. */
public final class MacroAccessibilityService extends AccessibilityService {
    public static final String ACTION_STATUS = "dev.farmmacro.app.STATUS";
    private static volatile MacroAccessibilityService instance;
    private static volatile boolean editorVisible = true;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private WindowManager windows;
    private LinearLayout controller;
    private TextView controllerStatus;
    private Button startButton, mapButton;
    private WindowManager.LayoutParams controllerParams;
    private View capture;
    private TapProfile profile;
    private TapScheduler scheduler;
    private int mapIndex;
    private int runWidth, runHeight, runRotation;
    private String status = "Service ready. Show the floating controls to begin.";
    private String lastBroadcast = "";
    private final Rect targetBounds = new Rect();
    private int targetWindowId = -1;
    private boolean receiverRegistered;

    public static MacroAccessibilityService getInstance() { return instance; }
    public static boolean isAvailable() { return instance != null; }
    public static void setEditorVisible(boolean visible) {
        editorVisible = visible;
        MacroAccessibilityService service = instance;
        if (visible && service != null) service.handler.post(new Runnable() { public void run() { service.stopMacro(); service.removeCapture(); } });
    }
    public String getStatus() { return status; }
    public long getTapCount() { return scheduler == null ? 0 : scheduler.getTapCount(); }
    public boolean isRunning() { return scheduler != null && scheduler.isRunning(); }

    @Override protected void onServiceConnected() {
        super.onServiceConnected();
        if (scheduler != null) scheduler.stop("Service reconnected");
        handler.removeCallbacks(pulse);
        if (receiverRegistered) { unregisterReceiver(screenReceiver); receiverRegistered = false; }
        instance = this;
        windows = (WindowManager)getSystemService(WINDOW_SERVICE);
        scheduler = new TapScheduler(new TapScheduler.Clock() { public long now() { return SystemClock.uptimeMillis(); } }, new TapScheduler.Backend() {
            public boolean isTargetReady() { return targetReady(true); }
            public boolean dispatch(TapAction action, final TapScheduler.Completion completion) {
                if (!targetReady(true)) return false;
                final int expectedWindowId = targetWindowId;
                float x = (float)(action.X * (runWidth - 1));
                float y = (float)(action.Y * (runHeight - 1));
                if (!targetBounds.contains((int)x, (int)y) || controllerCovers(x, y) || blockedByOverlay(x, y, expectedWindowId)) return false;
                if (!targetReady(true) || targetWindowId != expectedWindowId || !targetBounds.contains((int)x, (int)y)) return false;
                Path path = new Path(); path.moveTo(x, y);
                GestureDescription gesture = new GestureDescription.Builder().addStroke(new GestureDescription.StrokeDescription(path, 0, action.HoldMs)).build();
                return dispatchGesture(gesture, new GestureResultCallback() {
                    @Override public void onCompleted(GestureDescription description) { completion.completed(); publish(); }
                    @Override public void onCancelled(GestureDescription description) { completion.cancelled(); status = scheduler.getStatus(); publish(); }
                }, handler);
            }
        });
        IntentFilter filter = new IntentFilter(); filter.addAction(Intent.ACTION_SCREEN_OFF); filter.addAction(Intent.ACTION_SHUTDOWN);
        if (Build.VERSION.SDK_INT >= 33) registerReceiver(screenReceiver, filter, Context.RECEIVER_NOT_EXPORTED);
        else registerReceiver(screenReceiver, filter);
        receiverRegistered = true;
        publish(); handler.post(pulse);
    }
    private final BroadcastReceiver screenReceiver = new BroadcastReceiver() {
        @Override public void onReceive(Context context, Intent intent) { stopWith("Stopped: screen is off"); removeCapture(); hideController(); }
    };
    private final Runnable pulse = new Runnable() {
        public void run() {
            if (instance != MacroAccessibilityService.this) return;
            if (scheduler != null && scheduler.isRunning()) { scheduler.tick(); status = scheduler.getStatus(); }
            publish(); handler.postDelayed(this, isRunning() ? 25 : 250);
        }
    };
    @Override public void onAccessibilityEvent(AccessibilityEvent event) {
        if (isRunning() && !targetReady(true)) stopWith("Stopped: target, screen or orientation changed");
        if (capture != null && !targetReady(false)) {
            removeCapture(); status = "Mapping cancelled: focus the selected app and try again."; publish();
        }
    }
    @Override public void onInterrupt() { stopWith("Service interrupted"); removeCapture(); }
    @Override public void onConfigurationChanged(Configuration configuration) {
        super.onConfigurationChanged(configuration);
        if (isRunning()) stopWith("Stopped: screen orientation changed; check tap points");
        removeCapture();
    }
    @Override public void onDestroy() {
        stopWith("Service stopped"); removeCapture(); hideController();
        handler.removeCallbacksAndMessages(null);
        if (receiverRegistered) { unregisterReceiver(screenReceiver); receiverRegistered = false; }
        if (instance == this) instance = null;
        super.onDestroy();
    }

    public void showController(TapProfile source) {
        if (source == null) throw new IllegalArgumentException("Choose a profile first.");
        source.validate(false);
        stopMacro();
        profile = source.copy(); ProfileStore.save(this, profile);
        if (controller == null) createController();
        status = "Focus the selected app, then tap Start or Map.";
        publish();
    }
    public void setMapIndex(int index) { mapIndex = Math.max(0, index); publish(); }
    public void startMacro() {
        try {
            profile = ProfileStore.loadCurrent(this);
            profile.validate(true);
            DisplayMetrics metrics = metrics();
            if (metrics.widthPixels != profile.ScreenWidth || metrics.heightPixels != profile.ScreenHeight)
                throw new IllegalArgumentException("Screen size changed. Map the tap points again.");
            runWidth = metrics.widthPixels; runHeight = metrics.heightPixels; runRotation = windows.getDefaultDisplay().getRotation();
            if (!targetReady(true)) throw new IllegalArgumentException("Focus the selected target app with the screen unlocked.");
            if (controller == null) createController();
            scheduler.start(profile.Actions, profile.StartDelayMs, profile.SessionLimitMs);
            status = scheduler.getStatus(); publish();
        } catch (RuntimeException error) { stopWith(error.getMessage()); }
    }
    public void stopMacro() { stopWith("Stopped. An in-progress tap may finish within one second."); }
    private void stopWith(String message) {
        if (scheduler != null) scheduler.stop(message);
        status = message == null ? "Stopped" : message;
        publish();
    }
    public void hideController() {
        stopMacro(); removeCapture();
        if (controller != null) {
            try { windows.removeView(controller); } catch (IllegalArgumentException ignored) { }
            controller = null; controllerStatus = null; startButton = null; mapButton = null;
        }
    }
    private DisplayMetrics metrics() {
        DisplayMetrics result = new DisplayMetrics(); windows.getDefaultDisplay().getRealMetrics(result); return result;
    }
    private boolean targetReady(boolean verifyCalibration) {
        targetWindowId = -1;
        targetBounds.setEmpty();
        if (editorVisible || profile == null || windows == null) return false;
        PowerManager power = (PowerManager)getSystemService(POWER_SERVICE);
        KeyguardManager keyguard = (KeyguardManager)getSystemService(KEYGUARD_SERVICE);
        if (!power.isInteractive() || keyguard.isKeyguardLocked()) return false;
        if (verifyCalibration) {
            DisplayMetrics current = metrics();
            if (current.widthPixels != runWidth || current.heightPixels != runHeight || windows.getDefaultDisplay().getRotation() != runRotation) return false;
        }
        AccessibilityNodeInfo root = getRootInActiveWindow();
        if (root == null) return false;
        try {
            CharSequence packageName = root.getPackageName();
            if (packageName == null || !profile.TargetPackage.contentEquals(packageName)) return false;
            root.getBoundsInScreen(targetBounds);
            targetWindowId = root.getWindowId();
            return targetWindowId >= 0 && !targetBounds.isEmpty();
        } finally { root.recycle(); }
    }
    private boolean controllerCovers(float x, float y) {
        if (controller == null || !controller.isShown()) return false;
        int[] position = new int[2]; controller.getLocationOnScreen(position);
        return x >= position[0] && y >= position[1] && x < position[0] + controller.getWidth() && y < position[1] + controller.getHeight();
    }
    private boolean blockedByOverlay(float x, float y, int expectedWindowId) {
        List<AccessibilityWindowInfo> visible = null;
        try {
            visible = getWindows();
            if (expectedWindowId < 0 || visible == null || visible.isEmpty()) return true;
            AccessibilityWindowInfo target = null;
            for (AccessibilityWindowInfo window : visible) {
                if (window == null) return true;
                if (window.getId() == expectedWindowId) target = window;
            }
            // getWindows() is limited to interactive windows on the default display.
            // A missing target cannot establish where this global gesture would land.
            if (target == null) return true;
            final int targetLayer = target.getLayer();
            Rect bounds = new Rect();
            target.getBoundsInScreen(bounds);
            if (!bounds.contains((int)x, (int)y)) return true;
            for (AccessibilityWindowInfo window : visible) {
                if (window.getId() == expectedWindowId) continue;
                // Greater layers are above the target; equal layers have no proven order.
                // Check every window type, including other apps and nonfocusable overlays.
                if (window.getLayer() < targetLayer) continue;
                window.getBoundsInScreen(bounds);
                if (bounds.contains((int)x, (int)y)) return true;
            }
            return false;
        } catch (RuntimeException unavailable) {
            return true;
        } finally {
            if (visible != null) for (AccessibilityWindowInfo window : visible) if (window != null) window.recycle();
        }
    }

    private int dp(int value) { return (int)(value * getResources().getDisplayMetrics().density + 0.5f); }
    private GradientDrawable background(int color, int radius) {
        GradientDrawable shape = new GradientDrawable(); shape.setColor(color); shape.setCornerRadius(dp(radius)); return shape;
    }
    private Button controlButton(String title, View.OnClickListener listener) {
        Button button = new Button(this); button.setText(title); button.setTextSize(12); button.setAllCaps(false);
        button.setTextColor(Color.rgb(237, 242, 255)); button.setBackground(background(Color.rgb(34, 47, 72), 9));
        button.setMinWidth(0); button.setMinimumWidth(0); button.setMinHeight(dp(40)); button.setMinimumHeight(dp(40));
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0, dp(40), 1); params.setMargins(dp(2), 0, dp(2), 0);
        button.setLayoutParams(params); button.setOnClickListener(listener); return button;
    }
    private void createController() {
        controller = new LinearLayout(this); controller.setOrientation(LinearLayout.VERTICAL); controller.setPadding(dp(8), dp(6), dp(8), dp(8));
        controller.setBackground(background(Color.rgb(14, 20, 34), 14)); controller.setElevation(dp(8));
        TextView drag = new TextView(this); drag.setText("Flow Macro  ·  drag to move"); drag.setTextSize(12); drag.setTextColor(Color.rgb(93, 222, 186)); drag.setPadding(dp(3), dp(4), dp(3), dp(6));
        controller.addView(drag);
        controllerStatus = new TextView(this); controllerStatus.setTextColor(Color.rgb(237, 242, 255)); controllerStatus.setTextSize(11); controllerStatus.setSingleLine(true); controllerStatus.setPadding(dp(3), 0, dp(3), dp(6)); controller.addView(controllerStatus);
        LinearLayout buttons = new LinearLayout(this); buttons.setOrientation(LinearLayout.HORIZONTAL);
        startButton = controlButton("Start", new View.OnClickListener() { public void onClick(View view) { startMacro(); } });
        Button stop = controlButton("Stop", new View.OnClickListener() { public void onClick(View view) { stopMacro(); } });
        stop.setTextColor(Color.rgb(14, 43, 38)); stop.setBackground(background(Color.rgb(93, 222, 186), 9));
        mapButton = controlButton("Map point", new View.OnClickListener() { public void onClick(View view) { capturePoint(); } });
        Button close = controlButton("Hide", new View.OnClickListener() { public void onClick(View view) { hideController(); } });
        buttons.addView(startButton); buttons.addView(stop); buttons.addView(mapButton); buttons.addView(close); controller.addView(buttons);
        controllerParams = new WindowManager.LayoutParams(dp(300), WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN, PixelFormat.TRANSLUCENT);
        controllerParams.gravity = Gravity.TOP | Gravity.LEFT; controllerParams.x = dp(10); controllerParams.y = dp(70);
        drag.setOnTouchListener(new View.OnTouchListener() {
            float downX, downY; int originX, originY;
            public boolean onTouch(View view, MotionEvent event) {
                if (event.getActionMasked() == MotionEvent.ACTION_DOWN) {
                    downX = event.getRawX(); downY = event.getRawY(); originX = controllerParams.x; originY = controllerParams.y; return true;
                }
                if (event.getActionMasked() == MotionEvent.ACTION_MOVE) {
                    DisplayMetrics current = metrics();
                    controllerParams.x = Math.max(0, Math.min(current.widthPixels - controller.getWidth(), originX + (int)(event.getRawX() - downX)));
                    controllerParams.y = Math.max(0, Math.min(current.heightPixels - controller.getHeight(), originY + (int)(event.getRawY() - downY)));
                    windows.updateViewLayout(controller, controllerParams); return true;
                }
                return event.getActionMasked() == MotionEvent.ACTION_UP;
            }
        });
        windows.addView(controller, controllerParams);
    }
    private void capturePoint() {
        stopMacro();
        try {
            profile = ProfileStore.loadCurrent(this);
            if (profile.Actions.isEmpty()) throw new IllegalArgumentException("Add an action in the editor first.");
            if (!targetReady(false)) throw new IllegalArgumentException("Focus the selected target app before mapping.");
            if (mapIndex >= profile.Actions.size()) mapIndex = 0;
            final int selected = mapIndex;
            final DisplayMetrics dimensions = metrics();
            final int rotation = windows.getDefaultDisplay().getRotation();
            removeCapture();
            LinearLayout shade = new LinearLayout(this); shade.setOrientation(LinearLayout.VERTICAL); shade.setGravity(Gravity.CENTER);
            shade.setPadding(dp(24), dp(24), dp(24), dp(24)); shade.setBackgroundColor(Color.argb(80, 0, 0, 0));
            TextView instructions = new TextView(this); instructions.setText("Choose the “" + profile.Actions.get(selected).Label + "” point once\nThis touch is captured, not sent to the selected app.\nPress Back/Home to leave if you need to cancel.");
            instructions.setTextColor(Color.WHITE); instructions.setTextSize(18); instructions.setGravity(Gravity.CENTER); shade.addView(instructions);
            Button cancel = new Button(this); cancel.setText("Cancel mapping"); cancel.setAllCaps(false);
            cancel.setTextColor(Color.rgb(14, 43, 38)); cancel.setBackground(background(Color.rgb(93, 222, 186), 10));
            LinearLayout.LayoutParams cancelParams = new LinearLayout.LayoutParams(dp(180), dp(46)); cancelParams.topMargin = dp(24);
            shade.addView(cancel, cancelParams);
            cancel.setOnClickListener(new View.OnClickListener() { public void onClick(View view) { removeCapture(); status = "Mapping cancelled."; publish(); } });
            shade.setOnTouchListener(new View.OnTouchListener() {
                public boolean onTouch(View view, MotionEvent event) {
                    if (event.getActionMasked() == MotionEvent.ACTION_UP) {
                        final float x = event.getRawX(), y = event.getRawY();
                        removeCapture();
                        handler.post(new Runnable() { public void run() {
                            DisplayMetrics now = metrics();
                            if (now.widthPixels != dimensions.widthPixels || now.heightPixels != dimensions.heightPixels || windows.getDefaultDisplay().getRotation() != rotation || !targetReady(false)) {
                                status = "Mapping cancelled: target or screen changed."; publish(); return;
                            }
                            if (!targetBounds.contains((int)x, (int)y) || controllerCovers(x, y)) {
                                status = "Choose a point inside the selected app, away from the floating controls."; publish(); return;
                            }
                            if (profile.ScreenWidth != dimensions.widthPixels || profile.ScreenHeight != dimensions.heightPixels) {
                                for (TapAction action : profile.Actions) { action.X = -1; action.Y = -1; }
                            }
                            profile.ScreenWidth = dimensions.widthPixels; profile.ScreenHeight = dimensions.heightPixels;
                            TapAction action = profile.Actions.get(selected);
                            action.X = Math.max(0, Math.min(1, x / Math.max(1, dimensions.widthPixels - 1)));
                            action.Y = Math.max(0, Math.min(1, y / Math.max(1, dimensions.heightPixels - 1)));
                            ProfileStore.save(MacroAccessibilityService.this, profile);
                            mapIndex = (selected + 1) % profile.Actions.size();
                            for (int index = 0; index < profile.Actions.size(); index++) {
                                if (profile.Actions.get(index).Enabled && !profile.Actions.get(index).isMapped()) { mapIndex = index; break; }
                            }
                            status = "Mapped “" + action.Label + "”. Map the remaining actions or Start."; publish();
                        } });
                    }
                    return true;
                }
            });
            capture = shade;
            WindowManager.LayoutParams params = new WindowManager.LayoutParams(WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY, WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS, PixelFormat.TRANSLUCENT);
            params.gravity = Gravity.TOP | Gravity.LEFT;
            windows.addView(capture, params);
            status = "Mapping “" + profile.Actions.get(selected).Label + "”: touch its on-screen button."; publish();
        } catch (RuntimeException error) { status = error.getMessage(); publish(); }
    }
    private void removeCapture() {
        if (capture != null) { try { windows.removeView(capture); } catch (IllegalArgumentException ignored) { } capture = null; }
    }
    private void publish() {
        String key = status + "|" + getTapCount() + "|" + isRunning();
        if (key.equals(lastBroadcast)) return;
        if (controllerStatus != null) controllerStatus.setText((status == null ? "Stopped" : status) + " · " + getTapCount() + " taps");
        if (startButton != null) startButton.setEnabled(!isRunning());
        if (mapButton != null) mapButton.setEnabled(!isRunning());
        lastBroadcast = key;
        Intent update = new Intent(ACTION_STATUS).setPackage(getPackageName());
        update.putExtra("status", status); update.putExtra("count", getTapCount()); update.putExtra("running", isRunning());
        sendBroadcast(update);
    }
}
