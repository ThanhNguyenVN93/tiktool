# Social Toolkit

A Windows app that helps you **clean up your social accounts** and **download videos**, all running on your own computer:

- **TikTok Unfollow**: loads the accounts you follow, finds the *inactive* ones (no videos, or not posting for a long time) and unfollows them in bulk, with a confirmation.
- **YouTube Unsubscribe**: loads your subscriptions, reads the date of each channel's newest video, finds the inactive channels and unsubscribes in bulk, with a confirmation and an **automatic CSV backup**.
- **Download**: downloads TikTok / Instagram Reels / YouTube and 1000+ other sites without watermark, using `yt-dlp`.

> Tiếng Việt: xem [README.md](README.md).

The display name is **Social Toolkit**; the executable and the data folder are still called `TikTool` (kept so existing users do not lose their data).

## 1. Install

1. Open the **[Releases](https://github.com/ThanhNguyenVN93/tiktool/releases)** page and download `SocialToolkit-…-win-x64.zip`.
2. Unzip it anywhere (for example `D:\SocialToolkit`) and run **`TikTool.exe`**. No installer, no .NET to install, no Chrome needed.
3. Requirements: **Windows 10/11 64-bit** and the **WebView2 Runtime** (included with Windows 11; usually present on Windows 10). If the app says it is missing, get the "Evergreen Bootstrapper" from <https://developer.microsoft.com/microsoft-edge/webview2/>.
4. **Windows SmartScreen / antivirus may warn** because the app is not code-signed (a code-signing certificate is a paid item). Press *More info → Run anyway*. You can compare the SHA256 of the zip with the one on the Release page to be sure the file is untouched.

## 1b. Updating

- When the app starts and GitHub has a newer release, a bar at the top of the window says **"A new version vX is available"** with **Download**, **What's new / manual** (opens the Release page, where the zip is) and **Later**. The app sends **one request** to GitHub and none of your data.
- **Download**: the app downloads the zip, **checks its SHA256** against the `.sha256` file of the release (a mismatch discards the file, nothing is installed), unpacks it safely. Then press **Restart and update**, confirm (default No), and the app swaps the changed files and **restarts by itself**. If any step fails **everything is rolled back** and the app stays as it was.
- **Install automatically** (**off** by default, turn it on in *Settings → Updates*; some antivirus products may warn about files being replaced): the new version is downloaded in the background and **installed the next time you start the app**.
- Your data in `%APPDATA%\TikTool\` is never touched. `Resources\yt-dlp.exe` and `ffmpeg.exe` are never replaced (you or yt-dlp itself own them).
- A stable install is never moved to a pre-release. To update by hand: download the new zip from the Release page and unzip it over the old folder.
- If an update fails or cannot be installed, look at `logs\update.log`.

## 2. Quick start

1. Open the **Accounts** tab, pick **TikTok** or **YouTube** and sign in right inside the frame (password, QR or SMS). The app notices when you are done. You only need to sign in to the platform you want to use.
2. Go to **Unfollow → TikTok Unfollow** or **YouTube Unsubscribe** and follow sections 3 and 4 below.
3. Next time you open the app it just works, unless you want another account (press **Sign out / Switch account** on the Accounts tab).

## 3. TikTok Unfollow

1. Press **Reload**. A browser window of the app opens and scrolls your following list by itself (1–3 minutes for a few hundred accounts). TikTok does not always return the whole list (for example 516 of 539); finish this batch, then press Reload to try for the rest.
2. Pick the **inactive criterion**: no posts for 30 / 90 / 180 (default) / 365 days, or just never posted (0 videos). Use the filter, sort and search to look at the list.
3. Video counts are known right after loading. The **newest post date** is read separately with the **Check dates** button (slow, and TikTok limits it): about 40 accounts per round, then TikTok blocks for a while; wait and press again. Results are kept for 30 days.
4. Press **Select inactive** or tick accounts one by one, then **Start Unfollow**. The app asks for confirmation (default *No*) showing the number of accounts and a few example names.
5. **Safety**: mutual follows are skipped (can be turned off), right-click an account to **protect** it (🔒, never unfollowed), a limit per session (default 50) and per day (default 100), random pauses between actions, automatic wait when TikTok rate-limits. An unfinished session is offered for resuming the next time you open the app.
6. To follow again: the **History** tab has a follow button per account and exports the unfollowed list to CSV.

## 4. YouTube Unsubscribe

1. Sign in to Google on the Accounts tab (once), then press **Connect**. The app loads your subscriptions and reads the date of each channel's newest video (about 1–2 seconds per channel, remembered for 7 days).
2. Pick the **inactive threshold** (default 1 year). A channel that has a **Shorts** tab but old long videos is tagged *"Has Shorts — unclear"* and is **never selected automatically**, because Shorts have no dates.
3. Select channels (or **Select inactive**) and press **Unsubscribe**. The app asks for confirmation (default *No*), **writes your subscription list to a CSV** before unsubscribing (folder `youtube_backups`), then unsubscribes one by one with pauses.
4. Right-click a channel to **protect** it (🔒), open it or copy its link. To subscribe again use the link in the CSV or the *Re-subscribe* button on the History tab.

## 5. Download

Paste links (several, one per line), pick MP4 / MP3 / MKV, choose the folder and press **Add**. Progress updates live; the history is on the History tab. `yt-dlp.exe` is included in the `Resources` folder; the app checks for an update when it opens (can be turned off in Settings). `ffmpeg.exe` is optional (some formats need it): get it from <https://github.com/BtbN/FFmpeg-Builds/releases> and put it in `Resources`.

## 6. Background browser and captchas

- The work runs in a browser window of the app (WebView2). Turn on **Settings → Run the browser in the background** to hide it; the **👁** button on the bottom bar of the Unfollow / YouTube pages shows or hides it at any time. Running in the background works exactly like running visibly.
- When TikTok or Google shows a **captcha / verification**, the app **stops**, shows the window on top, plays a sound and flashes the taskbar button. You solve it in that window; the app pauses for a while and repeats the interrupted step.
- The app waits **without a time limit** (you may be away) and reminds you at 1, 2, 5 and 10 minutes, then every 30 minutes. Press **Stop** to give up. The app never clicks a captcha or reloads the page by itself. Every occurrence is written to `challenges.log` with a screenshot so the detection can be improved.

## 7. Settings

| Item | Meaning |
|---|---|
| Use the built-in browser | On (default): no Chrome needed. Off: use the installed Chrome/Edge |
| Run the browser in the background | Hide the working window (off by default) |
| Delay between actions / session limit / daily limit | Unfollow pace to avoid TikTok rate limits (default 2–5 s, 50, 100) |
| Skip mutual follows | Do not unfollow people who follow you back (on by default) |
| Auto-wait on rate limit, wait time | On by default, 90 s; also the pause after a captcha is solved |
| Start with Windows / minimize to tray | Optional; the tray menu has shortcuts to the tabs |
| Language | English (default) or Vietnamese (the app restarts by itself) |
| Logs → Open folder | Opens the log folder |
| Updates | Check for updates when the app starts (on), install automatically (off), a *Check now* button |

## 8. Where data is kept, privacy

Everything stays on your computer, in `%APPDATA%\TikTool\`. The app **does not send your data anywhere** (it only uses the network to download videos, check for a yt-dlp update, and talk to TikTok / YouTube on your behalf).

| File / folder | Content |
|---|---|
| `cookie.dat` | The TikTok session, captured by the app when you sign in, encrypted with your Windows key (useless on another machine) |
| `tiktok_webview\`, `youtube_webview\` | Built-in browser profiles (Google keeps its session here) |
| `history.dat`, `whitelist.dat`, `youtube_protected.dat` | History, protected channels |
| `youtube_backups\*.csv` | Subscription backups made before each unsubscribe |
| `settings.dat`, `session_resume.dat`, `lastpost.dat`, `youtube_ages.dat`, `avatars\` | Settings, unfinished session, caches |
| `logs\`, `debug\` | Logs and screenshots taken when a captcha shows |
| `update\` | A downloaded update waiting to be installed (removed after the install) |

**The app does not read or decrypt your own Chrome/Edge data.** To wipe everything: sign out on the Accounts tab, then delete `%APPDATA%\TikTool\` while the app is closed.

## 9. Troubleshooting

| Symptom | What to do |
|---|---|
| WebView2 Runtime missing | Install Microsoft's "Evergreen Bootstrapper", or turn off "Use the built-in browser" and install Chrome/Edge |
| TikTok session expired | Accounts tab → Sign out / Switch account → sign in again |
| TikTok says "Something went wrong" / empty list | TikTok is rate-limiting: wait 30–60 minutes, raise the delay, lower the session limit |
| Check dates stops after ~40 accounts | TikTok blocks for a while: wait and press again, earlier results are kept |
| Opening the app a second time shows no new window | By design: one copy runs and the running one is brought to the front |
| Antivirus / SmartScreen warning | The app is not signed; compare the SHA256 with the Release page, press Run anyway |
| Need to report a bug | Send the content of `errors.log` (look it over first) in [Issues](https://github.com/ThanhNguyenVN93/tiktool/issues) |

## 10. Known limitations

- TikTok and YouTube **change their pages at any time** and a feature can stop working; the data parsers have tests to catch that early.
- The TikTok list can miss a few dozen of the oldest follows (TikTok does not return them).
- TikTok post dates are slow and rate-limited (about 40 accounts per round); YouTube dates are approximate ("3y ago").
- The captcha detection has never seen a real TikTok captcha, so it may need tuning after the first real one.
- This release has only been tried on one machine, not on a clean one.

## 11. Build from source

```bash
dotnet build TikTool.sln
dotnet test TikTool.Tests/TikTool.Tests.csproj
dotnet run --project TikTool/TikTool.csproj
```

Needs the .NET 8 SDK. Put `yt-dlp.exe` (and optionally `ffmpeg.exe`) into `TikTool/Resources/` (not committed). Architecture and the lessons learned live in [CLAUDE.md](CLAUDE.md).

## 12. License, notes

There is no license file in the repo yet. `yt-dlp` is a separate program under the Unlicense (<https://github.com/yt-dlp/yt-dlp>). Use the tool in line with TikTok's and YouTube's terms; unfollowing or unsubscribing in bulk is your decision and your responsibility.