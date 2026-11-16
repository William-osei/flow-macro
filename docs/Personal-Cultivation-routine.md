# Personal Cultivation Unbound routine

This optional example preserves the original user's setup. General users can use Mouse clicker, Routines or an empty recording.

## Load the saved preset

Open **Profiles → Included presets**, select **Cultivation Unbound - C then V**, then **Add to library**. Select it in the saved library and choose **Load selected**. Then choose the Roblox target, open Record & replay and use Play/F9. Infinite looping is enabled; adding or loading leaves playback stopped. Existing saved copies remain available through Refresh library.

The portable backup is `presets/Cultivation-Unbound-C-then-V.json`. Import it through Profiles on another installation. Z and X are included as disabled optional timer rows.

## C/V with an independent Z timer

Use **Cultivation Unbound - C V + Z 24s** with Flow Macro 1.4.1 or newer. In version 1.5, find it under **Profiles → Included presets → Add to library**, then select it in the saved library and choose **Load selected**. Choose Roblox, switch to **Routines**, then Start/F9. This mode runs all enabled timer rows independently. The recording stored in this profile is still the original C/V-only recording.

C and V retain their saved timings: C every 12.3 seconds with a 500 ms hold, V every 12.3 seconds with a 1.8-second initial delay and a 500 ms hold. Z first presses 2.6 seconds after the routine becomes active, then every 24 seconds, with a 60 ms hold. X stays disabled and the session limit is unlimited.

Z's Keep CD checkbox is enabled. Returning after a focus or modifier pause keeps the deadline from the last accepted Z press; an overdue timer fires once instead of catching up. Explicit Stop/Start begins a new timer session, so start when Z is ready. The macro does not read the game's cooldown display or confirm that an accepted key cast a skill.

Z sends only its key and leaves the mouse position alone. Aim the cursor where the hand strike should land. The updated portable profile is `presets/Cultivation-Unbound-CV-Z24.json`. The earlier C/V-only profile remains available.

C fires a straight-line ki attack, V throws a sword, Z drops a big hand at the cursor with a 24-second cooldown, and X heals, according to the user's description. Line up two mobs before C, then allow the attack to finish before V. The game's auto-quest feature handles quest restarts.

The C-then-V example uses a 500 ms C hold, a 1300 ms wait after release, then a 500 ms V hold, with a 10000 ms loop gap. These are editable starting values, not measured optimal cooldowns. Z and X are optional. Healing is timed, and the macro does not find mobs, aim or navigate from the screen.

In the saved named preset, timer rows match the ordered replay: V starts 1800 ms after C, and each cycle lasts 12300 ms. Record & replay is the recommended mode when C must always precede V.

The original input failure was a privilege mismatch: Roblox ran at High integrity and the macro at Medium integrity. The user confirmed V worked after both ran as administrator. C's earlier cooldown observation did not establish input attribution. A complete optimized farming cycle and silver rate remain unmeasured.

Keep the selected window focused, stop before chat or menus, and use F10 to stop. F9 also ends an active session; the next Start resets its cooldown history. Check the exact shortcuts shown if another program reserves those keys.
