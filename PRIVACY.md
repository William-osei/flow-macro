# Flow Macro privacy policy

Effective 16 November 2026. This policy describes the Windows app and Android 1.6 development build published by [William-osei](https://github.com/William-osei/flow-macro).

## Information kept on your device

Flow Macro stores routine names, selected input keys or app package, click/touch points, gesture endpoints and timing settings locally. Windows recordings contain the keys, holds and click points you explicitly record. Android stores the gestures you configure; it does not record arbitrary screen interaction. Start recording or playback only when you intend to use it.

The Windows app checks the selected window and its focus and permission level. Android's optional AccessibilityService checks the active app package, window identity, bounds and layers to decide whether your configured gesture can run. It can perform taps and swipes after you enable the service and explicitly start a routine. It does not inspect or save screen text or take screenshots. Android may describe broader system capabilities in its accessibility warning.

## Network, sharing and payments

The current apps do not send your profiles or input data to a server. The Android build has no Internet permission. There are no accounts, advertising SDKs, analytics, billing or subscriptions. Neither app automatically uploads personal presets. Importing and exporting a profile uses a location you choose; anyone you share that file with can see its configured routine.

GitHub downloads, issue reports and Android/Windows system services have their own privacy practices. Information you post in a public GitHub issue is public. Review an exported file or diagnostic report before sharing it.

## Your controls

Windows settings are stored under `%LOCALAPPDATA%\FarmMacro`. Close the app before removing files from that folder. Personal portable backups remain wherever you saved them. Android profiles are in app-private storage; clearing the app's storage or uninstalling removes that local storage. Exported files remain until you delete them yourself.

Use Stop to prevent new input. An Android gesture already dispatched may take up to one second to finish. Disable Flow Macro in Android Accessibility settings to revoke gesture access. Loading or importing a profile does not start a routine.

## Changes and contact

Any future billing, advertising, cloud sync or analytics release will need an updated policy and accurate disclosures before it is offered. Questions can be raised through the [project's issues page](https://github.com/William-osei/flow-macro/issues); do not include passwords, payment details or sensitive recordings there.
