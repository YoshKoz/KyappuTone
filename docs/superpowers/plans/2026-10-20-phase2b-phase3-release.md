# KyappuTone Phase 2B/3 Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the remaining Phase 2 library features, add the four requested Phase 3 features, validate the existing playlist UI, prepare release packages, and publish the cleaned private repository with a complete README.

**Architecture:** Keep the existing single-process WinUI 3 shell. Add small pure-logic classes in `KyappuTone.Core` for testable parsing and matching, and thin app services for HTTP, SQLite, TagLib, child processes, named pipes, and playback. Add three pages under the existing navigation: Folders, Metadata, and Discover (YouTube, Downloads, Radio). Keep credentials in `PasswordVault` and keep all network work explicit.

**Tech Stack:** .NET 10, C# 14, WinUI 3 / Windows App SDK 2.5.1, SQLite/FTS5, TagLib#, yt-dlp + FFmpeg child processes, Radio Browser API, Discord IPC, xUnit/Shouldly.

**Spec:** `docs/superpowers/specs/2026-10-20-phase2b-phase3-release-design.md`

## Global Constraints

- Windows only; x64 and ARM64.
- Build x64 Debug and Release with zero warnings.
- Build ARM64 Debug with zero warnings.
- All text files use CRLF.
- No telemetry, analytics, or crash upload.
- No paid build dependency.
- No Shiranami code, assets, screenshots, translations, or copy.
- Keep the repository private.
- Store all secrets in `PasswordVault`; never commit a token, key, password, or private certificate.
- Do not run tone playback or soak loops; the user's "stop testing tones" instruction remains active.
- Launch every app and test window on monitor 2 (DISPLAY5, x = 1920-3840).
- Write code, commits, README, and user-facing docs in normal English, not Caveman style.

## Review Focus

- Starting the app without a GitHub token must not show an error or block startup.
- A failed yt-dlp/FFmpeg download must show a recoverable error and leave the player usable.
- A radio stream has no finite duration; progress math, auto-advance, and scrobbling must not divide by zero or treat the stream as a local track.
- An ambiguous or mismatched MusicBrainz result must never write tags without the user confirming the preview.
- Auto-playlist reconciliation must never touch user or smart playlists, and must not create duplicate names.
- Discord being absent, closed, or misconfigured must not crash or slow playback.
- No personal paths, usernames, email addresses, logs, databases, API keys, or private certificates may enter Git.

---

## Task 1: Baseline, repository cleanup, and test harness

**Files:**
- Modify: `.gitignore`
- Modify: `docs/reviews/2026-10-20-full-review.md`
- Test: existing test projects

**Interfaces:**
- Produces: a clean tracked tree and a recorded baseline for every later task.

- [ ] **Step 1: Record the baseline**

Run:

```powershell
git status --short --branch
dotnet test .\tests\KyappuTone.Core.Tests
dotnet build .\KyappuTone.sln -p:Platform=x64 -c Debug -p:AppxPackageSigningEnabled=false
```

Expected: tests pass (currently 77/77); build has 0 warnings and 0 errors.

- [ ] **Step 2: Audit tracked files for generated or personal content**

Run:

```powershell
git ls-files | Select-String -Pattern '(?i)(\.log$|\.db3$|\.pfx$|\.p12$|^bin/|^obj/|AppPackages|\.superpowers|crash|LocalState)'
gitleaks detect --source . --no-git --redact
```

Expected: no private key, token, local database, or crash log is tracked. Remove any
upstream-branded certificate or generated file that is not needed.

- [ ] **Step 3: Add explicit ignores**

Add to `.gitignore`:

```gitignore
# KyappuTone local state
*.db3
*.db3-shm
*.db3-wal
*.pfx
*.p12
crash.log
app.log
AppPackages/
Tools/
.venv/
```

Do not delete files from disk; only stop tracking generated content.

- [ ] **Step 4: Commit the cleanup**

```powershell
git add .gitignore
git commit -m "chore: keep local state and signing material out of git"
```

---

## Task 2: Private GitHub update checks

**Files:**
- Create: `src/KyappuTone.Core/Integrations/GitHub/GitHubReleaseParser.cs`
- Create: `tests/KyappuTone.Core.Tests/Integrations/GitHubReleaseParserTests.cs`
- Modify: `Common/Services/UpdateService.cs`
- Modify: `Common/Enums.cs`
- Modify: `Views/SettingsPage.xaml`
- Modify: `Views/SettingsPage.xaml.cs`
- Modify: `Views/MainPage.xaml.cs`
- Modify: `docs/release.md`

**Interfaces:**
- Produces: `GitHubReleaseParser.Parse(string json) -> GitHubRelease?`
- Produces: `UpdateService.GetLatestReleaseAsync(string? token) -> Task<UpdateInfo?>`
- Produces: `GitHubTokenService.Save(string token)`, `Load()`, `Remove()`, `HasToken`.

- [ ] **Step 1: Write the failing parser tests**

Test names:

```csharp
[Fact] public void Parses_a_published_release()
[Fact] public void Rejects_draft_and_prerelease()
[Fact] public void Rejects_missing_tag_or_url()
[Fact] public void Accepts_v_prefixed_semantic_tag()
```

Use a minimal JSON fixture with `tag_name`, `html_url`, `body`, `draft`, and
`prerelease`; assert the parsed version is `0.4.0` for tag `v0.4.0`.

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test .\tests\KyappuTone.Core.Tests --filter GitHubReleaseParserTests`

Expected: FAIL because the parser type does not exist.

- [ ] **Step 3: Implement the parser**

`GitHubRelease` record fields: `Version Version`, `string Tag`, `string ReleaseUrl`,
`string Notes`. `Parse` returns null for drafts, prereleases, missing fields, or an
invalid version.

- [ ] **Step 4: Run the parser tests**

Expected: PASS.

- [ ] **Step 5: Add the token vault wrapper**

Add `Common/Services/GitHubTokenService.cs` with:

```csharp
public static class GitHubTokenService
{
    public static bool HasToken { get; }
    public static void Save(string token);
    public static string? Load();
    public static void Remove();
}
```

Use `PasswordVault` resource `KyappuTone.GitHub`, user `token`. Trim input and ignore
empty values.

- [ ] **Step 6: Use the token in UpdateService**

Change `GetLatestReleaseAsync(string? token = null)`. When a token is present, add
`Authorization: Bearer <token>`. Keep the existing user agent and `Accept` header.
Return null for 404/403 and on malformed JSON.

- [ ] **Step 7: Add Settings UI**

Under About, add:

- `PasswordBox GitHubTokenBox` with placeholder `GitHub token (Contents: read)`;
- `Button SaveGitHubTokenButton`;
- `Button ClearGitHubTokenButton`;
- status text `Private update checks enabled` / `Token not set`.

On save, store the token and show a success notification. On clear, remove it.
Show the token as empty after loading; never read it back into the UI.

- [ ] **Step 8: Update startup and manual check**

`MainPage.CheckForUpdate` calls the service only when `GitHubTokenService.HasToken`.
`SettingsPage.CheckGitHubUpdateAsync` passes the token and shows a clear message when
the token is missing. Do not call the public release feed.

- [ ] **Step 9: Run tests and build**

Run: `dotnet test .\tests\KyappuTone.Core.Tests` and the x64 Debug build.

Expected: all tests pass; 0 warnings, 0 errors.

- [ ] **Step 10: Commit**

```powershell
git add src/KyappuTone.Core/Integrations/GitHub tests/KyappuTone.Core.Tests/Integrations Common/Services/GitHubTokenService.cs Common/Services/UpdateService.cs Common/Enums.cs Views/SettingsPage.xaml Views/SettingsPage.xaml.cs Views/MainPage.xaml.cs docs/release.md
git commit -m "feat: support private GitHub update checks"
```

---

## Task 3: Folder auto-playlists

**Files:**
- Create: `src/KyappuTone.Core/Library/FolderPlaylistPlanner.cs`
- Create: `tests/KyappuTone.Core.Tests/Library/FolderPlaylistPlannerTests.cs`
- Create: `Common/Services/FolderPlaylistService.cs`
- Create: `Views/LibraryViews/FoldersPage.xaml`
- Create: `Views/LibraryViews/FoldersPage.xaml.cs`
- Modify: `Common/Services/DatabaseHelper.cs`
- Modify: `Common/Operations/LibraryScanner.cs`
- Modify: `App.xaml.cs`
- Modify: `Views/PlaylistViews/PlayListTemplate.xaml.cs`
- Modify: `Assets/NavViewMenu/AppData.json`

**Interfaces:**
- Produces: `FolderPlaylistPlanner.Build(IEnumerable<LibraryRoot> roots, IEnumerable<string> folders) -> IReadOnlyList<FolderPlaylist>`
- Produces: `FolderPlaylistService.ReconcileAsync()`
- Produces: `FolderPlaylistService.RefreshAsync(string playlistName)`
- Produces: `DatabaseHelper.GetAutoPlaylists()`, `UpsertAutoPlaylist(...)`, `RemoveAutoPlaylist(...)`.

- [ ] **Step 1: Write failing planner tests**

Test names:

```csharp
[Fact] public void Creates_one_playlist_per_immediate_subfolder()
[Fact] public void Uses_library_prefix_when_folder_names_collide()
[Fact] public void Ignores_folders_outside_the_library_root()
[Fact] public void Is_case_insensitive_on_windows_paths()
```

Use roots `C:\Music\Main` and `C:\Music\Other`, folders `C:\Music\Main\Rock`,
`C:\Music\Other\Rock`, and `C:\Music\Main\Rock\Live`. Expect two playlists, named
`Main - Rock` and `Other - Rock`, both with recursive track scope.

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test .\tests\KyappuTone.Core.Tests --filter FolderPlaylistPlannerTests`

- [ ] **Step 3: Implement the planner**

Types:

```csharp
public sealed record LibraryRoot(string Name, string Path);
public sealed record FolderPlaylist(string Name, string FolderPath, string LibraryRoot);
public static class FolderPlaylistPlanner
{
    public static IReadOnlyList<FolderPlaylist> Build(
        IEnumerable<LibraryRoot> roots,
        IEnumerable<string> folders);
}
```

Only immediate subfolders of a root qualify. Names are folder names; on collision use
`<library name> - <folder name>` and then append a numeric suffix if still colliding.

- [ ] **Step 4: Run the planner tests**

Expected: PASS.

- [ ] **Step 5: Add database methods**

Add:

```csharp
public Task<List<PlaylistName>> GetAutoPlaylists()
public Task UpsertAutoPlaylist(string name, string folderPath, string libraryRoot)
public Task RemoveAutoPlaylist(string name)
```

Use `Kind = 'auto'`. Store `RulesJson` as JSON with `folderPath` and `libraryRoot`.
`UpsertAutoPlaylist` must not overwrite a user playlist with the same name; return or
skip instead.

- [ ] **Step 6: Implement reconciliation**

`FolderPlaylistService.ReconcileAsync`:

1. Load library roots.
2. Enumerate immediate directories under each root.
3. Keep only directories that contain at least one supported audio file recursively.
4. Build the planner output.
5. Upsert each auto playlist and replace its songs with matching library paths.
6. Remove auto playlists whose folder is gone.
7. Never delete or rename a playlist whose `Kind` is `user` or `smart`.

`RefreshAsync` re-queries the database for songs under the stored folder and replaces
the playlist rows.

- [ ] **Step 7: Call reconciliation**

Call `await FolderPlaylistService.ReconcileAsync()` after `LibraryScanner.ScanLibraries`
completes and after `DatabaseHelper.InitializeDatabase()` in `App.OnLaunched`.
Do not block startup navigation; run it as a background task after the main page loads.

- [ ] **Step 8: Add the Folders page**

List auto playlists with name, track count, and an open action. Include a Refresh
button. Empty state: "No folder playlists yet. Add a library folder and rescan."
Navigation parameter is the playlist name, matching `PlayListTemplate`.

- [ ] **Step 9: Protect auto playlists in the playlist header**

In `PlayListTemplate`, read the playlist kind on navigation. Hide Rename, Delete,
cover change, and cover remove for `smart` and `auto` playlists. Keep Play, Shuffle,
sort, and export.

- [ ] **Step 10: Add navigation**

Add `KyappuTone.Views.LibraryViews.FoldersPage` titled `Folders` under Library with a
folder glyph. Build once so the T4 navigation map includes it.

- [ ] **Step 11: Run tests and build**

Run Core tests and the x64 Debug build. Expected: 0 warnings, 0 errors.

- [ ] **Step 12: Commit**

```powershell
git add src/KyappuTone.Core/Library tests/KyappuTone.Core.Tests/Library Common/Services/FolderPlaylistService.cs Common/Services/DatabaseHelper.cs Common/Operations/LibraryScanner.cs App.xaml.cs Views/LibraryViews/FoldersPage.xaml Views/LibraryViews/FoldersPage.xaml.cs Views/PlaylistViews/PlayListTemplate.xaml.cs Assets/NavViewMenu/AppData.json
git commit -m "feat: add folder auto-playlists"
```

---

## Task 4: Metadata enrichment core and service

**Files:**
- Create: `src/KyappuTone.Core/Metadata/MetadataModels.cs`
- Create: `src/KyappuTone.Core/Metadata/MusicBrainzMapper.cs`
- Create: `src/KyappuTone.Core/Metadata/MetadataDiffBuilder.cs`
- Create: `tests/KyappuTone.Core.Tests/Metadata/MusicBrainzMapperTests.cs`
- Create: `tests/KyappuTone.Core.Tests/Metadata/MetadataDiffBuilderTests.cs`
- Create: `Common/Services/MetadataService.cs`
- Create: `Common/Services/TagMetadataWriter.cs`
- Modify: `Common/Services/DatabaseHelper.cs`
- Modify: `Common/Enums.cs`

**Interfaces:**
- Produces: `MusicBrainzMapper.ParseRecordings(string json) -> IReadOnlyList<MetadataCandidate>`
- Produces: `MetadataDiffBuilder.Build(Song current, MetadataCandidate candidate) -> MetadataDiff`
- Produces: `MetadataService.SearchAsync(Song song, CancellationToken) -> Task<IReadOnlyList<MetadataCandidate>>`
- Produces: `TagMetadataWriter.ApplyAsync(Song song, MetadataCandidate candidate, bool writeCover) -> Task<Song>`
- Produces: `DatabaseHelper.GetMetadataCache(string key)`, `SetMetadataCache(string key, string json)`, `ClearMetadataCache()`.

- [ ] **Step 1: Write failing mapper tests**

Test names:

```csharp
[Fact] public void Parses_recording_title_artist_release_and_year()
[Fact] public void Ignores_recordings_without_a_title()
[Fact] public void Builds_cover_art_archive_url_from_release_id()
[Fact] public void Maps_empty_genres_to_null()
```

Expected candidate for a fixture: title `Blue Monday`, artist `New Order`, album
`Power, Corruption & Lies`, year `1983`, cover URL beginning with
`https://coverartarchive.org/release/`.

- [ ] **Step 2: Write failing diff tests**

Test names:

```csharp
[Fact] public void Only_reports_changed_or_missing_fields()
[Fact] public void Does_not_replace_an_existing_value_with_an_empty_candidate()
[Fact] public void Treats_unknown_as_missing()
```

Expected: an existing album stays unchanged; `Unknown Artist` is a proposed change;
empty candidate fields are omitted.

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test .\tests\KyappuTone.Core.Tests --filter Metadata`

- [ ] **Step 4: Implement the models and mapper**

`MetadataCandidate` fields: `RecordingId`, `Title`, `Artist`, `Album`, `Year`,
`Genre`, `CoverUrl`, `Score`. `MetadataDiff` has `IReadOnlyList<MetadataChange> Changes`
and a `bool HasChanges` property. `MetadataChange` has `Field`, `Current`, `Proposed`.

- [ ] **Step 5: Run the core tests**

Expected: PASS.

- [ ] **Step 6: Add the cache table and methods**

Create `MetadataCache(Key TEXT PRIMARY KEY, Json TEXT NOT NULL, FetchedAtUtc INTEGER NOT NULL)`.
Cache lookups for seven days. Add `ClearMetadataCache` for Settings.

- [ ] **Step 7: Implement MetadataService**

Use `HttpClient` with user agent
`KyappuTone/<version> ( https://github.com/YoshKoz/KyappuTone )`. Search
`https://musicbrainz.org/ws/2/recording?query=...&fmt=json&limit=5`. Rate-limit to one
request per second with a `SemaphoreSlim` and a timestamp check. Return an empty list
on network errors; do not throw into the UI.

- [ ] **Step 8: Implement TagMetadataWriter**

Write Title, Performers, Album, Year, Genres through TagLib#. If `writeCover` and the
candidate has a cover URL, download the front image, embed it as `FrontCover`, and
regenerate the thumbnail. Return an updated `Song` with the new values. Do not save
when `HasChanges` is false.

- [ ] **Step 9: Add tests for no-op writes**

Add a test that `MetadataDiffBuilder` produces `HasChanges == false` for identical
values, and a test that an empty candidate never produces a change. Do not test actual
file writes in the Core project.

- [ ] **Step 10: Run tests and build**

- [ ] **Step 11: Commit**

```powershell
git add src/KyappuTone.Core/Metadata tests/KyappuTone.Core.Tests/Metadata Common/Services/MetadataService.cs Common/Services/TagMetadataWriter.cs Common/Services/DatabaseHelper.cs Common/Enums.cs
git commit -m "feat: add metadata lookup and diffing"
```

---

## Task 5: Metadata page and settings

**Files:**
- Create: `Views/LibraryViews/MetadataPage.xaml`
- Create: `Views/LibraryViews/MetadataPage.xaml.cs`
- Modify: `Assets/NavViewMenu/AppData.json`
- Modify: `Views/SettingsPage.xaml`
- Modify: `Views/SettingsPage.xaml.cs`
- Modify: `Views/MainPage.xaml` and `Views/MainPage.xaml.cs`
- Modify: `Common/Enums.cs`

**Interfaces:**
- Consumes: `MetadataService`, `TagMetadataWriter`, `MetadataDiffBuilder`.
- Produces: `LocalSave.MetadataLookupEnabled` (default off).

- [ ] **Step 1: Add the Settings toggle**

Settings → Integrations → "Metadata enrichment": toggle off by default, with text
"MusicBrainz and Cover Art Archive; explicit lookups only." Add a "Clear metadata
cache" button.

- [ ] **Step 2: Add the Metadata page**

List tracks where `Album`, `Year`, or `Genre` is empty/`Unknown`. Show title, artist,
album, and a "Find metadata" button. Support multi-select and "Look up selected".
Disable lookup when the global toggle is off and show a link to Settings.

- [ ] **Step 3: Add the candidate preview**

On lookup, show a ContentDialog or in-page panel with candidates. For the selected
candidate, show a two-column diff: current and proposed. Include a "Write cover art"
checkbox. Buttons: Apply, Skip. Apply calls `TagMetadataWriter.ApplyAsync`, then
refreshes the row and shows a success notification.

- [ ] **Step 4: Handle ambiguous and failed lookups**

If no candidates, show "No confident match" and do not write. If the service throws,
show a recoverable error. Never auto-select the first candidate.

- [ ] **Step 5: Add the song context menu action**

Add "Find metadata" to the shared song context menu. It opens the Metadata page with
the selected track, or opens the same preview directly from `MainPage`.

- [ ] **Step 6: Add navigation**

Add `MetadataPage` titled `Metadata` under Library with a tag glyph. Build once for
the generated navigation map.

- [ ] **Step 7: Run tests and build**

Build x64 Debug; fix XAML compile errors before continuing.

- [ ] **Step 8: Commit**

```powershell
git add Views/LibraryViews/MetadataPage.xaml Views/LibraryViews/MetadataPage.xaml.cs Views/SettingsPage.xaml Views/SettingsPage.xaml.cs Views/MainPage.xaml Views/MainPage.xaml.cs Common/Enums.cs Assets/NavViewMenu/AppData.json
git commit -m "feat: add metadata enrichment page"
```

---

## Task 6: yt-dlp and FFmpeg bootstrap

**Files:**
- Create: `src/KyappuTone.Core/Acquisition/ChecksumFile.cs`
- Create: `src/KyappuTone.Core/Acquisition/SafeArchive.cs`
- Create: `tests/KyappuTone.Core.Tests/Acquisition/ChecksumFileTests.cs`
- Create: `tests/KyappuTone.Core.Tests/Acquisition/SafeArchiveTests.cs`
- Create: `Common/Services/ToolBootstrapService.cs`
- Modify: `Views/SettingsPage.xaml`
- Modify: `Views/SettingsPage.xaml.cs`
- Modify: `Common/Enums.cs`

**Interfaces:**
- Produces: `ChecksumFile.TryFind(string checksumText, string fileName, out string sha256)`
- Produces: `SafeArchive.IsSafeEntry(string destinationRoot, string entryName)`
- Produces: `ToolBootstrapService.GetStatusAsync()`, `InstallOrUpdateAsync(IProgress<string>)`
- Produces: `LocalSave.YtDlpPath`, `LocalSave.FfmpegPath`.

- [ ] **Step 1: Write failing checksum tests**

Test names:

```csharp
[Fact] public void Finds_a_sha256_for_an_exact_file_name()
[Fact] public void Ignores_comments_and_blank_lines()
[Fact] public void Rejects_a_mismatched_file_name()
```

Use lines in `SHA2-256SUMS` format: `<64 hex>  <file>`.

- [ ] **Step 2: Write failing archive-safety tests**

Test names:

```csharp
[Fact] public void Accepts_paths_below_the_destination()
[Fact] public void Rejects_parent_directory_escape()
[Fact] public void Rejects_rooted_paths()
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test .\tests\KyappuTone.Core.Tests --filter "ChecksumFileTests|SafeArchiveTests"`

- [ ] **Step 4: Implement the pure helpers**

`ChecksumFile` accepts uppercase or lowercase hex and trims `*` before the file name.
`SafeArchive` normalizes separators and rejects `..`, rooted paths, and drive-rooted
paths.

- [ ] **Step 5: Run the core tests**

Expected: PASS.

- [ ] **Step 6: Implement ToolBootstrapService**

Paths:

```text
%LOCALAPPDATA%\Packages\<package>\LocalState\Tools\yt-dlp.exe
%LOCALAPPDATA%\Packages\<package>\LocalState\Tools\ffmpeg.exe
```

Sources:

- yt-dlp: latest official `yt-dlp.exe` asset and `SHA2-256SUMS`.
- FFmpeg: latest LGPL Windows archive from the yt-dlp/FFmpeg-Builds release, plus its
  checksum file; extract only `ffmpeg.exe`.

Download to a temporary file, verify SHA-256, then replace the target atomically.
Use a single HttpClient with a 60-second timeout. Show progress text through
`IProgress<string>`. Never execute a file whose checksum did not match.

- [ ] **Step 7: Add Settings UI**

Settings → Integrations → "Download tools": show installed versions or "Not installed";
buttons Install/Update, Browse yt-dlp, Browse FFmpeg; a note that yt-dlp is GPL-2.0+ and
the FFmpeg build is LGPL. Use file pickers for manual paths.

- [ ] **Step 8: Add fallback behavior**

If bootstrap fails, keep manual paths usable and show the actual error. If neither
tool is available, YouTube search/download shows "Install yt-dlp in Settings" instead
of attempting a process launch.

- [ ] **Step 9: Run tests and build**

- [ ] **Step 10: Commit**

```powershell
git add src/KyappuTone.Core/Acquisition tests/KyappuTone.Core.Tests/Acquisition Common/Services/ToolBootstrapService.cs Views/SettingsPage.xaml Views/SettingsPage.xaml.cs Common/Enums.cs
git commit -m "feat: bootstrap yt-dlp and ffmpeg safely"
```

---

## Task 7: YouTube search and download queue

**Files:**
- Create: `src/KyappuTone.Core/Acquisition/YtDlpOutputParser.cs`
- Create: `tests/KyappuTone.Core.Tests/Acquisition/YtDlpOutputParserTests.cs`
- Create: `Common/Services/YouTubeService.cs`
- Create: `Common/Services/DownloadService.cs`
- Modify: `Common/Services/DatabaseHelper.cs`
- Modify: `Common/Enums.cs`
- Modify: `Common/Operations/LibraryScanner.cs`

**Interfaces:**
- Produces: `YtDlpOutputParser.ParseSearch(string json) -> IReadOnlyList<YouTubeSearchResult>`
- Produces: `YtDlpOutputParser.ParseProgress(string line) -> DownloadProgress?`
- Produces: `DownloadService.EnqueueAsync(DownloadRequest)`, `CancelAsync(int id)`, `RetryAsync(int id)`, `QueueChanged`.
- Produces: `DatabaseHelper.GetDownloadQueue()`, `UpsertDownload(...)`, `RemoveDownload(...)`.

- [ ] **Step 1: Write failing parser tests**

Test names:

```csharp
[Fact] public void Parses_flat_search_entries()
[Fact] public void Skips_entries_without_an_id_or_title()
[Fact] public void Parses_percent_speed_and_eta()
[Fact] public void Ignores_non_progress_lines()
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test .\tests\KyappuTone.Core.Tests --filter YtDlpOutputParserTests`

- [ ] **Step 3: Implement the parser**

`YouTubeSearchResult` fields: `Id`, `Title`, `Uploader`, `Duration`, `Url`,
`ThumbnailUrl`. Parse the `entries` array from `--dump-single-json --flat-playlist`.
`DownloadProgress` fields: `Percent`, `Speed`, `Eta`.

- [ ] **Step 4: Run the parser tests**

Expected: PASS.

- [ ] **Step 5: Add the DownloadQueue table**

Columns: `Id`, `Url`, `Title`, `TargetFolder`, `Status`, `Progress`, `Error`,
`OutputPath`, `CreatedAtUtc`. Status values: `queued`, `running`, `completed`,
`failed`, `cancelled`. On startup, reset any `running` row to `queued`.

- [ ] **Step 6: Implement YouTubeService**

`SearchAsync(string query)` runs:

```text
yt-dlp --flat-playlist --dump-single-json "ytsearch20:<query>"
```

Use a 30-second timeout, capture stdout/stderr, and return an empty list on failure
with an error message. Do not log full URLs containing tokens.

- [ ] **Step 7: Implement DownloadService**

Run one download at a time:

```text
yt-dlp -x --audio-format mp3 --audio-quality 0 --embed-metadata --embed-thumbnail --newline --progress-template "download:%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s" -o "<folder>\%(title)s [%(id)s].%(ext)s" "<url>"
```

Pass `--ffmpeg-location <path>`. Parse progress lines. On exit code 0, resolve the
output file, optionally import it with `LibraryScanner.ExtractSongMetadata` and
`DatabaseHelper.InsertMultipleSongs`, and mark completed. On failure, store stderr's
last useful line. On cancel, kill the process tree and delete `.part` files.

- [ ] **Step 8: Add settings**

Add `LocalSave.DownloadFolder` (default `Downloads\KyappuTone` under the user's Music
folder if available, otherwise LocalState), `LocalSave.ImportDownloads` default true,
and `LocalSave.DownloadConcurrency` fixed at 1 in this pass.

- [ ] **Step 9: Run tests and build**

- [ ] **Step 10: Commit**

```powershell
git add src/KyappuTone.Core/Acquisition tests/KyappuTone.Core.Tests/Acquisition Common/Services/YouTubeService.cs Common/Services/DownloadService.cs Common/Services/DatabaseHelper.cs Common/Enums.cs Common/Operations/LibraryScanner.cs
git commit -m "feat: add YouTube search and download queue"
```

---

## Task 8: Discover page, YouTube and Downloads tabs

**Files:**
- Create: `Views/DiscoverPage.xaml`
- Create: `Views/DiscoverPage.xaml.cs`
- Modify: `Assets/NavViewMenu/AppData.json`
- Modify: `Views/SettingsPage.xaml`
- Modify: `Views/SettingsPage.xaml.cs`

**Interfaces:**
- Consumes: `YouTubeService`, `DownloadService`, `ToolBootstrapService`.
- Produces: the user-facing search, download, and queue workflow.

- [ ] **Step 1: Add the page shell**

Use a `TabView` with tabs `YouTube`, `Downloads`, and `Radio`. The Radio tab is filled
in Task 10. Show tool status at the top; if yt-dlp is missing, show a Settings link.

- [ ] **Step 2: Implement YouTube search**

Search box, Search button, progress ring, results ListView. Each result shows title,
uploader, duration, and a Download button. The download button opens a folder picker
with the configured default; if cancelled, use the default. Enqueue and switch to the
Downloads tab.

- [ ] **Step 3: Implement the Downloads tab**

Bind to `DownloadService` queue changes. Show title, status, progress bar, error text,
and actions Retry, Cancel, Open folder. Show an empty state. Disable Cancel for
completed/failed rows.

- [ ] **Step 4: Add the settings card**

Settings → Integrations → "Downloads": default folder picker, "Import completed
downloads into the library" toggle, and "Open downloads folder" button.

- [ ] **Step 5: Add navigation**

Add `DiscoverPage` titled `Discover` under Library with a search/globe glyph. Build
once for the generated navigation map.

- [ ] **Step 6: Validate layout on monitor 2**

Launch the app on monitor 2, open Discover, search for a short public query, and
confirm the results list scrolls and the tabs switch without layout errors. Do not
download copyrighted audio during the check; use a public-domain or test URL.

- [ ] **Step 7: Build and commit**

```powershell
git add Views/DiscoverPage.xaml Views/DiscoverPage.xaml.cs Views/SettingsPage.xaml Views/SettingsPage.xaml.cs Assets/NavViewMenu/AppData.json
git commit -m "feat: add Discover search and download UI"
```

---

## Task 9: Radio core and stream playback

**Files:**
- Create: `src/KyappuTone.Core/Radio/RadioBrowserMapper.cs`
- Create: `src/KyappuTone.Core/Radio/IcyMetadataParser.cs`
- Create: `tests/KyappuTone.Core.Tests/Radio/RadioBrowserMapperTests.cs`
- Create: `tests/KyappuTone.Core.Tests/Radio/IcyMetadataParserTests.cs`
- Create: `Common/Services/RadioService.cs`
- Create: `Common/Services/IcyMetadataService.cs`
- Modify: `Common/Services/DatabaseHelper.cs`
- Modify: `Common/Services/MusicPlayer.cs`
- Modify: `ViewModels/MusicControlViewModel.cs`

**Interfaces:**
- Produces: `RadioBrowserMapper.ParseStations(string json) -> IReadOnlyList<RadioStation>`
- Produces: `IcyMetadataParser.TryParseTitle(byte[] metadata) -> string?`
- Produces: `RadioService.SearchAsync(string query)`, `GetFavoritesAsync()`, `ToggleFavoriteAsync(RadioStation)`.
- Produces: `MusicPlayer.LoadStreamAsync(RadioStation station, bool play = true)`.
- Produces: `MusicPlayer.CurrentStream` and `MusicPlayer.StreamTitleChanged`.

- [ ] **Step 1: Write failing radio mapper tests**

Test names:

```csharp
[Fact] public void Parses_station_name_url_tags_and_bitrate()
[Fact] public void Skips_stations_without_a_resolvable_url()
[Fact] public void Prefers_https_url_when_present()
```

- [ ] **Step 2: Write failing ICY tests**

Test names:

```csharp
[Fact] public void Parses_stream_title_from_icy_metadata()
[Fact] public void Returns_null_for_empty_metadata()
[Fact] public void Handles_artist_dash_title_text_verbatim()
```

- [ ] **Step 3: Run and verify failure**

Run: `dotnet test .\tests\KyappuTone.Core.Tests --filter "RadioBrowserMapperTests|IcyMetadataParserTests"`

- [ ] **Step 4: Implement the pure radio helpers**

`RadioStation` fields: `Uuid`, `Name`, `Url`, `FaviconUrl`, `Tags`, `Country`,
`Codec`, `Bitrate`. `ParseStations` accepts `url_resolved` and falls back to `url`;
reject non-HTTP URLs.

- [ ] **Step 5: Run the core tests**

Expected: PASS.

- [ ] **Step 6: Add RadioFavorites table and methods**

Columns: `Uuid TEXT PRIMARY KEY`, `Name`, `Url`, `FaviconUrl`, `Tags`, `Country`,
`Codec`, `Bitrate INTEGER`, `LastPlayedUtc INTEGER`. Add `GetRadioFavorites`,
`UpsertRadioFavorite`, `RemoveRadioFavorite`, `TouchRadioFavorite`.

- [ ] **Step 7: Implement RadioService**

Use `https://de1.api.radio-browser.info/json/stations/search` with `limit=50`,
`hidebroken=true`, and `order=clickcount`. Set a descriptive user agent. Return an
empty list and a status message on failure. Cache the last successful result in memory
for the session.

- [ ] **Step 8: Implement stream mode in MusicPlayer**

Add:

```csharp
public RadioStation? CurrentStream { get; }
public event EventHandler<string>? StreamTitleChanged;
public async Task LoadStreamAsync(RadioStation station, bool play = true);
```

Force the Flyleaf backend, open the station URL, set `CurrentSong` to
`radio://<uuid>`, set `CurrentStream`, and clear the local playlist. Set
`SMTC` metadata to the station name. Do not query the database or TagLib.

Modify `PlaybackSession_MediaOpenedAsync` to branch on `CurrentStream`: set title,
artist, cover, duration 0, hide progress, skip play-count/scrobble, and subscribe to
ICY title updates. `Next`/`Previous` must stop or no-op in stream mode. `Ended` must
not auto-advance to a local playlist.

- [ ] **Step 9: Implement ICY metadata**

`IcyMetadataService.GetCurrentTitleAsync(string url)` opens an HTTP request with
`Icy-MetaData: 1`, reads `icy-metaint`, reads one metadata block, parses
`StreamTitle`, and closes. Run it every 30 seconds while a stream is playing. Ignore
all errors.

- [ ] **Step 10: Run tests and build**

- [ ] **Step 11: Commit**

```powershell
git add src/KyappuTone.Core/Radio tests/KyappuTone.Core.Tests/Radio Common/Services/RadioService.cs Common/Services/IcyMetadataService.cs Common/Services/DatabaseHelper.cs Common/Services/MusicPlayer.cs ViewModels/MusicControlViewModel.cs
git commit -m "feat: add internet radio playback"
```

---

## Task 10: Radio tab

**Files:**
- Modify: `Views/DiscoverPage.xaml`
- Modify: `Views/DiscoverPage.xaml.cs`

**Interfaces:**
- Consumes: `RadioService`, `MusicPlayer.LoadStreamAsync`.

- [ ] **Step 1: Implement the Radio tab**

Search box, Search button, favorites section, station list with name, country, tags,
bitrate, favorite toggle, and Play/Stop. Show the current ICY title when available.
Show "Radio Browser unavailable" on network failure.

- [ ] **Step 2: Wire playback**

Play calls `MusicPlayer.Instance.LoadStreamAsync(station)`. Stop calls
`MusicPlayer.Instance.Pause()` and clears stream state. Do not add radio stations to
local playlists or the download queue.

- [ ] **Step 3: Validate on monitor 2**

Play a known public station for at most 20 seconds, confirm audio starts, the title
shows, progress is hidden, and Stop works. Do not run a soak test.

- [ ] **Step 4: Build and commit**

```powershell
git add Views/DiscoverPage.xaml Views/DiscoverPage.xaml.cs
git commit -m "feat: add radio browser UI"
```

---

## Task 11: Discord Rich Presence

**Files:**
- Create: `src/KyappuTone.Core/Integrations/Discord/DiscordIpcProtocol.cs`
- Create: `tests/KyappuTone.Core.Tests/Integrations/DiscordIpcProtocolTests.cs`
- Create: `Common/Services/DiscordPresenceService.cs`
- Modify: `Common/Enums.cs`
- Modify: `Views/SettingsPage.xaml`
- Modify: `Views/SettingsPage.xaml.cs`
- Modify: `ViewModels/MusicControlViewModel.cs`
- Modify: `App.xaml.cs`

**Interfaces:**
- Produces: `DiscordIpcProtocol.Encode(int opcode, string json) -> byte[]`
- Produces: `DiscordIpcProtocol.TryDecode(ReadOnlySpan<byte> data, out int opcode, out string json) -> bool`
- Produces: `DiscordPresenceService.UpdateTrack(Song? song, bool isPlaying)`, `Clear()`.

- [ ] **Step 1: Write failing framing tests**

Test names:

```csharp
[Fact] public void Encodes_opcode_and_payload_length_little_endian()
[Fact] public void Round_trips_a_handshake_payload()
[Fact] public void Rejects_a_truncated_frame()
```

Expected frame: 4-byte little-endian opcode, 4-byte little-endian JSON byte length,
UTF-8 JSON.

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test .\tests\KyappuTone.Core.Tests --filter DiscordIpcProtocolTests`

- [ ] **Step 3: Implement the protocol helper**

No external package.

- [ ] **Step 4: Run the tests**

Expected: PASS.

- [ ] **Step 5: Implement DiscordPresenceService**

Connect to `\\.\pipe\discord-ipc-0` through `-9`. Send opcode 0 handshake
`{"v":1,"client_id":"<id>"}`. Send opcode 1 `SET_ACTIVITY` with `pid`, `activity`
details, state, timestamps, and instance false. Reconnect on failure with a short
backoff. All failures are silent to playback but available as a status string.

- [ ] **Step 6: Add Settings UI**

Settings → Integrations → "Discord Rich Presence": toggle, application ID text box,
status text, Save. If the ID is empty, presence is disabled. Add instructions to create
a Discord application. Do not call the ID a secret.

- [ ] **Step 7: Hook playback**

On track open, play, pause, resume, and stop, call `UpdateTrack` or `Clear`. Do not
update on every position tick. On app exit, clear presence.

- [ ] **Step 8: Test with and without Discord**

With Discord closed, confirm playback and settings save work. If Discord is available,
test with a user-provided app ID; otherwise record the absence as a manual check.

- [ ] **Step 9: Run tests and build**

- [ ] **Step 10: Commit**

```powershell
git add src/KyappuTone.Core/Integrations/Discord tests/KyappuTone.Core.Tests/Integrations Common/Services/DiscordPresenceService.cs Common/Enums.cs Views/SettingsPage.xaml Views/SettingsPage.xaml.cs ViewModels/MusicControlViewModel.cs App.xaml.cs
git commit -m "feat: add Discord rich presence"
```

---

## Task 12: Interactive validation of smart playlists and covers

**Files:**
- Modify: `docs/reviews/2026-10-20-full-review.md`

**Interfaces:**
- Consumes: the existing smart playlist and cover features.

- [ ] **Step 1: Prepare data**

Use the existing development library. Confirm the database has at least one genre,
artist, and year value. Do not alter user files; work on a copy of one track if tag
changes are needed.

- [ ] **Step 2: Create a smart playlist through the UI**

Launch on monitor 2. Open Add Playlist, enable Smart playlist, set a genre or artist
filter, create it, and verify the playlist row and track count in SQLite.

- [ ] **Step 3: Verify refresh on reopen**

Change a matching track's genre or year in the database, reopen the playlist, and
confirm the membership changes without losing the playlist.

- [ ] **Step 4: Change and remove a custom cover**

Open a user playlist, use Change cover, choose an image on monitor 2, confirm the
header image, then remove it and confirm the fallback image returns.

- [ ] **Step 5: Record results**

Add a short verification entry to the review follow-up with date, playlist names,
track counts, and any failure. Update the follow-up if a bug is fixed.

- [ ] **Step 6: Commit the verification record**

```powershell
git add docs/reviews/2026-10-20-full-review.md
git commit -m "docs: record playlist UI verification"
```

---

## Task 13: Release packaging and signing

**Files:**
- Create: `scripts/package-release.ps1`
- Modify: `docs/release.md`
- Modify: `Package.appxmanifest` only if a real publisher certificate is supplied
- Modify: `KyappuTone.csproj` version if the release version changes

**Interfaces:**
- Produces: `scripts/package-release.ps1 -Platform x64|ARM64 -Version <version> [-CertificatePath <pfx>] [-CertificatePassword <password>] [-CreateDevelopmentCertificate]`.

- [ ] **Step 1: Implement the packaging script**

Validate the version format. Build Release with `AppxPackageSigningEnabled=false` when
no certificate is supplied. With a certificate, validate that the PFX subject exactly
equals the manifest `Publisher` before building, then build with signing enabled.
Write SHA-256 checksums next to each package.

- [ ] **Step 2: Add the development certificate path**

`-CreateDevelopmentCertificate` creates a self-signed certificate for
`CN=KyappuTone Development`, installs it into `CurrentUser\TrustedPeople`, exports a
password-protected PFX under `%LOCALAPPDATA%\KyappuTone\Signing`, and signs local test
packages. It must print that the result is not production-trusted.

- [ ] **Step 3: Build x64 and ARM64**

Run:

```powershell
.\scripts\package-release.ps1 -Platform x64 -Version 0.4.0 -CreateDevelopmentCertificate
.\scripts\package-release.ps1 -Platform ARM64 -Version 0.4.0 -CreateDevelopmentCertificate
```

Expected: both packages exist under `AppPackages`, checksums match, and the signature
is valid for the development certificate.

- [ ] **Step 4: Test installation**

Install the x64 development-signed package on the development machine, launch on
monitor 2, and confirm the database and settings survive. Do not test on a clean VM
without the certificate installed; that would fail by design.

- [ ] **Step 5: Update release docs**

Document the private-token update check, the development-certificate limitation, and
the exact steps to supply a real PFX. Do not claim production signing.

- [ ] **Step 6: Commit**

```powershell
git add scripts/package-release.ps1 docs/release.md Package.appxmanifest KyappuTone.csproj
git commit -m "chore: add release packaging and development signing"
```

---

## Task 14: README, repository metadata, and private push

**Files:**
- Modify: `README.md`
- Modify: `docs/integrations.md`
- Modify: `docs/release.md`
- Add: screenshots under `docs/images/` if useful
- Modify: `.gitignore`

**Interfaces:**
- Produces: a complete private GitHub repository description and README.

- [ ] **Step 1: Scan for personal data**

Run:

```powershell
gitleaks detect --source . --no-git --redact
git grep -n -I -E 'C:\\Users\\|C:\\Tools\\|yoshi|@[A-Za-z0-9.-]+\.[A-Za-z]{2,}|api[_-]?key|client[_-]?secret|password|token' -- ':!bin' ':!obj'
```

Review every hit. Replace local paths with `<repo>` or generic examples. Do not commit
logs, database files, `.superpowers`, or test artifacts.

- [ ] **Step 2: Rewrite the README**

Include:

- product name and one-paragraph description;
- screenshot or app icon;
- feature list by area;
- Windows requirements;
- install from a private build (manual MSIX);
- build instructions;
- Last.fm, Spotify, YouTube/yt-dlp, radio, Discord, and update setup links;
- privacy statement (no telemetry, explicit network calls, local database);
- GPLv3 license and credits to Tunetastic;
- contribution and security notes.

No personal email, local paths, API keys, or private repository tokens.

- [ ] **Step 3: Update integration docs**

Add sections for metadata lookup, yt-dlp/FFmpeg bootstrap, Radio Browser, Discord
application ID, and the private GitHub token. State exactly what is stored where.

- [ ] **Step 4: Build and test once more**

Run Core tests, x64 Debug, x64 Release, and ARM64 Debug. Confirm 0 warnings/0 errors
and no crash log after a short launch.

- [ ] **Step 5: Commit**

```powershell
git add README.md docs .gitignore
git commit -m "docs: write release README and integration guides"
```

- [ ] **Step 6: Push the private repository**

Run:

```powershell
git push origin main
```

Confirm the branch is ahead/up to date and the repository remains private.

- [ ] **Step 7: Set GitHub description and topics**

Use the GitHub API/MCP to set:

- Description: "A modern, local-first music player for Windows 11 with playlists, metadata enrichment, downloads, radio, and Discord presence."
- Topics: `windows`, `winui3`, `music-player`, `dotnet`, `csharp`, `sqlite`, `ffmpeg`, `yt-dlp`, `radio`, `discord-rpc`.

Do not publish a release or make the repository public.

---

## Task 15: Final verification

**Files:** none.

- [ ] **Step 1: Run the full verification**

```powershell
dotnet test .\tests\KyappuTone.Core.Tests
dotnet build .\KyappuTone.sln -p:Platform=x64 -c Debug -p:AppxPackageSigningEnabled=false
dotnet build .\KyappuTone.sln -p:Platform=x64 -c Release -p:AppxPackageSigningEnabled=false
dotnet build .\KyappuTone.sln -p:Platform=ARM64 -c Debug -p:AppxPackageSigningEnabled=false
```

Expected: all tests pass; every build has 0 warnings and 0 errors.

- [ ] **Step 2: Launch on monitor 2**

Open the app, confirm the database loads, open Folders, Metadata, Discover, YouTube,
Downloads, and Radio, and check the crash log path. Do not run tone playback.

- [ ] **Step 3: Confirm repository cleanliness**

```powershell
git status --short --branch
git ls-files | Select-String -Pattern '(?i)(\.pfx$|\.p12$|\.db3$|crash\.log|LocalState)'
```

Expected: no unexpected untracked files and no secrets.

- [ ] **Step 4: Report**

Summarize completed features, test counts, build results, signing limitation, and any
manual checks that still require the user's credentials or hardware.
