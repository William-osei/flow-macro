# Flow Macro: Android publishing and income plan

Research checked **16 November 2026** against official Google documentation. This is a preparation plan; the app has not been submitted to Google Play, and this document does not establish approval or expected income. Recheck the linked requirements before submission.

## Recommended first business model

Start with a useful free version and test demand before charging. A future **one-time Pro upgrade** could unlock convenience features such as larger routine libraries, advanced scheduling and batch profile tools. Keep Stop, permission explanations and basic setup available to everyone. This is a product recommendation, not a feature or price commitment.

A permanent Pro unlock fits Google's **non-consumable one-time product** model. A subscription is better reserved for a service with continuing value and costs, such as optional cloud sync, rather than only local tapping. Google documents permanent upgrades as a one-time-product use case. [One-time products](https://developer.android.com/google/play/billing/one-time-products)

There is currently no checkout, paid entitlement, advertising SDK or subscription in this project. Add payment only after phone testing and a clear paid feature set.

## Account and Ghana eligibility

Google currently lists **Ghana** as supporting both developer registration and merchant registration, with **USD** as the developer default currency. This supports a possible paid release; it does not confirm a particular person's account, bank or identity verification. [Supported locations](https://support.google.com/googleplay/android-developer/answer/9306917?hl=en)

Opening Play Console requires an adult account owner, a **US$25 one-time registration fee**, acceptance of Google's agreement and identity verification. Google may request government ID and a payment card matching the legal name. Choose the correct personal or organization account type and complete the steps yourself. [Get started with Play Console](https://support.google.com/googleplay/android-developer/answer/6112435?hl=en)

New personal accounts must verify access to a real, non-rooted Android device running Android 10 or newer through the Play Console mobile app. This is separate from the app's Android 8 minimum. [Device verification](https://support.google.com/googleplay/android-developer/answer/14316361?hl=en)

For personal accounts created after **13 November 2023**, production access requires a closed test with at least **12 testers opted in continuously for 14 days**, followed by a production-access application. Test participation and feedback should reflect real use; reaching the count and elapsed time does not itself publish the app. [Personal-account testing](https://support.google.com/googleplay/android-developer/answer/14151465?hl=en)

For Ghana merchants, Google's bank guide says payouts arrive in **USD by wire transfer from the United States**. Confirm the receiving-bank and intermediary details and charges with the bank before relying on small sales. This is different from payment methods customers use to buy apps. [Ghana merchant bank details](https://support.google.com/googleplay/android-developer/answer/7161440?hl=en)

Wire fees can materially reduce small payouts. Google recommends considering a higher payout threshold for wire transfers, but the useful threshold depends on the bank's actual charges. [Payout thresholds](https://support.google.com/paymentscenter/answer/7163445?hl=en)

## Accessibility review is a release requirement

Flow Macro is a user-configured automation utility, so it should retain **`isAccessibilityTool="false"`**. Google allows narrow, deterministic automation following a static human-defined script; it prohibits using this API for an app to autonomously initiate, plan and execute decisions. Keep actions explicitly selected and started by the user. [Accessibility API policy](https://support.google.com/googleplay/android-developer/answer/10964491?hl=en)

Before a Play submission, provide an in-app, separate disclosure during normal setup describing the accessibility data accessed, how it is used or shared, and why gestures are needed. Obtain affirmative consent before taking the user to enable the service. Document the API in the store listing, complete the Accessibility declaration, and provide the required review video demonstrating consent, refusal and a core feature. A system permission screen or privacy-policy link alone is insufficient. Google makes the final review decision. [Accessibility disclosure and declaration](https://support.google.com/googleplay/android-developer/answer/10964491?hl=en)

## Store build and privacy work

| Item | Preparation needed |
| --- | --- |
| Phone quality | Test multiple Android versions and screen sizes; verify mapping, swipe coverage, controller movement, Stop, focus changes, locking and rotation on physical phones. |
| Target SDK | Phone apps submitted now must target **Android 16 / API 36 or higher**. The project already targets 36; test the corresponding behavior changes. [Target API requirements](https://support.google.com/googleplay/android-developer/answer/11926878?hl=en) |
| Upload format | Create a signed **Android App Bundle (AAB)**. The sideload test APK is not the required new-app publishing format. [Android App Bundles](https://developer.android.com/guide/app-bundle) |
| Signing | Adopt Play App Signing and a securely managed upload-key process. Resolve package ownership and continuity with existing test installations before the first store release; retain private keys outside Git. [Play App Signing FAQ](https://developer.android.com/guide/app-bundle/faq) |
| Privacy policy | Publish an app-specific HTTPS policy on a public webpage and link it in both the app and store listing, even if no personal data is collected. Finish a real support/contact channel. [Prepare for review](https://support.google.com/googleplay/android-developer/answer/9859455?hl=en) |
| Data safety | Complete an accurate Data safety form covering the actual release and its SDKs. Reassess it whenever billing, analytics, ads, accounts or cloud features are added. [User Data policy](https://support.google.com/googleplay/android-developer/answer/10144311?hl=en) |
| Listing | Prepare honest screenshots, descriptions, content rating, target audience, app access and ads declarations. Avoid promising acceptance by another app or unattended background operation. [Prepare for review](https://support.google.com/googleplay/android-developer/answer/9859455?hl=en) |

The current APK build and automated scheduler tests are development checks. They do not replace phone testing or Play review. The full AAB, account setup and store declarations remain future release work.

## Payment implementation for a later release

For a Pro feature used inside a Play-distributed app, use **Google Play Billing** unless an applicable regional program explicitly permits an alternative and its enrollment requirements are met. Do not add a generic card or external-payment link as a shortcut. Clearly show the paid features and price. [Payments policy](https://support.google.com/googleplay/android-developer/answer/9858738?hl=en)

Use a currently supported Billing Library version; as of this research, version 7's extension deadline has passed and version 8's new-app/update deadline is August 2027. Recheck the table at implementation time. [Billing version deadlines](https://developer.android.com/google/play/billing/deprecation-faq)

Implement purchase verification, pending/cancelled states, restoring purchases, refunds/revocation and entitlement recovery. Grant Pro only for verified completed purchases, and acknowledge them within Google's three-day window. Prefer secure backend verification when introducing real paid entitlements, then test with Play's license-testing tools before charging customers. [Billing integration](https://developer.android.com/google/play/billing/integrate)

Do not assume one worldwide fee percentage. Google's **2026 service-fee structure varies by buyer region, install cohort and program**, with a separate billing fee in the new structure. The published remaining-market schedule still includes a 15% first-US$1-million tier for enrolled developers. Calculate actual proceeds using the terms that apply to each sale, then account for refunds, tax and bank charges. [Current service fees](https://support.google.com/googleplay/android-developer/answer/112622?hl=en)

## Practical release order

1. Finish the free PC and Android workflows and collect real phone-test feedback.
2. Prepare the AAB, privacy page, accessibility disclosure/video and store listing.
3. Have the owner complete Play Console identity, device, payment and testing requirements.
4. Apply for production access and submit a free release for review.
5. Choose a small, clearly useful Pro feature set based on feedback; integrate and test one-time billing before enabling purchases.

Revenue is possible if people choose to pay for a reliable product. Neither an account fee nor an approved listing guarantees sales, and no earnings estimate has been made here.
