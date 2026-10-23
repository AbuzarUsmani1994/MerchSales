# Building the mobile app (no Android Studio required)

All Android builds happen in GitHub Actions. You don't need Android Studio, an Android
SDK, or Flutter installed on your own machine to get an installable APK.

## One-time setup

### 1. Set the API address

The app needs to know which server to talk to. This is a **repository variable**, not a
secret (it's not sensitive).

1. On GitHub, go to the repository → **Settings** → **Secrets and variables** → **Actions**
   → **Variables** tab.
2. Click **New repository variable**.
3. Name: `API_BASE_URL`. Value: your server's address, e.g. `https://api.yourcompany.com`
   (no trailing slash). For testing against a server on your own network before it has a
   public address, you can use `http://<your-server-LAN-IP>:5000` — this only works for the
   **dev** flavor, which allows plain HTTP.

### 2. Create the Android signing key (once, ever)

This key proves every future update to the app came from you. If you lose it, you cannot
publish updates to an app already installed on phones — back up the values somewhere safe
(a password manager, not another GitHub repo).

1. On GitHub, go to the **Actions** tab → **create-keystore** workflow → **Run workflow**.
2. Leave the defaults (or set your own key alias / company name) → **Run workflow**.
3. When it finishes, open the run and look at the **Summary** tab. It prints four values:
   `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`,
   `ANDROID_KEY_PASSWORD`.
4. Go to **Settings** → **Secrets and variables** → **Actions** → **Secrets** tab, and add
   each of the four as a new repository secret, using the names exactly as shown.
5. **Do this only once.** Running it again creates a *different* key — if you've already
   given anyone a build signed with the old key, a new key means they can't install updates
   over it without uninstalling first.

If you skip this step entirely, the mobile workflow still works — it just produces a
**debug-signed** APK instead of a release-signed one, which is fine for testing but
shouldn't be the one you hand out widely.

### 3. Firebase (push notifications) — optional

Push notifications (low stock alerts, approval requests) need a `google-services.json`
file from a Firebase project. If you don't set this up yet, the app still builds and runs
— it just won't receive push notifications.

1. Create a Firebase project at [console.firebase.google.com](https://console.firebase.google.com)
   and add an Android app with package name `app.stockorder.mobile` (or whatever you've set
   in `mobile/android/app/build.gradle`).
2. Download the `google-services.json` file it gives you.
3. Base64-encode it:
   - Windows PowerShell: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("google-services.json")) | Set-Clipboard`
   - macOS/Linux: `base64 -i google-services.json | pbcopy` (or redirect to a file)
4. Add it as a repository secret named `GOOGLE_SERVICES_JSON_BASE64`.

## Building an APK

1. Go to the **Actions** tab → **mobile** workflow → **Run workflow**.
2. Choose `dev` (for testing) or `prod` (for the real release-signed build).
3. Wait for the run to finish (a few minutes).
4. Open the finished run, scroll to **Artifacts**, and download the `.apk` file — it's named
   like `app-dev-1.0.0-debug.apk`.

## Installing the APK on an Android phone

1. Copy the downloaded `.apk` file to the phone (email it to yourself, use a USB cable, or
   a cloud drive — any way you'd normally move a file to the phone works).
2. On the phone, open the file. Android will warn that it's from an "unknown source" —
   this is expected for an app not installed from the Play Store. Allow the install.
3. Open the app, enter your tenant code, username and password, and sign in.

The `dev` flavor installs alongside the `prod` flavor as a separate app (different name
and icon badge), so you can have both on the same phone without conflict.

## Troubleshooting

- **Build fails at "Decode release keystore"**: you added `ANDROID_KEYSTORE_BASE64` as a
  repository **variable** instead of a **secret** (or vice-versa for `API_BASE_URL`) —
  double check which tab you used.
- **App installs but push notifications never arrive**: `GOOGLE_SERVICES_JSON_BASE64`
  probably isn't set, or doesn't match the package name in `android/app/build.gradle`.
- **App can't reach the server**: check `API_BASE_URL` has no typo and, if testing over
  your LAN, that the phone and server are on the same network.
