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
import android.graphics.Canvas;
import android.graphics.Paint;
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
import android.widget.FrameLayout;
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
    private Button startButton, mapButton, addTapButton, addSwipeButton, removeButton, pointsButton;
    private WindowManager.LayoutParams controllerParams;
    private View capture;
    private int captureGeneration;
    private boolean pendingMapping;
    private View markers;
    private boolean pointsVisible = true;
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
        if (visible && service != null) service.handler.post(new Runnable() { public void run() { service.stopMacro(); service.removeCapture(); service.removeMarkers(); } });
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
                if (capture != null || pendingMapping || markers != null || !targetReady(true)) return false;
                final int expectedWindowId = targetWindowId;
                float x = (float)(action.X * (runWidth - 1));
                float y = (float)(action.Y * (runHeight - 1));
                float endX = action.isSwipe() ? (float)(action.EndX * (runWidth - 1)) : x;
                float endY = action.isSwipe() ? (float)(action.EndY * (runHeight - 1)) : y;
                if (!pathInsideTarget(x, y, endX, endY) || controllerCoversPath(x, y, endX, endY) || blockedByOverlay(x, y, endX, endY, expectedWindowId)) return false;
                if (!targetReady(true) || targetWindowId != expectedWindowId || !pathInsideTarget(x, y, endX, endY)) return false;
                Path path = new Path(); path.moveTo(x, y);
                if (action.isSwipe()) path.lineTo(endX, endY);
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
            updateMarkers();
            publish(); handler.postDelayed(this, isRunning() ? 25 : 250);
        }
    };
    @Override public void onAccessibilityEvent(AccessibilityEvent event) {
        if (isRunning() && !targetReady(true)) stopWith("Stopped: target, screen or orientation changed");
        if (capture != null && !targetReady(false)) {
            removeCapture(); status = "Mapping cancelled: focus the selected app and try again."; publish();
        }
        updateMarkers();
    }
    @Override public void onInterrupt() { stopWith("Service interrupted"); removeCapture(); }
    @Override public void onConfigurationChanged(Configuration configuration) {
        super.onConfigurationChanged(configuration);
        if (isRunning()) stopWith("Stopped: screen orientation changed; check mapped points");
        removeCapture(); removeMarkers(); clampController();
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
        stopMacro(); removeCapture(); removeMarkers();
        profile = source.copy(); ProfileStore.save(this, profile);
        if (controller == null) createController();
        status = "Use + Tap or + Swipe to add points in the target app.";
        publish();
    }
    public void setMapIndex(int index) { mapIndex = Math.max(0, index); publish(); }
    public void startMacro() {
        try {
            removeCapture(); removeMarkers();
            profile = ProfileStore.loadCurrent(this);
            profile.validate(true);
            DisplayMetrics metrics = metrics();
            if (metrics.widthPixels != profile.ScreenWidth || metrics.heightPixels != profile.ScreenHeight)
                throw new IllegalArgumentException("Screen size changed. Map the touch points again.");
            runWidth = metrics.widthPixels; runHeight = metrics.heightPixels; runRotation = windows.getDefaultDisplay().getRotation();
            if (!targetReady(true)) throw new IllegalArgumentException("Focus the selected target app with the screen unlocked.");
            if (controller == null) createController();
            scheduler.start(profile.Actions, profile.StartDelayMs, profile.SessionLimitMs);
            status = scheduler.getStatus(); publish();
        } catch (RuntimeException error) { stopWith(error.getMessage()); }
    }
    public void stopMacro() { removeCapture(); stopWith("Stopped. An in-progress gesture may finish within one second."); }
    private void stopWith(String message) {
        if (scheduler != null) scheduler.stop(message);
        status = message == null ? "Stopped" : message;
        publish();
    }
    public void hideController() {
        stopMacro(); removeCapture(); removeMarkers();
        if (controller != null) {
            try { windows.removeView(controller); } catch (IllegalArgumentException ignored) { }
            controller = null; controllerStatus = null; startButton = null; mapButton = null;
            addTapButton = null; addSwipeButton = null; removeButton = null; pointsButton = null;
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
        AccessibilityNodeInfo root = null;
        try {
            root = getRootInActiveWindow();
            if (root == null) return false;
            CharSequence packageName = root.getPackageName();
            if (packageName == null || !profile.TargetPackage.contentEquals(packageName)) return false;
            root.getBoundsInScreen(targetBounds);
            targetWindowId = root.getWindowId();
            return targetWindowId >= 0 && !targetBounds.isEmpty();
        } catch (RuntimeException unavailable) {
            targetWindowId = -1; targetBounds.setEmpty(); return false;
        } finally { if (root != null) root.recycle(); }
    }
    private boolean pathInsideTarget(float x, float y, float endX, float endY) {
        // The target bounds are a convex rectangle; both ends inside also contain
        // every point of the straight swipe between them.
        return targetBounds.contains((int)x, (int)y) && targetBounds.contains((int)endX, (int)endY);
    }
    private boolean controllerCoversPath(float x, float y, float endX, float endY) {
        if (controller == null || !controller.isShown()) return false;
        int[] position = new int[2]; controller.getLocationOnScreen(position);
        return segmentIntersectsRect(x, y, endX, endY,
            new Rect(position[0], position[1], position[0] + controller.getWidth(), position[1] + controller.getHeight()));
    }
    private static boolean segmentIntersectsRect(float x, float y, float endX, float endY, Rect rect) {
        return GestureGeometry.intersectsRect(x, y, endX, endY, rect.left, rect.top, rect.right, rect.bottom);
    }
    private boolean blockedByOverlay(float x, float y, float endX, float endY, int expectedWindowId) {
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
            if (!bounds.contains((int)x, (int)y) || !bounds.contains((int)endX, (int)endY)) return true;
            for (AccessibilityWindowInfo window : visible) {
                if (window.getId() == expectedWindowId) continue;
                // Greater layers are above the target; equal layers have no proven order.
                // Check every window type, including other apps and nonfocusable overlays.
                if (window.getLayer() < targetLayer) continue;
                window.getBoundsInScreen(bounds);
                if (segmentIntersectsRect(x, y, endX, endY, bounds)) return true;
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
        button.setTextColor(Color.rgb(237, 242, 255)); button.setBackgroundTintList(null); button.setBackground(background(Color.rgb(34, 47, 72), 9));
        button.setPadding(dp(4), 0, dp(4), 0); button.setSingleLine(true);
        button.setAutoSizeTextTypeUniformWithConfiguration(9, 12, 1, android.util.TypedValue.COMPLEX_UNIT_SP);
        button.setMinWidth(0); button.setMinimumWidth(0); button.setMinHeight(dp(48)); button.setMinimumHeight(dp(48));
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0, dp(48), 1); params.setMargins(dp(2), 0, dp(2), 0);
        button.setLayoutParams(params); button.setOnClickListener(listener); return button;
    }
    private void createController() {
        controller = new LinearLayout(this); controller.setOrientation(LinearLayout.VERTICAL); controller.setPadding(dp(8), dp(6), dp(8), dp(8));
        controller.setBackground(background(Color.rgb(14, 20, 34), 14)); controller.setElevation(dp(8));
        TextView drag = new TextView(this); drag.setText("Flow Macro  ·  drag to move"); drag.setTextSize(12); drag.setTextColor(Color.rgb(93, 222, 186)); drag.setPadding(dp(3), dp(4), dp(3), dp(6));
        controller.addView(drag);
        controllerStatus = new TextView(this); controllerStatus.setTextColor(Color.rgb(237, 242, 255)); controllerStatus.setTextSize(11); controllerStatus.setMaxLines(3); controllerStatus.setPadding(dp(3), 0, dp(3), dp(6)); controller.addView(controllerStatus);
        LinearLayout buttons = new LinearLayout(this); buttons.setOrientation(LinearLayout.HORIZONTAL);
        startButton = controlButton("Start", new View.OnClickListener() { public void onClick(View view) { startMacro(); } });
        Button stop = controlButton("Stop", new View.OnClickListener() { public void onClick(View view) { stopMacro(); } });
        stop.setTextColor(Color.rgb(14, 43, 38)); stop.setBackground(background(Color.rgb(93, 222, 186), 9));
        mapButton = controlButton("Map 1", new View.OnClickListener() { public void onClick(View view) { capturePoint(); } });
        Button close = controlButton("Hide", new View.OnClickListener() { public void onClick(View view) { hideController(); } });
        buttons.addView(startButton); buttons.addView(stop); buttons.addView(mapButton); buttons.addView(close); controller.addView(buttons);
        LinearLayout edits = new LinearLayout(this); edits.setOrientation(LinearLayout.HORIZONTAL); edits.setPadding(0, dp(5), 0, 0);
        addTapButton = controlButton("+Tap", new View.OnClickListener() { public void onClick(View view) { appendAction(false); } });
        addSwipeButton = controlButton("+Swipe", new View.OnClickListener() { public void onClick(View view) { appendAction(true); } });
        removeButton = controlButton("− Last", new View.OnClickListener() { public void onClick(View view) { removeLastAction(); } });
        pointsButton = controlButton("Points", new View.OnClickListener() { public void onClick(View view) { pointsVisible = !pointsVisible; updateMarkers(); publish(); } });
        addTapButton.setContentDescription("Add and map a tap action");
        addSwipeButton.setContentDescription("Add and map a swipe action");
        removeButton.setContentDescription("Remove the last action from this profile");
        edits.addView(addTapButton); edits.addView(addSwipeButton); edits.addView(removeButton); edits.addView(pointsButton); controller.addView(edits);
        controllerParams = new WindowManager.LayoutParams(Math.min(dp(312), Math.max(1, metrics().widthPixels - dp(16))), WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
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
                    try { windows.updateViewLayout(controller, controllerParams); } catch (IllegalArgumentException ignored) { }
                    return true;
                }
                return event.getActionMasked() == MotionEvent.ACTION_UP;
            }
        });
        try { windows.addView(controller, controllerParams); }
        catch (RuntimeException unavailable) {
            controller = null; controllerStatus = null; startButton = null; mapButton = null;
            addTapButton = null; addSwipeButton = null; removeButton = null; pointsButton = null;
            throw unavailable;
        }
        controller.post(new Runnable() { public void run() { clampController(); } });
    }
    private void clampController() {
        if (controller == null || controllerParams == null || windows == null) return;
        DisplayMetrics current = metrics();
        controllerParams.width = Math.min(dp(312), Math.max(1, current.widthPixels - dp(16)));
        controllerParams.x = Math.max(0, Math.min(Math.max(0, current.widthPixels - controller.getWidth()), controllerParams.x));
        controllerParams.y = Math.max(0, Math.min(Math.max(0, current.heightPixels - controller.getHeight()), controllerParams.y));
        try { windows.updateViewLayout(controller, controllerParams); } catch (IllegalArgumentException ignored) { }
    }
    private void appendAction(boolean swipe) {
        if (isRunning() || capture != null || pendingMapping) return;
        try {
            profile = ProfileStore.loadCurrent(this);
            if (!targetReady(false)) throw new IllegalArgumentException("Focus the selected target app before adding a point.");
            if (profile.Actions.size() >= 32) throw new IllegalArgumentException("Use at most 32 touch actions.");
            TapAction action = new TapAction();
            action.Kind = swipe ? "Swipe" : "Tap";
            action.Label = (swipe ? "Swipe " : "Tap ") + (profile.Actions.size() + 1);
            action.IntervalMs = swipe ? 2000 : 1000;
            action.HoldMs = swipe ? 300 : 40;
            action.clearMapping();
            profile.Actions.add(action);
            mapIndex = profile.Actions.size() - 1;
            ProfileStore.save(this, profile);
            capturePoint();
        } catch (RuntimeException error) { removeCapture(); status = error.getMessage(); publish(); }
    }
    private void removeLastAction() {
        if (isRunning() || capture != null || pendingMapping) return;
        try {
            profile = ProfileStore.loadCurrent(this);
            if (profile.Actions.isEmpty()) throw new IllegalArgumentException("No action to remove.");
            TapAction removed = profile.Actions.remove(profile.Actions.size() - 1);
            mapIndex = Math.max(0, Math.min(mapIndex, profile.Actions.size() - 1));
            ProfileStore.save(this, profile);
            status = "Removed “" + removed.Label + "”. Your other points are kept.";
            updateMarkers(); publish();
        } catch (RuntimeException error) { status = error.getMessage(); publish(); }
    }
    private void removeMarkers() {
        View previous = markers; markers = null;
        if (previous != null) try { windows.removeView(previous); } catch (IllegalArgumentException ignored) { }
    }
    private void updateMarkers() {
        if (!pointsVisible || controller == null || profile == null || capture != null || pendingMapping || isRunning() || !targetReady(false)) { removeMarkers(); return; }
        DisplayMetrics current = metrics();
        if (profile.ScreenWidth != current.widthPixels || profile.ScreenHeight != current.heightPixels) { removeMarkers(); return; }
        boolean any = false;
        for (TapAction action : profile.Actions) if (action.Enabled && action.hasStartPoint()) { any = true; break; }
        if (!any) { removeMarkers(); return; }
        if (markers == null) {
            markers = new PointMarkers();
            WindowManager.LayoutParams params = new WindowManager.LayoutParams(WindowManager.LayoutParams.MATCH_PARENT, WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY, WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE |
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS, PixelFormat.TRANSLUCENT);
            params.gravity = Gravity.TOP | Gravity.LEFT;
            try { windows.addView(markers, params); } catch (RuntimeException unavailable) { markers = null; }
        }
        if (markers != null) markers.invalidate();
    }
    private final class PointMarkers extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        PointMarkers() { super(MacroAccessibilityService.this); setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO); }
        @Override protected void onDraw(Canvas canvas) {
            super.onDraw(canvas);
            if (profile == null) return;
            int[] origin = new int[2]; getLocationOnScreen(origin);
            DisplayMetrics current = metrics();
            for (int index = 0; index < profile.Actions.size(); index++) {
                TapAction action = profile.Actions.get(index);
                if (!action.Enabled || !action.hasStartPoint()) continue;
                float x = (float)(action.X * (current.widthPixels - 1)) - origin[0];
                float y = (float)(action.Y * (current.heightPixels - 1)) - origin[1];
                if (action.isSwipe() && action.hasEndPoint()) {
                    float endX = (float)(action.EndX * (current.widthPixels - 1)) - origin[0];
                    float endY = (float)(action.EndY * (current.heightPixels - 1)) - origin[1];
                    paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(dp(3)); paint.setColor(Color.rgb(137, 145, 255));
                    canvas.drawLine(x, y, endX, endY, paint);
                    circle(canvas, endX, endY, (index + 1) + "B", Color.rgb(137, 145, 255));
                }
                circle(canvas, x, y, (index + 1) + (action.isSwipe() ? "A" : ""), Color.rgb(93, 222, 186));
            }
        }
        private void circle(Canvas canvas, float x, float y, String label, int color) {
            paint.setStyle(Paint.Style.FILL); paint.setColor(Color.argb(230, 14, 20, 34)); canvas.drawCircle(x, y, dp(17), paint);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(dp(2)); paint.setColor(color); canvas.drawCircle(x, y, dp(17), paint);
            paint.setStyle(Paint.Style.FILL); paint.setColor(Color.WHITE); paint.setTextSize(dp(12)); paint.setTextAlign(Paint.Align.CENTER);
            canvas.drawText(label, x, y - (paint.ascent() + paint.descent()) / 2, paint);
        }
    }
    private void capturePoint() {
        stopMacro();
        try {
            profile = ProfileStore.loadCurrent(this);
            if (profile.Actions.isEmpty()) throw new IllegalArgumentException("Add an action in the editor first.");
            if (!targetReady(false)) throw new IllegalArgumentException("Focus the selected target app before mapping.");
            if (mapIndex >= profile.Actions.size()) mapIndex = 0;
            final int selected = mapIndex;
            final boolean swipe = profile.Actions.get(selected).isSwipe();
            final int expectedWindow = targetWindowId;
            final DisplayMetrics dimensions = metrics();
            final int rotation = windows.getDefaultDisplay().getRotation();
            removeCapture(); removeMarkers();
            final int activeGeneration = captureGeneration;
            final float[] trace = new float[] { Float.NaN, Float.NaN, Float.NaN, Float.NaN };
            FrameLayout shade = new FrameLayout(this); shade.setBackgroundColor(Color.argb(70, 0, 0, 0));
            final View preview = new View(this) {
                private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
                @Override protected void onDraw(Canvas canvas) {
                    super.onDraw(canvas);
                    if (Float.isNaN(trace[0])) return;
                    int[] origin = new int[2]; getLocationOnScreen(origin);
                    float x = trace[0] - origin[0], y = trace[1] - origin[1];
                    float endX = trace[2] - origin[0], endY = trace[3] - origin[1];
                    paint.setColor(Color.rgb(93, 222, 186)); paint.setStrokeWidth(dp(4)); paint.setStyle(Paint.Style.STROKE);
                    if (swipe) canvas.drawLine(x, y, endX, endY, paint);
                    canvas.drawCircle(x, y, dp(18), paint);
                    if (swipe) canvas.drawCircle(endX, endY, dp(18), paint);
                }
            };
            shade.addView(preview, new FrameLayout.LayoutParams(-1, -1));
            LinearLayout guide = new LinearLayout(this); guide.setOrientation(LinearLayout.VERTICAL); guide.setPadding(dp(14), dp(12), dp(14), dp(12));
            guide.setBackground(background(Color.argb(230, 14, 20, 34), 12));
            TextView instructions = new TextView(this);
            instructions.setText("Map " + (selected + 1) + " · " + profile.Actions.get(selected).Label + "\n" +
                (swipe ? "Swipe once from its start to its end." : "Touch its point once.") + "\nThis gesture is captured; it does not reach the target.");
            instructions.setTextColor(Color.WHITE); instructions.setTextSize(15); guide.addView(instructions);
            Button cancel = new Button(this); cancel.setText("Cancel mapping"); cancel.setAllCaps(false);
            cancel.setTextColor(Color.rgb(14, 43, 38)); cancel.setBackground(background(Color.rgb(93, 222, 186), 10));
            LinearLayout.LayoutParams cancelParams = new LinearLayout.LayoutParams(-1, dp(44)); cancelParams.topMargin = dp(8);
            guide.addView(cancel, cancelParams);
            FrameLayout.LayoutParams guideParams = new FrameLayout.LayoutParams(Math.min(dp(320), dimensions.widthPixels - dp(32)), -2, Gravity.TOP | Gravity.CENTER_HORIZONTAL);
            guideParams.topMargin = dp(24); shade.addView(guide, guideParams);
            cancel.setOnClickListener(new View.OnClickListener() { public void onClick(View view) { removeCapture(); status = "Mapping cancelled."; publish(); } });
            shade.setOnTouchListener(new View.OnTouchListener() {
                boolean started, multiple;
                public boolean onTouch(View view, MotionEvent event) {
                    if (activeGeneration != captureGeneration) return true;
                    if (event.getActionMasked() == MotionEvent.ACTION_DOWN) {
                        started = true; multiple = false;
                        trace[0] = trace[2] = event.getRawX(); trace[1] = trace[3] = event.getRawY(); preview.invalidate();
                    } else if (event.getActionMasked() == MotionEvent.ACTION_POINTER_DOWN) multiple = true;
                    else if (event.getActionMasked() == MotionEvent.ACTION_MOVE && started) {
                        if (event.getPointerCount() != 1) multiple = true;
                        trace[2] = event.getRawX(); trace[3] = event.getRawY(); preview.invalidate();
                    } else if (event.getActionMasked() == MotionEvent.ACTION_CANCEL) {
                        removeCapture(); status = "Mapping cancelled. No points changed."; publish();
                    }
                    if (event.getActionMasked() == MotionEvent.ACTION_UP) {
                        final float x = swipe ? trace[0] : event.getRawX(), y = swipe ? trace[1] : event.getRawY();
                        final float endX = swipe ? event.getRawX() : x, endY = swipe ? event.getRawY() : y;
                        boolean validTouch = started && !multiple;
                        if (swipe && Math.hypot(endX - x, endY - y) < dp(8)) validTouch = false;
                        if (!validTouch) { removeCapture(); status = swipe ? "Mapping cancelled. Swipe from a start point to a different end point." : "Mapping cancelled. Use one finger to choose a point."; publish(); return true; }
                        pendingMapping = true;
                        removeCapture(true);
                        final int finishedGeneration = captureGeneration;
                        handler.post(new Runnable() { public void run() {
                            if (finishedGeneration != captureGeneration) return;
                            try {
                            DisplayMetrics now = metrics();
                            if (now.widthPixels != dimensions.widthPixels || now.heightPixels != dimensions.heightPixels || windows.getDefaultDisplay().getRotation() != rotation || !targetReady(false) || targetWindowId != expectedWindow) {
                                status = "Mapping cancelled: target or screen changed."; publish(); return;
                            }
                            if (!pathInsideTarget(x, y, endX, endY) || controllerCoversPath(x, y, endX, endY) || blockedByOverlay(x, y, endX, endY, expectedWindow)) {
                                status = "Map inside the selected app, away from keyboards, dialogs and floating controls."; publish(); return;
                            }
                            if (profile.ScreenWidth != dimensions.widthPixels || profile.ScreenHeight != dimensions.heightPixels) {
                                for (TapAction action : profile.Actions) action.clearMapping();
                            }
                            profile.ScreenWidth = dimensions.widthPixels; profile.ScreenHeight = dimensions.heightPixels;
                            TapAction action = profile.Actions.get(selected);
                            action.X = Math.max(0, Math.min(1, x / Math.max(1, dimensions.widthPixels - 1)));
                            action.Y = Math.max(0, Math.min(1, y / Math.max(1, dimensions.heightPixels - 1)));
                            if (swipe) {
                                action.EndX = Math.max(0, Math.min(1, endX / Math.max(1, dimensions.widthPixels - 1)));
                                action.EndY = Math.max(0, Math.min(1, endY / Math.max(1, dimensions.heightPixels - 1)));
                            }
                            try { ProfileStore.save(MacroAccessibilityService.this, profile); }
                            catch (RuntimeException error) { status = "Could not save mapping: " + error.getMessage(); publish(); return; }
                            mapIndex = (selected + 1) % profile.Actions.size();
                            for (int index = 0; index < profile.Actions.size(); index++) {
                                if (profile.Actions.get(index).Enabled && !profile.Actions.get(index).isMapped()) { mapIndex = index; break; }
                            }
                            status = "Mapped “" + action.Label + "”. Add another point or Start.";
                            } catch (RuntimeException unavailable) {
                                status = "Could not finish mapping: " + (unavailable.getMessage() == null ? "check the target and try again." : unavailable.getMessage());
                            } finally {
                                if (finishedGeneration == captureGeneration) { pendingMapping = false; updateMarkers(); publish(); }
                            }
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
            status = "Mapping “" + profile.Actions.get(selected).Label + "”: " + (swipe ? "swipe from start to end." : "touch its point once."); publish();
        } catch (RuntimeException error) { removeCapture(); status = error.getMessage(); publish(); }
    }
    private void removeCapture() {
        removeCapture(false);
    }
    private void removeCapture(boolean keepPending) {
        if (!keepPending) pendingMapping = false;
        captureGeneration++;
        View previous = capture; capture = null;
        if (previous != null) try { windows.removeView(previous); } catch (IllegalArgumentException ignored) { }
    }
    private void publish() {
        int count = profile == null ? 0 : profile.Actions.size();
        String key = status + "|" + getTapCount() + "|" + isRunning() + "|" + count + "|" + mapIndex + "|" + (capture != null) + "|" + pendingMapping + "|" + pointsVisible;
        if (key.equals(lastBroadcast)) return;
        if (controllerStatus != null) controllerStatus.setText(count + " actions  ·  " + getTapCount() + " gestures sent\n" + (status == null ? "Stopped" : status));
        boolean editing = !isRunning() && capture == null && !pendingMapping;
        if (startButton != null) startButton.setEnabled(editing && count > 0);
        if (mapButton != null) { mapButton.setEnabled(editing && count > 0); mapButton.setText("Map" + (mapIndex + 1)); }
        if (addTapButton != null) addTapButton.setEnabled(editing && count < 32);
        if (addSwipeButton != null) addSwipeButton.setEnabled(editing && count < 32);
        if (removeButton != null) removeButton.setEnabled(editing && count > 0);
        if (pointsButton != null) { pointsButton.setEnabled(editing); pointsButton.setTextColor(pointsVisible ? Color.rgb(93, 222, 186) : Color.rgb(237, 242, 255)); pointsButton.setContentDescription(pointsVisible ? "Hide numbered point markers" : "Show numbered point markers"); }
        lastBroadcast = key;
        Intent update = new Intent(ACTION_STATUS).setPackage(getPackageName());
        update.putExtra("status", status); update.putExtra("count", getTapCount()); update.putExtra("running", isRunning());
        sendBroadcast(update);
    }
}
