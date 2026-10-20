# Design: Phase 2B completion, acquisition, and private release

Date: 2026-10-20
Status: pending approval
Product: KyappuTone

## 1. Intent

Finish the remaining Phase 2 work, implement the four requested Phase 3 features,
validate the already-built playlist features, prepare signed release artifacts, and
publish the private repository with a clean README and no personal data.

Items already completed and verified by the human:

- Last.fm connection, now-playing, scrobble queue, and retry.
- Spotify playlist import with the human's own Spotify application.

This document covers:

1. Interactive validation of smart playlists and custom playlist covers.
2. Signing and release packaging.
3. Private-repository update checks.
4. Subfolder auto-playlists.
5. Metadata enrichment.
6. YouTube search and download.
7. yt-dlp and FFmpeg bootstrap.
8. Internet radio.
9. Discord Rich Presence.
10. Repository cleanup, README, and private GitHub publication.

## 2. Goals

- Leave every new feature reachable from the existing navigation without adding a
  second app shell or a background service.
- Keep the player local-first. Network access happens only after an explicit user
  action, except for the existing optional Last.fm, Spotify, and update integrations.
- Keep every new credential in `PasswordVault` or `LocalSettings`; never commit one.
- Keep the build free of paid dependencies.
- Leave the repository private, but make its README and GitHub metadata complete.

## 3. Non-goals

- Spotify, Apple Music, Tidal, or other DRM streaming.
- Radio recording or YouTube playlist import in this pass.
- Automatic MSIX download and installation. The app still opens a release page.
- Publishing the repository publicly or publishing a GitHub release.
- Acquiring a commercial code-signing certificate on the human's behalf.

## 4. Decisions and defaults

These are builder calls unless the human changes them.

### 4.1 Repository stays private

The GitHub repository remains private. The existing public `releases/latest` check
returns 404 for a private repository and is therefore not useful.

Default: add an optional fine-grained GitHub token in Settings. The token needs only
`Contents: read` on this repository. It is stored in `PasswordVault` under
`KyappuTone.GitHub` and used only for `GET /repos/{owner}/{repo}/releases/latest`.

- With a token: update checks work against the private repository.
- Without a token: the check is skipped and Settings explains that a token is needed.
- The token is never logged, displayed back in full, or written to the database.
- The manual GitHub Releases link remains available.

### 4.2 Signing

The repository keeps `CN=KyappuTone Development` until a real publisher certificate is
provided. A new `scripts/package-release.ps1` will:

- validate that the PFX subject exactly matches the manifest `Publisher`;
- build x64 and ARM64 Release packages with signing enabled;
- write SHA-256 checksums next to the packages;
- optionally create and trust a self-signed development certificate for local testing.

A self-signed package is explicitly a development artifact. Production signing still
requires a trusted certificate and its private key.

### 4.3 Subfolder auto-playlists

Auto-playlists are created for the **immediate subfolders** of each configured library
root. A playlist includes every supported track below that subfolder, recursively.
This keeps the number of playlists predictable while matching the usual
`Music\Artist` or `Music\Genre` layout.

- Stored in `Playlists` with `Kind = 'auto'`.
- `RulesJson` stores the absolute folder path and the library root.
- Names are the folder name; duplicate names get a library-name prefix.
- Reconciled after a library scan and on startup.
- Removed when the folder no longer exists, unless the user has manually edited it.
- Added to a dedicated **Folders** page under Library, not to the main sidebar.
- Tracks are re-evaluated when the playlist is opened.

### 4.4 Metadata enrichment

Metadata lookup is opt-in and uses MusicBrainz plus the Cover Art Archive.

- New **Metadata** page under Library.
- Shows tracks whose album, year, or genre is empty or marked `Unknown`.
- Multi-select supported.
- Lookup searches MusicBrainz recordings by artist and title, then release information.
- Candidates show the current values next to the proposed values before anything is
  written.
- Applying writes through TagLib# and updates the local database and thumbnail.
- Cover Art Archive front cover is optional and written as embedded art after preview.
- Requests are rate-limited to one per second, use a descriptive user agent, and are
  cached in SQLite for seven days.
- A global toggle in Settings defaults to off. Per-item lookup is always explicit.
- Failed or ambiguous lookups never modify a file.

### 4.5 Discover hub

One **Discover** page contains three tabs:

- **YouTube** — search, result list, per-result target folder, download action.
- **Downloads** — persisted queue, progress, retry, cancel, open folder.
- **Radio** — station search, favorites, play/stop.

The page also shows the installed yt-dlp and FFmpeg versions and an **Install/Update
tools** action.

### 4.6 yt-dlp and FFmpeg bootstrap

- Tools live in `%LOCALAPPDATA%\<package>\LocalState\Tools`.
- yt-dlp comes from the official `yt-dlp/yt-dlp` release asset.
- FFmpeg comes from an LGPL Windows build, not a GPL build.
- Every download is checked against the release's SHA-256 checksum file before it is
  executed.
- The app supports user-provided paths for both tools when automatic installation is
  blocked or unavailable.
- Versions are recorded; updating is an explicit action.
- A missing or broken yt-dlp produces a clear error and a retry action, never a crash.

### 4.7 YouTube search and downloads

- Search uses `yt-dlp "ytsearch20:<query>" --flat-playlist --dump-single-json`.
  No YouTube API key is required.
- Audio is downloaded with yt-dlp and converted to MP3 by FFmpeg.
- Video metadata and thumbnail are embedded when the source provides them.
- Each queue item has its own target folder; the default comes from Settings.
- Only one download runs at a time. The queue survives restarts.
- Completed files are imported into the local library when the **Import downloads**
  setting is on. The file is inserted into the database directly, so the destination
  folder does not have to be a library root.
- Cancel terminates the child process and removes partial files.
- Radio streams are never recorded or saved.

### 4.8 Internet radio

- Station search and metadata come from the Radio Browser API.
- Favorites are stored in a new SQLite table.
- Playback goes through the existing Flyleaf backend, which handles MP3, AAC, and OGG
  streams.
- `MusicPlayer` gains a stream mode with station title, artist, and cover.
- The progress bar is hidden or indeterminate while a stream is playing.
- An optional ICY metadata reader polls the stream for the current `StreamTitle`
  without downloading the whole stream.
- Radio playback is not scrobbled to Last.fm in this pass, because station metadata is
  frequently wrong.

### 4.9 Discord Rich Presence

- Direct Discord IPC over `discord-ipc-0` through `discord-ipc-9`. No third-party
  package is required.
- The user supplies a Discord application ID in Settings. The ID is not a secret.
- Presence shows the track title, artist, playback state, and elapsed time.
- It updates on play, pause, resume, track change, and stop.
- If Discord is not running or the app ID is empty, presence stays off and the player
  continues normally.
- No album art is uploaded or fetched for presence in this pass.

## 5. Data model changes

New tables:

- `MetadataCache` — query key, JSON response, fetched timestamp.
- `RadioFavorites` — station UUID, name, stream URL, favicon, tags, codec, bitrate,
  country, last played.
- `DownloadQueue` — URL, title, target folder, status, progress, error, output path,
  created timestamp.

Existing `Playlists` gains no new columns. `Kind = 'auto'` and `RulesJson` are reused.

## 6. UI changes

- New Library items: **Folders**, **Metadata**, **Discover**.
- Settings → Integrations: Discord Rich Presence, yt-dlp/FFmpeg paths and status.
- Settings → About: GitHub token field for private update checks.
- Song context menu: **Find metadata** for a single track.
- Playlist header: hide rename/delete actions for auto and smart playlists.
- New pages use the existing page base classes and the existing navigation service.

## 7. Privacy and repository hygiene

- Add the new network calls only for explicit features.
- No telemetry, analytics, crash upload, or remote logging.
- Store the GitHub token in `PasswordVault`.
- Never log request bodies, tokens, or full stream URLs containing credentials.
- Remove tracked build logs, package output, local databases, certificates with private
  keys, and editor state from Git.
- Scan the tree for local paths, user names, email addresses, tokens, and API keys.
- Replace local absolute paths in documentation with placeholders.
- Keep `LICENSE`, `NOTICE`, and FFmpeg license notices.
- Write a new README with feature overview, screenshots, requirements, build steps,
  privacy notes, and credits.
- Set the GitHub description and topics after pushing.

## 8. Verification

- Build x64 Debug and Release with zero warnings.
- Build ARM64 Debug.
- Run all Core tests, including new tests for metadata mapping, folder reconciliation,
  and Discord IPC framing.
- Interactive UI validation on monitor 2:
  - create a smart playlist, reopen it, change a matching field, confirm refresh;
  - change and remove a custom playlist cover;
  - create a folder playlist, rescan, confirm it updates;
  - look up metadata, preview, apply, confirm tags and database;
  - search YouTube, cancel a download, retry, complete, and import;
  - install/update tools;
  - play a radio station and confirm the title updates;
  - confirm Discord presence with a test app ID if available;
  - check for updates with and without a token.
- Confirm no crash log.
- Run the repository secret and personal-data scan before pushing.

## 9. Open decision

The only external blocker is the production signing certificate. If no PFX is
available, this pass will produce unsigned packages plus a self-signed development
package, and the release documentation will say exactly that.
