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
   (no trailing slash). The same variable is used by the web admin deploy, so it's probably
   set already.

   > **HTTP vs HTTPS.** While the server is reached as `http://<ip>:<port>` (no HTTPS yet),
   > only the **dev** build can talk to it — the **prod** build refuses plain HTTP, and the
   > workflow stops a prod build with an `http://` address on purpose. Over plain HTTP,
   > passwords travel unencrypted across the internet: fine for a short test, not for real
   > staff. Put the API behind a domain with HTTPS (Cloudflare, see the deploy runbook) before
   > handing phones out, then switch to prod builds.

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

If you skip this step entirely, the mobile workflow still works, but each build is signed
with a **throwaway key** made fresh on GitHub's build machine. Android only installs an update
over an app signed with the same key, so every new build then has to be installed by
uninstalling the old app first (which also clears its unsent orders). Do this step before you
hand the app to staff.

### 3. Firebase (push notifications) — optional

Push notifications (low stock alerts, approval requests) need a `google-services.json`
file from a Firebase project. If you don't set this up yet, the app still builds and runs
— it just won't receive push notifications. (Push is switched on in the app with the next
release; setting the secret now does no harm.)

1. Create a Firebase project at [console.firebase.google.com](https://console.firebase.google.com)
   and add **two** Android apps: package name `app.stockorder.mobile` (prod) and
   `app.stockorder.mobile.dev` (dev). Download the `google-services.json` that covers both.
2. Download the `google-services.json` file it gives you.
3. Base64-encode it:
   - Windows PowerShell: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("google-services.json")) | Set-Clipboard`
   - macOS/Linux: `base64 -i google-services.json | pbcopy` (or redirect to a file)
4. Add it as a repository secret named `GOOGLE_SERVICES_JSON_BASE64`.

## Building an APK

Every change to the app merged into `main` builds a **dev** APK automatically. To build one
by hand (or a prod one):

1. Go to the **Actions** tab → **mobile** workflow → **Run workflow**.
2. Choose `dev` (for testing, works with an `http://` server) or `prod` (HTTPS server only).
3. Wait for the run to finish (about 10 minutes).
4. Open the finished run, scroll to **Artifacts**, and download it. GitHub hands you a
   `.zip`; the `.apk` is inside, named like `app-dev-1.0.0.apk` (`app-<flavor>-<version>.apk`).
   It's a release build of about 20-25 MB that runs on any Android phone.

## Installing the APK on an Android phone

1. Copy the downloaded `.apk` file to the phone (email it to yourself, use a USB cable, or
   a cloud drive — any way you'd normally move a file to the phone works).
2. On the phone, open the file. Android will warn that it's from an "unknown source" —
   this is expected for an app not installed from the Play Store. Allow the install.
3. Open the app and sign in (next section).

## First sign-in on a phone

1. The branch manager creates the user on the web admin (**Users → New user**: branch and
   role, e.g. Cashier or Waiter) and gives the person the **one-time password** it shows.
2. On the phone: **Company code** (the tenant code, e.g. `CHEEZ`), **Username**, and the
   one-time password → **Sign in**.
3. The app asks for a new password (at least 8 characters, not only numbers, not the
   username). After that it opens the home screen and syncs.
4. If the phone has a fingerprint or face unlock, the home screen offers to use it next time.
5. The phone now appears on the web admin's **Devices** page. If it's lost, the branch
   manager (for their staff) or the Owner revokes it there; "Revoke + wipe" also erases the
   app's data on the phone the next time it goes online.

Only users whose role includes **Mobile.Use** can sign in on the app.

The `dev` flavor installs alongside the `prod` flavor as a separate app (different name
and icon badge), so you can have both on the same phone without conflict.

## Troubleshooting

- **Build fails at "Decode release keystore"**: you added `ANDROID_KEYSTORE_BASE64` as a
  repository **variable** instead of a **secret** (or vice-versa for `API_BASE_URL`) —
  double check which tab you used.
- **App installs but push notifications never arrive**: `GOOGLE_SERVICES_JSON_BASE64`
  probably isn't set, or doesn't match the package name in `android/app/build.gradle`.
- **App can't reach the server**: check `API_BASE_URL` has no typo and, if testing over
  your LAN, that the phone and server are on the same network. With an `http://` address
  you must use the **dev** APK.
- **"This build has no server address" and Sign in is greyed out**: `API_BASE_URL` wasn't
  set when the APK was built. Set the variable, run the workflow again, reinstall.
- **"This account isn't allowed to use the mobile app"**: the user's role lacks the
  `Mobile.Use` permission (Roles page on the web admin).
- **"Update required"**: the server's minimum app version (Platform → app versions) is above
  this APK's version. Install the newer APK.
