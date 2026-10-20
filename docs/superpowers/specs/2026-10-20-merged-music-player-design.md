# Design: One Windows music player combining Tunetastic and Shiranami

Date: 2026-10-20
Status: rev 2, decisions resolved, pending final approval
Product name: **KyappuTone**
Repository: **private** `github.com/YoshKoz/KyappuTone` (GPLv3 source published at release), local checkout `C:\Tools\KyappuTone`

## 1. Intent

Build a single Windows desktop music player that combines what Tunetastic already does
well (native Windows shell integration, tag editing, Mica/Acrylic UI, taskbar overlay,
tray, SMTC, synced LRC lyrics) with what Shiranami does well (modular library views,
favorites, listening history and statistics, recommendation mixes, subsonic-style home
overview, internet radio, YouTube search and download, playlist import, equalizer,
visualizers, sleep timer, mini player, command palette, localization, Discord presence,
in-app updates).

The human's words: "I just want both features from both apps integrated into one" and
"Windows-only for now", using "the most modern and fastest backend and frontend".

## 2. What we are building

A new, independently named GPLv3 Windows application for local music libraries.
It is built as a fork of the Tunetastic codebase, because Tunetastic already solves the
expensive, Windows-specific parts: MSIX packaging, WinUI 3 shell integration, taskbar
overlay windows, thumbnail toolbar, SMTC and lock-screen media controls, tray behavior,
system/app volume modes, and the database-backed library scan. Everything Shiranami has
that Tunetastic lacks is reimplemented from scratch against that base.

It is *not* a fork of Shiranami. Shiranami is source-available, not open source: its
license forbids redistribution, derivative works, and competing products. We may study
it to understand a feature, but no Shiranami code, assets, screenshots, translations, or
copy can enter this repository.

## 3. Goals

1. Feature parity with both apps for Windows users of local music.
2. Modern, fast stack: current stable .NET, current stable Windows App SDK, current
   stable Windows SDK, compiled bindings, GPU visualizers, low idle memory.
3. Native, not web-wrapped: real WinUI 3 chrome, Mica/Acrylic, taskbar and lock-screen
   integration, no Electron/WebView shell for the main UI.
4. Every feature from both apps reachable in at most two clicks from its main view.
5. Shippable in phases; each phase leaves a usable player.

## 4. Non-goals

- macOS, Linux, Android, iOS. Windows only for now; no cross-platform abstraction tax.
- Streaming catalogs or DRM playback (Spotify/Apple Music/Tidal streaming).
- Cloud accounts, sync services, social features, server component.
- Plugin system or scripting surface.
- Mobile remote control, casting.
- Copying Shiranami implementation details, assets, or branding.
- Ripping CDs, DRM removal, or anything that circumvents paid storefronts.

## 5. Anti-goals (would count as failure even if it works)

- A web-tech shell that loses native shell integration or adds 200 MB of runtime.
- A launcher that needs a separate service/daemon running as a second process.
- Feature pile with no coherent navigation; user cannot find the new features.
- Audio glitches, clicks, or EQ that colors the sound when set flat.
- Shipping with telemetry or crash upload on by default.
- A build that requires a paid third-party license key to compile.

## 6. Constraints

- **Platform**: Windows 10 1903+ / Windows 11, x64 and arm64.
- **License**: GPLv3, inherited from Tunetastic. Repository stays public, source
  available, and every derivative distribution stays GPLv3.
- **No SixLabors license dependency.** Tunetastic currently requires a SixLabors
  ImageSharp license file to build. The new app removes ImageSharp entirely and uses
  SkiaSharp (MIT) inside a small `KyappuTone.Core` library for cover-art decode, resize,
  and PNG encode. This removes a build blocker from CI and forks.
- **FFmpeg**: LGPL-2.1+ shared build only. No GPL codecs enabled. Ship `avcodec`,
  `avformat`, `avutil`, `swresample` DLLs with license notices.
- **TagLib#** is LGPL-2.1; dynamic-link/assembly usage with notice is acceptable.
- **Distribution**: MSIX (sideload + optional Microsoft Store) and winget manifest.
  In-app updater checks GitHub Releases.

## 7. Decisions already made by the human

- Windows-only for now.
- One app containing both feature sets.
- Use the most modern/fastest available stack and current build tools.
- Product name **KyappuTone**, published under the human's GitHub account (`YoshKoz`).
- Spotify playlist import available, using the human's own Spotify API application.
- No crash reporting; nothing telemetry-shaped ships.
- Plain English UI only for v1.

## 8. My calls (stated so they can be rejected)

These are builder decisions, not something the human agreed to.

1. **Base on Tunetastic's codebase, not a blank project.** Reimplementing taskbar
   overlay, thumbnail toolbar, SMTC, tray, and MSIX plumbing is weeks of pure loss.
2. **UI: WinUI 3 on Windows App SDK 2.5.1, .NET 10, C# 14.** This is the newest stable
   native Windows UI stack and the only one where every required shell API is
   first-class. Avalonia 12 was considered; it would force manual Win32 work for the
   taskbar overlay and thumbnail toolbar and gains nothing because we are Windows-only.
   A Tauri/React shell was considered and rejected for the same reason, plus the main
   UI would then be a web page.
3. **Audio engine: in-process C#, not Rust.** FFmpeg (via FFmpeg.AutoGen) for
   demuxing/decoding, custom float32 DSP graph (preamp, 10-band biquad EQ, equal-power
   crossfade mixer, limiter), NAudio WASAPI for output, FftSharp for the visualizer tap.
   A Rust core (symphonia + cpal) is genuinely faster on paper, but it adds a second
   toolchain, an FFI boundary, and cross-language debugging for a workload that is
   trivial on a modern CPU (a 10-band EQ on 48 kHz stereo is a few percent of one core).
   The engine sits behind `IAudioEngine` so a Rust core can replace it later without
   touching UI code.
4. **Visualizers: GPU rendering through Vortice.Direct2D1/DXGI into a SwapChainPanel.**
   Win2D 1.4.0 still depends on Windows App SDK 1.8 and is not validated on 2.5; using
   it would pin the whole app to an older SDK. Vortice is MIT, actively maintained, and
   has no WinAppSDK coupling. Fallback for the first visualizer styles is XAML shapes,
   replaced by the D2D path in the same phase.
5. **Database: SQLite with WAL + FTS5 via Microsoft.Data.Sqlite.** Raw SQL with a small
   hand-written data layer, not EF Core: scanning 100k files with EF is measurable
   overhead and the schema is small.
6. **Tag editing: TagLib#.** Tunetastic already uses it and it writes ID3/Vorbis/APIC
   correctly, including embedded lyrics.
7. **Phasing**: four shippable phases (below), audio core first because every other
   feature depends on it.
8. **English-only UI for v1, with resource infrastructure from Phase 0.** Strings live in
   `.resw` resources from the start so a later second language is a translation drop
   rather than a refactor.
9. **No crash reporting subsystem in v1.** Local crash logs plus a "copy diagnostics"
   button; nothing leaves the machine. Revisit after v1 if wanted.

## 9. Stack (verified versions, October 2026)

| Layer | Choice | Version |
|---|---|---|
| SDK | .NET SDK | 10.0.402 (installed); target `net10.0-windows10.0.28000.0` |
| Language | C# | 14 |
| UI | WinUI 3 / Windows App SDK | 2.5.1 (latest stable on NuGet) |
| Windows SDK | build tools | 10.0.28000.2705 |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 |
| DI/host | Microsoft.Extensions.* | 10.0.13 |
| Toolkit UI | CommunityToolkit.WinUI.* | 8.2.251219 (inherited) |
| Database | Microsoft.Data.Sqlite | 10.0.13 |
| Tags | TagLibSharp | 2.3.0 |
| Decode | FFmpeg.AutoGen + LGPL FFmpeg shared DLLs | 9.0.1.1 |
| Output | NAudio (WASAPI shared/exclusive) | 3.3.0 |
| FFT | FftSharp | 2.2.0 |
| GPU visuals | Vortice.Direct2D1 / Vortice.DXGI | 3.8.3 |
| Images | SkiaSharp (MIT) for cover-art processing | 4.153.1 |
| YouTube metadata | YouTubeExplode (optional, search only) | 6.6.2 |
| Discord | DiscordRichPresence (or direct IPC) | 1.143.0 |
| Tests | xUnit + Shouldly | current stable |

Phase 0 includes a compatibility spike for the riskiest pairings: WinAppSDK 2.5.1 with
Vortice SwapChainPanel, and NAudio 3.3 WASAPI on .NET 10. If either fails, fallbacks are
recorded here before code proceeds.

## 10. Architecture

Single process. No background service, no helper daemon.

```
WinUI 3 shell (Views, ViewModels)
        |  commands / observable state (MVVM Toolkit)
Application services
  LibraryService   PlaybackService   DownloadService   RadioService
  MetadataService  StatsService      PresenceService   UpdateService
        |                                   |
SQLite (WAL, FTS5)                  IAudioEngine
                                    +-- DecoderPool (FFmpeg)
                                    +-- DspGraph (preamp, EQ, crossfade, limiter)
                                    +-- WasapiOutput (NAudio)
                                    +-- FftTap -> VisualizerFeed
Shell interop (Win32): SMTC, thumbnail toolbar, taskbar overlay, tray, taskbar progress
```

Threading rules: decoding and DSP on the audio thread/pool; scanning on a bounded
parallel worker set sized by disk type; all UI updates marshalled through the dispatcher;
database writes batched in transactions off the UI thread.

### 10.1 Audio engine

Graph per playback session:

```
Decoder A ─┐
Decoder B ─┴─> resample/convert (f32, 48 kHz, stereo) ─> preamp ─> 10-band EQ
             ─> crossfade mixer ─> limiter ─> WASAPI out
                                       └─> FFT tap (2048 Hann, ~60 Hz)
```

- **Formats**: mp3, flac, m4a/aac/alac, ogg/vorbis, opus, wav, wma (decode only),
  ape/wavpack if FFmpeg supplies them.
- **EQ**: 10 peaking bands at 31.5, 63, 125, 250, 500 Hz, 1, 2, 4, 8, 16 kHz, ±12 dB,
  Q 1.41, plus preamp ±12 dB, 13 presets (Flat, Rock, Pop, Jazz, Classical, Bass
  Boost, Treble Boost, Vocal, Electronic, Hip-Hop, Acoustic, Loudness, Custom).
  Bypass when flat; coefficient interpolation to avoid zipper noise.
- **Crossfade**: dual-deck, equal-power, 0–12 s, applies to manual skip and auto-advance
  separately. Crossfade 0 = gapless: next decoder prerolled 2 s before end.
- **Limiter**: soft-knee lookahead limiter after the mix, prevents clipping with EQ
  boost.
- **Output**: WASAPI shared by default (event-driven, 10 ms period); exclusive mode
  optional in settings. Device-change handling via `IMMNotificationClient`.
- **Latency target**: under 40 ms shared, under 15 ms exclusive.
- **Volume**: app gain in the graph plus optional system-volume control (inherited
  Tunetastic behavior: system vs app mode, pause-on-mute, resume-on-unmute).
- **Seek**: sample-accurate within container granularity; FLAC/MP3 seek via FFmpeg.
- **Soak criterion**: one hour continuous playback with EQ + visualizer enabled, zero
  xruns, zero clicks at track boundaries.

### 10.2 Library and statistics

Schema (summary):

- `tracks`: id, path (unique), title, artist, album_artist, album, genre, year,
  track_no, disc_no, duration_ms, bitrate, sample_rate, channels, codec, size, mtime,
  added_at, last_played_at, play_count, favorite, art_hash, has_lyrics.
- `albums`, `artists`, `genres`: normalized views with derived counts.
- `playlists`: id, name, kind (`user` | `smart` | `auto`), cover_path, rules_json,
  position.
- `playlist_items`: playlist_id, track_id, position.
- `play_history`: id, track_id, started_at, ms_played, completed.
- `radio_favorites`, `downloads`, `lyrics_cache`, `settings` (key/value JSON).
- `tracks_fts` (FTS5) over title, artist, album for instant search.

Scan: enumerate configured folders, filter by extension and minimum duration, skip
unchanged files by (path, size, mtime), read tags in parallel, batch insert in
transactions. Duplicate detection by acoustic-ish key (title+artist+album+duration
bucket) with a settings switch. Auto-scan on startup optional, with a bulk threshold.

Statistics: play counts and last played already exist in Tunetastic; the new history
table adds timestamps, listening minutes, daily/hourly heatmap, top tracks/artists per
range, and a "year ago" memory row for the home screen.

Smart collections: fixed ones (Recently Added, Recently Played, Most Played) plus
rule-based smart playlists (field/op/value, e.g. genre is Jazz AND year >= 1970) and
auto-playlists per subfolder. "Mixes" are generated: a mix is a rule set seeded from
listening history and time of day, refreshed on open.

Recommendations: content-based, no network: co-occurrence of artists/albums in history,
genre affinity, recency decay, plus a "Discover" shelf that queries YouTube for
unowned tracks in the user's top genres and offers preview + import. No account, no
telemetry.

### 10.3 Metadata enrichment

Opt-in per item and globally: MusicBrainz (release lookup) + Cover Art Archive for
missing art, optional AcoustID fingerprinting via `fpcalc` (already present on this
machine, but bundled only if licensing allows; otherwise user-provided path). Write
results through TagLib# with a preview diff before saving. Rate-limited, user agent set,
cache in SQLite.

### 10.4 Acquisition

- **YouTube search**: YouTubeExplode for metadata and preview; playback of a preview via
  a short-lived FFmpeg decode of the stream.
- **Downloads**: external `yt-dlp` + `ffmpeg`, auto-installed into
  `%LOCALAPPDATA%\<App>\tools` from official release assets with hash verification,
  versions recorded, one-click update. Queue with progress, retry, pause, cancel,
  concurrency limit, target folder per item, auto-import into library on completion,
  optional auto-tag from video metadata.
- **Playlist import**: YouTube playlists via yt-dlp flat-playlist. Spotify playlists
  through the user's own Spotify application: OAuth 2.0 Authorization Code with PKCE,
  loopback redirect (`http://127.0.0.1:<ephemeral-port>/callback`), no client secret
  shipped or stored, refresh token encrypted with DPAPI under the user profile. The user
  registers a free Spotify app and pastes its client ID; scopes limited to playlist
  read. Scraping Spotify URLs without credentials is not supported. Every imported track
  passes through a review list with match confidence before any download starts.
- **Radio**: Radio Browser API (station browse, search, country/language/genre filters,
  favorites, last played). Streams play through the same engine; ICY metadata updates
  the now-playing display.

### 10.5 Shell integration (inherited from Tunetastic, kept working)

SMTC (media keys, Bluetooth, notification flyout), Windows 11 lock-screen controls,
thumbnail toolbar (play/pause/prev/next), taskbar playback overlay with the existing
layout set, taskbar progress, tray icon (click play/pause, hover shows track), minimize
to tray, single instance, jump list of playlists.

### 10.6 Polish features

Command palette (Ctrl+K, fuzzy over views/tracks/albums/playlists/actions), shortcut
sheet, mini player (always-on-top compact window with optional lyrics), ambient tint
extracted from cover art, seasonal themes (6 backgrounds) layered on Mica/Acrylic,
noise overlay, UI scale 80–120 %, low-performance mode (disables blur, visualizer,
animations), English-only UI on resource-backed strings ready for translation, Discord
Rich Presence with templates and preview, share deep links (`app://track/<id>?meta=...`),
local crash logs plus a diagnostics export (no upload), in-app updater (check GitHub
Releases, download MSIX, hand off to App Installer).

## 11. Phases

### Phase 0 — Foundation (fork, rename, build green)

- Fresh repository `github.com/YoshKoz/KyappuTone` (private until first release, GPLv3), local checkout
  `C:\Tools\KyappuTone`, seeded from the Tunetastic tree, rebranded identity, new package
  identity (`YoshKoz.KyappuTone`) and app ID.
- Retarget .NET 10 and Windows App SDK 2.5.1; SDK-style MSIX build; CI on GitHub Actions.
- Remove ImageSharp, port image work to SkiaSharp inside `KyappuTone.Core`.
- Repository layout, analyzers, editorconfig, test project.

Exit: app builds with no license files, launches, scans and plays a library, all
existing shell integration still works.

### Phase 1 — Audio core

First action of the phase: compatibility spikes for the risky pairings — Vortice D2D
surface inside a `SwapChainPanel` on WinAppSDK 2.5.1, and NAudio 3.3 WASAPI (shared and
exclusive) on .NET 10. Spikes are throwaway; findings are recorded in the phase notes.

Custom engine replacing the current playback path; 10-band EQ + presets; dual-deck
crossfade and gapless; sleep timer; 12 visualizers on GPU; FFT-driven now-playing view;
audio settings page (output mode, device, buffer, EQ, crossfade); lyrics timing still
works with the new clock.

Exit: one-hour soak clean; EQ measurable on a frequency sweep; crossfade 1–12 s
audible and click-free; visualizers at 60 fps.

### Phase 2 — Library intelligence

Favorites, history + statistics + heatmap, home overview (greeting, stats, top
tracks/albums, recent, memory), mixes, subfolder auto-playlists, rule-based smart
playlists, custom playlist covers, metadata enrichment, multi-select batch actions,
quick filters.

Exit: 100k-track scan under 90 s on NVMe; stats match a seeded test library; playlists
survive restart and rescan.

### Phase 3 — Acquisition

YouTube search + download queue, yt-dlp/ffmpeg bootstrap and updates, playlist import
with confidence review, internet radio, download folders per item, auto-import and
optional auto-tag.

Exit: search → download → tag → library → play works end to end, offline-safe failure
paths, no crash on yt-dlp update breakage.

### Phase 4 — Polish and distribution

Command palette, shortcut sheet, mini player, ambient tint, seasonal themes, noise
overlay, UI scale, low-perf mode, English-only UI on resource-backed strings, Discord
presence, deep links, diagnostics export, updater, installer/signing, winget manifest,
store submission optional.

Exit: v1.0 installer, signed MSIX, clean upgrade from previous MSIX, translated-ready
resources, documented feature matrix versus both source apps.

## 12. Performance targets

| Metric | Target | Measured 2026-10-20 |
|---|---|---|
| Cold start to interactive | ≤ 1.5 s (warm disk, 50k tracks) | not measured |
| Idle RAM | ≤ 250 MB with 50k tracks | not measured (app working set 296–308 MB after launch, library size unknown) |
| Scan 100k tracks (NVMe, tags cached) | ≤ 90 s | not measured |
| Scan unchanged library (no-op) | ≤ 3 s | not measured |
| UI scroll | steady 60 fps | not measured |
| Audio CPU (EQ only) | ≤ 3 % of one core | 1.11 % (0.3 min engine harness, EQ + FFT tap) |
| Audio CPU (EQ + visualizer) | ≤ 8 % of one core | not measured (tone soak stopped at user request) |
| Audio dropouts | 0 in 1 h soak | 0 in 0.3 min; 1 h soak not run |
| Search latency (FTS5) | ≤ 50 ms at 100k tracks | not measured |

Measurements are recorded in `docs/perf/` per phase; claims in release notes cite them.

## 13. Testing

- Unit: biquad response, crossfade math, limiter, playlist rule evaluation, scanner
  filters, duplicate keys, tag round-trip, LRC parsing/offset conventions.
- Integration: generated WAV/FLAC fixtures through the engine; scan a temp tree and
  assert DB rows; import/export round-trip; smart playlist evaluation.
- Soak: headless harness that plays 1 h of generated audio with EQ + FFT tap and counts
  xruns.
- Manual checklist per phase for shell integration (taskbar overlay positions, lock
  screen, media keys, tray, multi-monitor DPI).
- No UI automation framework in v1; manual checklist plus screenshot review.

## 14. Repository and tooling

```
<repo>/
  KyappuTone.csproj        WinUI 3 app project (flat Tunetastic layout in Phase 0)
  Common/ Views/ ViewModels/ Themes/ Overlay/ Assets/ ...
  src/KyappuTone.Core/     new pure .NET library (imaging in Phase 0; DSP, models,
                           database and services move here from Phase 1)
  tests/KyappuTone.Core.Tests/
  scripts/                 build, smoke and tool-bootstrap helpers
  docs/superpowers/specs/  this document and later phase notes
  docs/superpowers/plans/  phase plans
  docs/perf/               measured results
  build/                   MSIX assets, CI scripts, winget manifest (Phase 4)
```

The flat app layout stays in Phase 0 to keep the rebrand mechanical and reviewable. The
`src/App` restructure happens in Phase 1 when the audio core makes the split worth it.

- Build: `dotnet build -p:Platform=x64`, MSIX signing disabled locally.
- CI: restore, build x64 + arm64, run tests, produce unsigned MSIX artifact.
- No secrets required to build. No paid license required to build.
- Analyzers: .NET analyzers, nullable enabled, warnings as errors in CI.

## 15. Risks

1. WinAppSDK 2.5.1 + Vortice surface incompatibility (mitigated by Phase 0 spike and
   XAML-shape fallback).
2. NAudio 3.3 WASAPI behavior changes on .NET 10 (mitigated by spike; fallback is a
   thin custom WASAPI wrapper).
3. FFmpeg LGPL build/redistribution bookkeeping (mitigated by shipping unmodified LGPL
   DLLs plus notices, no GPL codecs).
4. yt-dlp breakage on YouTube changes (mitigated by pinning versions, update button,
   clear error surface, YouTubeExplode fallback for metadata).
5. Spotify import fragility and terms (mitigated by title-matching import and optional
   user credentials; no scraping of protected content).
6. Scope: 40+ features is months of work; mitigated by phases, each usable.
7. Shiranami license contamination (mitigated by clean-room rule and review checklist).

## 16. Resolved questions (2026-10-20)

1. **Name and identity**: KyappuTone, repository under `YoshKoz` kept **private** for now;
   GPLv3 source becomes public when a release is published. Sideload MSIX plus winget for
   v1; Microsoft Store submission optional and later.
2. **Spotify import**: the human's own Spotify API application. PKCE + loopback redirect,
   user-pasted client ID, no client secret in the binary, tokens DPAPI-encrypted,
   playlist-read scopes only.
3. **Crash reporting**: none in v1. Local logs and diagnostics export only; no upload,
   no telemetry. Revisit post-v1.
4. **Language**: plain English only in v1. Resource infrastructure is present so a second
   language is a later translation task.

## 17. Left to the builder

- Exact visualizer art direction per style, layout of the home overview, and palette.
- Naming of internal modules and DB migration strategy details.
- Which FFmpeg LGPL build to vendor and its update cadence.
- Whether AcoustID fingerprinting ships bundled or user-provided.
- Concrete smart-playlist rule grammar.
- Test fixture audio set.
