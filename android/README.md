# Flow Macro for Android — 1.4.0 test build

Flow Macro is a general tap automation tool for Android. Choose an app, map **tap points on its screen**, and set an independent interval, hold duration, burst and initial delay for each action. It performs touchscreen gestures rather than desktop keyboard events.

The app includes saved profiles, JSON import/export, a numbered tap test pad, and floating Start/Stop/Map controls. Set the session limit to zero to run indefinitely. A new setup starts with **My tap routine**, one **Tap 1** action every 1000 ms with a 40 ms hold, and the local test pad as its target. New tap points must be mapped before playback.

## Install and set up

1. Copy `dist/FlowMacro-Android.apk` to your phone and open it there. Android 8 or newer is required. This is a locally signed test APK, not a Google Play release.
2. Review Android's installation prompts yourself. Open Flow Macro, read its accessibility disclosure, and use its settings button if you want to enable the service. The app cannot enable accessibility or approve security notices for you. Android 13 and newer may restrict accessibility access for sideloaded apps; review the system's notice rather than assuming every device permits this build.
3. Start with the **Tap pad**. Choose your routine and show the floating controls. In the pad, use **Map** and touch the desired point once for each enabled action. Mapping consumes that touch; it does not send it through to the target.
4. Move the floating controls away from the tap points. Use a short timed test before using **Run indefinitely**. Tap **Start** on the floating bar only while the selected target is in front.
5. To use another app, select it from the installed-app list, show the floating controls and map points in that app. Check that the expected controls are visible and the screen layout is stable before starting. Each profile keeps its own target and timing settings.

**Stop prevents subsequent gestures. An already dispatched tap can finish and release within one second.** Physical interaction can also cause Android to cancel an injected gesture; the routine then stops. Hiding the floating bar stops the routine.

## Focus and calibration

The service checks the active app package, window bounds, screen state and calibration before dispatching taps. It also requires the exact target window to appear in Android's interactive-window list and rejects a tap point covered by another window above it (or with an ambiguous equal layer), including keyboards, dialogs and other overlays. An unavailable window list blocks playback. It stops when the target changes, the editor opens, the device locks, the screen turns off, Android cancels a gesture, or the display size/orientation changes. It requires an explicit Start afterward.

This does not promise background operation. Android gestures affect the shared touchscreen and are not addressed to a specific app window. Window checks are snapshots, so Android cannot make the check and gesture delivery atomic; a window can change between them. Keep the chosen target in front, close keyboards/dialogs, and avoid using other apps while it runs. Window reporting and overlay behavior still require testing on an actual device.

Tap points are normalized coordinates with the screen dimensions recorded during mapping. Re-map after rotating or changing the display layout. Profile imports deliberately clear point calibration so a routine from another phone or Windows cannot immediately tap unrelated locations.

## Profiles and privacy

Profiles stay in app-private storage unless you explicitly export them through Android's file picker. Mobile JSON uses `Format: "FarmMacro.Mobile"`, `Version: 1`. Normal Windows keyboard/sequence profile files use another format; Windows-generated mobile drafts require fresh mapping on the phone. Importing does not start playback and avoids overwriting an existing name.

Version 1.4 keeps the same Android package, local profile storage, mobile schema and development signing key. Updating the existing app preserves named profiles, including older C/V routines and their chosen targets. The generic Tap 1 setup is only the default for a new setup; it does not replace saved routines. The retained `FarmMacro.Mobile` format name is a compatibility identifier.

The accessibility service can perform gestures and retrieve active-window metadata to check the selected package and bounds. It does not read or save screen text, take screenshots, or send data over a network. The APK does not request Internet, broad storage, or draw-over-other-apps permission; its floating bar uses the user-enabled accessibility overlay capability. Android may still display broad accessibility warnings for that capability.

The app repeats the points and timings you configure. It does not inspect screen content, find controls, or adapt to moving layouts. Input acceptance in other apps, manufacturer behavior, Android permission setup and physical-device display layouts remain unverified until tested on a phone.

## Build on Windows

From the repository root:

```powershell
.\scripts\PrepareAndroidTools.ps1
.\scripts\BuildAndroid.ps1
```

The preparation script downloads official Android SDK 36/build tools 36 and Microsoft OpenJDK 17 into the ignored `build/android-tools` folder. The build uses `aapt2`, `javac`, D8, `zipalign` and `apksigner`, runs the pure-Java scheduler tests, and verifies the signed output.

Output: `android/dist/FlowMacro-Android.apk`, a SHA-256 file, and this guide. The local development signing key stays in `build/android-tools/farmmacro-development.keystore`; retain it for installable updates to this test app. Its legacy filename and certificate name are retained for compatibility. A store release needs a separate release-signing and policy review.

The older download name, `FarmMacro-Android.apk`, is an identical copy of the current build so existing links still deliver version 1.4. Both APK filenames have the same SHA-256 hash and install the same app.

Automated host tests verify scheduler behavior and APK tooling verifies packaging/signatures. These checks do not replace physical-device testing or confirm that another app accepts the gestures.
