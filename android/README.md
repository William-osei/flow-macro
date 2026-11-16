# Flow Macro for Android — 1.6.0 test build

Flow Macro is a general touch automation tool for Android. Choose an app, map **tap points or straight swipe paths**, and set an independent interval, hold/duration, repeat count and initial delay for each action. A maximum of 32 actions is supported. It performs touchscreen gestures rather than desktop keyboard events.

The app includes saved profiles, JSON import/export, a numbered tap test pad, and floating Start/Stop/Map controls. Set the session limit to zero to run indefinitely. A new setup starts with **My tap routine**, one **Tap 1** action every 1000 ms with a 40 ms hold, and the local test pad as its target. New tap points must be mapped before playback.

## Install and set up

1. Copy `dist/FlowMacro-Android.apk` to your phone and open it there. Android 8 or newer is required. This is a locally signed test APK, not a Google Play release.
2. Review Android's installation prompts yourself. Open Flow Macro, read its accessibility disclosure, and use its settings button if you want to enable the service. The app cannot enable accessibility or approve security notices for you. Android 13 and newer may restrict accessibility access for sideloaded apps; review the system's notice rather than assuming every device permits this build.
3. Start with the **Tap pad**. Choose your routine and show the floating controls. In the pad, use **Map** and touch a tap point or drag from a swipe's start to its end. Mapping consumes the touch; it does not send it through to the target. Playback uses the straight line between a swipe's endpoints.
4. Move the floating controls away from the tap points. Use a short timed test before using **Run indefinitely**. Tap **Start** on the floating bar only while the selected target is in front.
5. To use another app, select it from the installed-app list, show the floating controls and map points in that app. Check that the expected controls are visible and the screen layout is stable before starting. Each profile keeps its own target and timing settings.

While stopped, **+ Tap** and **+ Swipe** add and map actions from the floating controller. **− Last** removes the last action. **Points** toggles passive numbered markers; swipe markers show a start and end. Use Map to reposition a point. Markers disappear before playback. Drag the controller by its heading to move it away from your gesture paths. Edits save a local draft and never start playback.

The editor supports Tap/Swipe type, names, enabled state and separate timing for each action. Hold or swipe duration is 10–1000 ms. Swipes require two different points; switching type clears mapping. Gestures serialize through completion callbacks and do not run simultaneously.

**Stop prevents subsequent gestures. An already dispatched gesture can finish and release within one second.** Physical interaction can also cause Android to cancel an injected gesture; the routine then stops. Hiding the floating bar stops the routine. The test pad reports received taps and swipes for delivery checks.

## Focus and calibration

The service checks the active app package, window bounds, screen state and calibration before dispatching gestures. It requires the exact target window in Android's interactive-window list and rejects any tap point or part of a straight swipe path covered by the controller or another window above it (or with an ambiguous equal layer), including keyboards, dialogs and overlays. Missing window data blocks playback. Target changes, opening the editor, locking, screen-off, gesture cancellation or display/orientation changes stop the routine. A new Start is required.

This does not promise background operation. Android gestures affect the shared touchscreen and are not addressed to a specific app window. Window checks are snapshots, so Android cannot make the check and gesture delivery atomic; a window can change between them. Keep the chosen target in front, close keyboards/dialogs, and avoid using other apps while it runs. Window reporting and overlay behavior still require testing on an actual device.

Points are normalized coordinates with screen dimensions recorded during mapping. Re-map after rotating or changing the display layout. Imports clear all tap points and both swipe endpoints, including calibration, so a routine cannot immediately gesture at unrelated positions on another phone.

## Profiles and privacy

Profiles stay in app-private storage unless you export them through Android's file picker. Mobile JSON retains `Format: "FarmMacro.Mobile"`. Tap-only profiles use **Version 1**; profiles containing any swipe use **Version 2**, which older builds reject rather than silently turning swipes into taps. New builds read both. Windows mobile exports remain version-1 tap drafts with fresh mapping required. Desktop recordings are not converted to mobile gestures. Importing stays stopped and avoids overwriting an existing name.

Updates keep the Android package, local profile storage and development signing key. Existing named tap profiles remain usable. The generic Tap 1 setup is only a fresh-install default. Public examples are general-purpose; personal game presets are not distributed. The retained `FarmMacro.Mobile` name is a compatibility identifier.

The accessibility service can perform gestures and retrieve active-window metadata to check the selected package and bounds. It does not read or save screen text, take screenshots, or send data over a network. The APK does not request Internet, broad storage, or draw-over-other-apps permission; its floating bar uses the user-enabled accessibility overlay capability. Android may still display broad accessibility warnings for that capability.

The app repeats the points and timings you configure. It does not inspect screen content, find controls, or adapt to moving layouts. Input acceptance, manufacturer behavior, Android permissions, markers and physical-device layouts remain unverified until tested on a phone. See the [privacy policy](../PRIVACY.md).

## Build on Windows

From the repository root:

```powershell
.\scripts\PrepareAndroidTools.ps1
.\scripts\TestAndroid.ps1
.\scripts\BuildAndroid.ps1
```

Preparation downloads official Android SDK 36/build tools 36 and Microsoft OpenJDK 17 into ignored `build/android-tools`. JVM tests use a pinned, checksum-verified `org.json` jar in ignored `build/test-deps`; it is test-only, and the APK uses Android's JSON API. The build runs gesture/profile/scheduler tests, then uses `aapt2`, `javac`, D8, `zipalign` and `apksigner` to compile and verify the APK.

Output: `android/dist/FlowMacro-Android.apk`, a SHA-256 file, and this guide. The local development signing key stays in `build/android-tools/farmmacro-development.keystore`; retain it for installable updates to this test app. Its legacy filename and certificate name are retained for compatibility. A store release needs a separate release-signing and policy review.

The older download name, `FarmMacro-Android.apk`, is an identical copy of the current build. Both APK filenames have the same SHA-256 hash and install the same app.

Host tests cover action validation, timing, callbacks, profile compatibility and path geometry; APK tools verify compilation, packaging and signatures. These checks do not replace phone testing or confirm that another app accepts gestures.

The development build is free with no billing, ads or subscriptions. The [publishing and income plan](../docs/PLAY_STORE_PLAN.md) covers Google Play preparation and a possible future one-time Pro upgrade. AAB signing, account setup, store submission and real purchases remain future work.
