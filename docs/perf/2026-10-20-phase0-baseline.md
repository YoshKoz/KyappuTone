# Phase 0 baseline — 2026-10-20

Build: Debug, x64, MSIX loose registration (`Add-AppxPackage -Register`).
Commit: recorded in the tag `v0.0.1-alpha`.
Machine: AMD Ryzen 7 9800X3D, 31 GB RAM, Windows 11 26H2 (build 26300).

## Build

| Step | Result |
|---|---|
| Clean restore (`dotnet restore KyappuTone.sln`) | 46 s first run, 1 attempt after one transient `unexpected EOF` from nuget.org |
| Baseline build on .NET 9 (before retarget) | 48 s, 1 warning (SixLabors license) |
| Build after retarget to .NET 10 | 35 s, 0 errors, 4 warnings |
| Build after rebrand | 32 s, 0 errors, 4 warnings |
| Build after ImageSharp removal | 23 s, 0 errors, 3 warnings (NU1510, CS8604, duplicate) |

## Runtime (measured)

| Metric | Value | Note |
|---|---|---|
| Launch to window (packaged, Debug) | under 10 s | measured as wall time until a titled window appeared |
| Working set, fresh launch + 8 s | 303–317 MB | Debug build, 4-track library |
| Working set after browsing Settings + Library | 486 MB | Debug build, not representative of Release |
| Scan of 4 fixture tracks | under 12 s including UI | 4 rows in `Songs`, 4 in `FileScanMeta` |
| Cover thumbnails generated | 4 resized + 4 full-size PNGs, 98,698 / 790,073 bytes | non-zero, readable |

Release-build cold start, idle RAM and 100k-track scan numbers are **not measured** in
Phase 0; they belong to the Phase 1/2 performance gates.

## Verified behaviour

- Private repo seeded, rebranded; zero stale `Tunetastic` identifiers in source (LICENSE,
  NOTICE and upstream docs excepted).
- Builds with no `sixlabors.lic` and no paid license.
- App launches as MSIX, window title `KyappuTone`, tray icon loads.
- Settings page opens without crashing (after the `RPC_E_WRONG_THREAD` fix).
- Library scan picks up mp3, flac, m4a and a filename with Japanese characters and
  Polish diacritics; titles round-trip to SQLite intact.
- Cover art pipeline writes valid PNG thumbnails through `CoverArtProcessor`.

## Known failures and unverified items

| Item | Status |
|---|---|
| Settings page crashed with `0x8001010E` in `SettingsPage.UpdateExtentionListOnUI` | Fixed: UI updates marshalled via `DispatcherQueue` |
| Store update check crashed with `0x803FB112` on sideloaded package | Fixed: `MainPage.CheckForUpdate` catches and logs |
| Playback / seek / next / previous | **Not verified in this session** — UI automation clicked the wrong window; run manually |
| Tray, thumbnail toolbar, taskbar overlay, SMTC, lock screen | **Not verified** — run manually |
| Lyrics, tag editor, playlists, export | **Not verified** — run manually |
| Locked file, long path (>260), corrupt embedded art | **Not verified** — checklist items documented |
| Unsigned MSIX install (vs loose registration) | **Not verified** — CI produced the artifact, install was not attempted |

## CI

Run `39395337086` on `main`: both matrix legs succeeded (x64 3m09s, ARM64 3m05s),
unit tests passed, `KyappuTone-msix-unsigned` artifact uploaded. Only annotation is the
pre-existing CS8604 nullable warning in `MainPlayerPage` clipboard call.

## Post-review verification (2026-10-20)

- Fixes for long-path scan skips, thumbnail fallback caching and unique thumbnail names built cleanly (0 errors, 3 warnings).
- Relaunched the packaged app after the fixes: window titled `KyappuTone`, process alive after 20 s, 332 MB working set, zero `.NET Runtime` crash events, 8 thumbnails intact.

