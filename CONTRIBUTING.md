# Development guide

Flow Macro's Windows interface uses C# 5 and .NET Framework WinForms. Android is a native Java preview. Keep existing profile formats and local storage identifiers compatible when changing public branding or adding settings.

## Windows

Use Windows PowerShell from the repository root:

```powershell
.\test.ps1
.\build.ps1
$preview = Start-Process .\FarmMacro.exe -ArgumentList '--preview', 'build/previews' -Wait -PassThru
if ($preview.ExitCode -ne 0) { throw 'Preview checks failed.' }
```

The scripts use the .NET Framework compiler included with Windows and require no NuGet restore. An alternative output is supported with `./build.ps1 -OutputPath ./build/FarmMacro.next.exe`; it avoids replacing a running executable.

The test suite exercises scheduler limits, input release, focus pauses, recordings, profiles, portable transfers, keyboard packets, mobile export and permission checks. Preview mode renders the app offscreen without registering hotkeys or sending input. Inspect the resulting PNGs at the minimum laptop size; UI changes also need a display-scaling review. Automated rendering does not prove that another app accepts input.

Use `UiTheme.cs` for navy/mint colors, typography and shared controls. Keep primary actions visible, units explicit and small layouts readable. Loading or importing must remain stopped. Preserve F10 emergency release, focus guards and infinite-loop options.

## Android

Full APK builds run on Windows:

```powershell
.\scripts\PrepareAndroidTools.ps1
.\scripts\TestAndroid.ps1
.\scripts\BuildAndroid.ps1
```

Preparation downloads project-local tools into ignored `build/android-tools`. The APK build runs gesture/profile/scheduler tests, compiles resources/Java/DEX, aligns the archive, and verifies its development signature. JVM codec tests download pinned `org.json:json:20260814` from Maven Central and verify SHA-256; the jar stays under ignored `build/test-deps` and is never an APK dependency. Keep the local development keystore private and retain it when updating a test installation. A fresh checkout creates its own key, so its APK will not update an installation signed by another developer's key.

For only the platform-independent scheduler tests, use JDK 17 on any host:

```sh
mkdir -p build/android-tests
javac -encoding UTF-8 -d build/android-tests \
  android/app/src/main/java/dev/farmmacro/app/TapAction.java \
  android/app/src/main/java/dev/farmmacro/app/TapScheduler.java \
  android/tests/TapSchedulerTest.java
java -cp build/android-tests TapSchedulerTest
```

Android's accessibility, window reporting, floating controls and mapping need physical-device verification. Host tests and a valid APK signature do not establish OEM compatibility or target-app gesture acceptance.

## Releases and changes

After building both platforms, run `./scripts/PackageWindows.ps1` to prepare the Windows ZIP and SHA-256 file. The public preset allowlist is `presets/catalog.txt`; `personal-presets/` stays ignored and unbundled. Publish generated downloads through GitHub Releases. Keep executables, APKs, archives, local settings, SDK/JDK caches and signing keys out of Git.

For a contribution, describe the user-visible behavior, relevant checks, and anything requiring a live app or phone test. Add focused tests for engine or persistence behavior; avoid tests that only repeat a layout implementation. New settings must round-trip through the editor, saved profiles and portable import/export.
