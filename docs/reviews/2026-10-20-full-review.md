# KyappuTone full review — 2026-10-20

## Follow-up implemented after this review

The findings below describe the tree at review time. Later on 2026-10-20 the following
items were implemented:

- Optional Last.fm scrobbling with user-supplied API credentials, "now playing", a
  persisted offline queue, retry, and disconnect.
- Spotify playlist import using Authorization Code + PKCE, a loopback callback, local
  title/artist matching, and normal KyappuTone playlist creation. No Spotify audio is
  streamed or downloaded.
- Local crash logging for XAML, AppDomain and unobserved task exceptions, with an
  **Open log folder** action in Settings. Nothing is uploaded.
- Custom playlist covers stored in the app's local cover folder.
- Package and assembly version reset to KyappuTone `0.3.0` after backing up and restoring
  the development library database.

Still open: automatic MSIX download/install handoff, a production signing certificate,
smart playlists, subfolder auto-playlists and metadata enrichment. Batch multi-select and
the Favorites quick filter were already present in the All Songs view when checked after
the review. Sideloaded builds now check the public GitHub Releases feed and open the
release page for a manual install; see `docs/release.md`.

## Scope

Reviewed the current `main` tree (`C:\Tools\KyappuTone`) after the Phase 2A/2B work,
branding replacement and the 12,813-track library import.

Checks performed:

- Read the design/spec, phase plans and current service/view structure.
- Searched the source for telemetry, network calls, Last.fm, Spotify, acquisition and
  metadata-enrichment code.
- Built the x64 Debug solution and ran the Core test suite.
- Ran a NuGet vulnerability scan for all projects.
- Rebuilt and launched the app against the imported library.
- Checked rebrand links, Store APIs, update paths and window icons.

## Verdict

The core local-library player is in good shape. Playback, scanning, shell integration,
favorites, history, statistics and the home overview are functional, and the build has
no warnings or errors.

This is not yet a release candidate. It is a local-files-only player: Last.fm scrobbling
and Spotify playback are not implemented, and the acquisition/metadata phases are
mostly untouched. Several inherited rebrand leftovers were still user-visible and were
fixed during this review.

## Fixed during review

- Settings "Check for updates" crashed with `0x803FB112` on sideloaded builds. It now
  detects non-Store packages and shows an explanatory dialog instead of calling Store
  APIs.
- Startup update check is gated the same way.
- Removed the inherited automatic Store-rating prompt and the Store rating/feedback card;
  the linked Store page was the upstream Tunetastic listing, not KyappuTone.
- Removed the Microsoft Store link that pointed at Tunetastic.
- Corrected About/issue/source links to `YoshKoz/KyappuTone`; added a Releases link.
- Removed the hidden "Music Downloader" card that revealed a third-party Telegram bot.
- Replaced the taskbar window icon source with the ICO file; the thumbnail hover icon
  now gets the new KyappuTone mark.
- Hid the visualizer while no track is selected; it was covering the idle logo.
- Fixed Home card rows to lay out horizontally.
- Removed the unused `System.Private.Uri` package reference.
- Fixed two nullable-reference warnings.

## Feature answers

### Last.fm scrobbling

**Not implemented.** There is no Last.fm, scrobble, API-key or account code anywhere in
the tree, and no settings surface for it. The Phase 1 notes explicitly describe the app
as "scrobbling-free". Play counts and listening history are local SQLite only.

Adding it would require:

- a Last.fm API application (API key + shared secret),
- desktop auth flow to obtain a session key, stored with DPAPI,
- "now playing" updates and scrobble submission at the 50%/4-minute threshold,
- offline queue plus retry,
- an opt-in privacy setting and disconnect action.

### Spotify

**No Spotify streaming and no Spotify import yet.**

- Streaming is explicitly out of scope in the design: no DRM streaming catalogs.
- Spotify playlist import is specified for the acquisition phase (Phase 3) using the
  user's own Spotify app: Authorization Code + PKCE, loopback redirect, playlist-read
  scope, title/artist matching and a review list before downloading. None of that code
  exists yet.
- The existing playlist importer only reads/writes local playlist formats (M3U, M3U8,
  PLS, WPL, ZPL). It does not talk to Spotify.

So today: use local files only. Spotify can be used to find music, but KyappuTone cannot
sign in, stream, or import a Spotify playlist yet.

## Open findings

### High priority

1. **Release versioning is inherited from upstream** — assembly/package version
   `1.66.19`. Reset to a KyappuTone semantic version before publishing.
2. **Updater does not match the spec** — spec calls for GitHub Releases/MSIX update
   handoff; only a Store update check exists. For a sideloaded build, Store updates are
   not applicable.
3. **Signing identity is still a development certificate** — `CN=KyappuTone Development`.
   Replace before distributing outside this machine.
4. **No global exception handler / local crash log** — an unhandled UI exception still
   produces the WinForms JIT dialog. The spec promised local diagnostics, not crash
   reporting.

### Medium priority

5. **Phase 2 is incomplete** — smart playlists, mixes, subfolder auto-playlists, custom
   playlist covers, metadata enrichment (MusicBrainz/Cover Art Archive), batch
   multi-select actions and quick filters are not implemented.
6. **Phase 3 is not started** — YouTube search/download, yt-dlp bootstrap, internet
   radio, Discord presence and Spotify import are absent.
7. **No UI/integration test project** — Core has 68 passing tests; the WinUI pages,
   database layer and shell integration have no automated coverage.
8. **Audio test suite was not rerun** because the user asked to stop tone playback.
   The last known result was 20/20.

### Low priority

9. Repository docs and templates had upstream URLs/assignees and stale ImageSharp
   instructions; corrected during this review.
10. `Assets/data/config.json` still contains the upstream Store ID used by the README
    chart tooling; not used by the app at runtime.

## Verification

- `dotnet build .\KyappuTone.sln -p:Platform=x64 -c Debug -p:AppxPackageSigningEnabled=false`:
  succeeded with 0 warnings, 0 errors.
- `dotnet test .\tests\KyappuTone.Core.Tests`: 68 passed, 0 failed.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.
- Library database: 12,813 songs, integrity check `ok`, FTS index present.
- No telemetry or HTTP client code found; the app is offline except for explicit Store
  update checks and user-opened links.
