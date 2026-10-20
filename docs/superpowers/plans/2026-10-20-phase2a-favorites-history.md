# Phase 2A: Favorites, Listening History and Statistics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persisted favorites, event-level listening history, and testable listening statistics as the data foundation for the Phase 2 library-intelligence surfaces.

**Architecture:** SQLite schema grows by additive migrations only (`Songs.Favorite`, a new `PlayHistory` table) so existing libraries survive. Pure aggregation lives in `KyappuTone.Core.Library` and is unit-tested; the app records history at the existing play-count threshold and exposes a favorite toggle in the player. Library-page filtering and the statistics UI build on these APIs in the next plan.

**Tech Stack:** .NET 10, C# 14, SQLite via sqlite-net-pcl (existing), xUnit + Shouldly for Core tests.

**Spec:** `docs/superpowers/specs/2026-10-20-merged-music-player-design.md` (§10.2 Library and statistics, §11 Phase 2)

## Global Constraints

- Windows only; targets `net10.0-windows10.0.26100.0`.
- No new paid dependencies; existing package versions stay pinned.
- All text files in the working tree are CRLF (`.gitattributes` enforces `eol=crlf`).
- UI text is plain English.
- Schema changes must be additive and idempotent: `CREATE TABLE IF NOT EXISTS` / `AddColumnIfMissing`, no destructive migration.
- Rescan must preserve user data: `Favorite`, `PlayCount`, `DateLastPlayed` and history rows survive an upsert or a library refresh.
- Dates are stored in UTC; display and heatmap bucketing use the local time zone.

## Review Focus

- **Rescan wipes favorites** — re-importing an existing file must keep its favorite flag; the upsert path is the likely culprit.
- **History rows die on rescan** — `ON DELETE CASCADE` must not fire for a normal upsert; only a real delete removes rows.
- **Duplicate history entries** — pausing/resuming or crossing the threshold twice must not add two completed rows for one play.
- **Timezone drift** — an entry at 23:30 local must count on that local date and hour, not the UTC date.
- **Empty or tiny history** — statistics must return zeros, not divide by zero or throw, when no rows exist.

---

### Task 1: Schema and repository APIs for favorites and history

**Files:**
- Modify: `Common/Models.cs`
- Modify: `Common/Services/DatabaseHelper.cs`
- Verify manually against `%LOCALAPPDATA%\Packages\YoshKoz.KyappuTone_cdmdvc1szkdwa\LocalState\KyappuTone.db3`

**Interfaces:**
- Consumes: existing `SQLiteAsyncConnection _database`, `AddColumnIfMissing`, upsert paths.
- Produces:
  - `Song.Favorite` (`bool`)
  - `PlayHistoryRow { int Id; string Path; DateTime StartedAt; long MsPlayed; bool Completed; }`
  - `Task SetFavorite(string songPath, bool favorite)`
  - `Task<bool> IsFavorite(string songPath)`
  - `Task<List<Song>> GetFavoriteSongs()`
  - `Task AddPlayHistory(string songPath, DateTime startedAtUtc, long msPlayed, bool completed)`
  - `Task<List<PlayHistoryRow>> GetPlayHistory(DateTime? sinceUtc = null, int limit = 0)`

- [ ] **Step 1: Add the additive schema**

In `DatabaseHelper` initialization, after the `Songs` `AddColumnIfMissing("Songs", "Track", ...)` call:

- Add `Favorite` with `AddColumnIfMissing("Songs", "Favorite", "INTEGER NOT NULL DEFAULT 0")`.
- Create `PlayHistory(Id INTEGER PRIMARY KEY AUTOINCREMENT, Path TEXT NOT NULL, StartedAt DATETIME NOT NULL, MsPlayed INTEGER NOT NULL DEFAULT 0, Completed INTEGER NOT NULL DEFAULT 0)` with `FOREIGN KEY (Path) REFERENCES Songs(Path) ON DELETE CASCADE`.
- Create indexes `idx_PlayHistory_StartedAt` and `idx_PlayHistory_Path`.

- [ ] **Step 2: Preserve favorites and history across rescans**

In both song upsert SQL paths, add `Favorite` to the insert column list and to the `DO UPDATE SET` clause. Preserve the stored value exactly like `PlayCount`:

- insert value: the existing `Song.Favorite` for new rows, or the value read from `SELECT Path, PlayCount, DateLastPlayed, Favorite` for existing rows;
- update expression: `Favorite = excluded.Favorite` is wrong for rescans; use the preserved existing value (same pattern as `PlayCount`).

- [ ] **Step 3: Implement the repository methods**

`SetFavorite` runs `UPDATE Songs SET Favorite = ? WHERE Path = ?` and invokes a new `public static event Action? OnFavoriteUpdated`.
`IsFavorite` runs `SELECT Favorite FROM Songs WHERE Path = ?` and returns false when missing.
`GetFavoriteSongs` runs `SELECT * FROM Songs WHERE Favorite = 1 ORDER BY Title COLLATE NOCASE ASC`.
`AddPlayHistory` inserts a row.
`GetPlayHistory` selects ordered by `StartedAt DESC`, applies the optional `>= sinceUtc` filter, and `LIMIT`s when `limit > 0`.

- [ ] **Step 4: Verify the migration on a copy of the real database**

Close the app. Copy `KyappuTone.db3` to `%TEMP%`, run the app once against the real DB, then query with `sqlite3`:

```sql
PRAGMA table_info(Songs);
SELECT name FROM sqlite_master WHERE type='table' AND name='PlayHistory';
```

Expected: `Favorite` column present, `PlayHistory` table and both indexes present. Mark favorites on two rows, rescan, and confirm both survive.

- [ ] **Step 5: Commit**

```bash
git add Common/Models.cs Common/Services/DatabaseHelper.cs
git commit -m "feat(library): persist favorites and listening history"
```

---

### Task 2: Listening statistics aggregation

**Files:**
- Create: `src/KyappuTone.Core/Library/PlayHistoryEntry.cs`
- Create: `src/KyappuTone.Core/Library/ListeningStats.cs`
- Test: `tests/KyappuTone.Core.Tests/Library/ListeningStatsTests.cs`

**Interfaces:**
- Consumes: nothing outside Core.
- Produces:
  - `record PlayHistoryEntry(string Path, DateTimeOffset StartedAt, long MsPlayed, bool Completed)`
  - `record DailyListening(DateOnly Date, TimeSpan Listened, int Plays)`
  - `record HourlyListening(DayOfWeek Day, int Hour, TimeSpan Listened)`
  - `ListeningStats.Compute(IEnumerable<PlayHistoryEntry> entries, DateTimeOffset now, TimeZoneInfo zone, int days = 30)`
  - `ListeningStats { int TotalPlays; TimeSpan TotalListening; IReadOnlyList<DailyListening> Daily; IReadOnlyList<HourlyListening> Hourly; }`

- [ ] **Step 1: Write the failing tests**

Cover: totals sum `MsPlayed`; only `Completed` entries count as plays; entries older than `days` are excluded; a 23:30 local entry lands on the correct local `DateOnly` and hour even when UTC is the next day; empty input returns zeros and 30 daily buckets.

- [ ] **Step 2: Run the tests and verify they fail**

Run: `dotnet test .\tests\KyappuTone.Core.Tests --filter FullyQualifiedName~ListeningStats`
Expected: compile error because `ListeningStats` does not exist.

- [ ] **Step 3: Implement the aggregation**

`Compute` converts each `StartedAt` with `TimeZoneInfo.ConvertTime`, filters by `localNow.Date - (days - 1)`, sums milliseconds, counts completed entries per local date and per `(DayOfWeek, Hour)`, and returns one `DailyListening` per day in ascending order including zero days.

- [ ] **Step 4: Run the tests and verify they pass**

Run: `dotnet test .\tests\KyappuTone.Core.Tests --filter FullyQualifiedName~ListeningStats`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/KyappuTone.Core/Library tests/KyappuTone.Core.Tests/Library
git commit -m "feat(stats): add listening statistics aggregation"
```

---

### Task 3: Record history at the play-count threshold

**Files:**
- Modify: `ViewModels/MusicControlViewModel.cs` (midpoint timer, around the existing `IncrementPlayCount` call)

**Interfaces:**
- Consumes: `DatabaseHelper.AddPlayHistory`, `PlaybackTracker.GetTotalPlayTime()`.
- Produces: one completed history row per counted play.

- [ ] **Step 1: Record the row once**

In `MidpointTimer_Tick`, after `UpdateDateLastPlayed`, call:

```csharp
var played = _playbackTracker.GetTotalPlayTime();
await DatabaseHelper.Instance.AddPlayHistory(
    _musicPlayer.CurrentSong,
    DateTime.UtcNow - played,
    (long)played.TotalMilliseconds,
    completed: true);
```

The existing `AlreadyCounted` guard is the once-per-play gate; do not add a second flag.

- [ ] **Step 2: Verify manually**

Play a track past 60 % of its duration, then query:

```sql
SELECT Path, StartedAt, MsPlayed, Completed FROM PlayHistory ORDER BY Id DESC LIMIT 5;
```

Expected: exactly one row for that play, `Completed = 1`, `MsPlayed` ≥ 60 % of the track duration in milliseconds. Pause and resume; no extra row appears.

- [ ] **Step 3: Commit**

```bash
git add ViewModels/MusicControlViewModel.cs
git commit -m "feat(stats): record listening history once per play"
```

---

### Task 4: Favorite toggle for the current track

**Files:**
- Modify: `Views/MainPlayerPage.xaml` (button strip containing `MusicInfoButton` and `ShowLyricsButton`)
- Modify: `Views/MainPlayerPage.xaml.cs`

**Interfaces:**
- Consumes: `DatabaseHelper.SetFavorite`, `DatabaseHelper.IsFavorite`, `Song.Favorite`.
- Produces: `FavoriteButton` with glyph `E734` (outline heart) / `E735` (filled heart).

- [ ] **Step 1: Add the button**

Add a 40×40 subtle button named `FavoriteButton` above `MusicInfoButton`, with `Click="FavoriteButton_Click"`, tooltip "Favorite", and a `FontIcon` named `FavoriteGlyph` using `&#xE734;`.

- [ ] **Step 2: Wire state on track load**

In the track-info method that already loads `track` for the player (`UpdateSongInfo`/`PlaybackSession_MediaOpenedAsync` path), after the track is known:

```csharp
var favorite = await DatabaseHelper.Instance.IsFavorite(songPath);
_favoriteSongPath = songPath;
FavoriteGlyph.Glyph = favorite ? "\uE735" : "\uE734";
ToolTipService.SetToolTip(FavoriteButton, favorite ? "Remove from favorites" : "Favorite");
```

Store the path so a click cannot toggle a stale song.

- [ ] **Step 3: Toggle and persist**

```csharp
private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
{
    if (string.IsNullOrEmpty(_favoriteSongPath)) return;
    var favorite = !await DatabaseHelper.Instance.IsFavorite(_favoriteSongPath);
    await DatabaseHelper.Instance.SetFavorite(_favoriteSongPath, favorite);
    FavoriteGlyph.Glyph = favorite ? "\uE735" : "\uE734";
    ToolTipService.SetToolTip(FavoriteButton, favorite ? "Remove from favorites" : "Favorite");
}
```

- [ ] **Step 4: Verify manually**

Favorite a track, restart the app, reopen the track: filled heart. Unfavorite: outline heart. Rescan the library: the flag survives.

- [ ] **Step 5: Commit**

```bash
git add Views/MainPlayerPage.xaml Views/MainPlayerPage.xaml.cs
git commit -m "feat(library): add favorite toggle to the player"
```

---

## Self-review notes

- Favorites filtering on library pages, the statistics/heatmap UI, and the home overview are deliberately deferred to Phase 2B; this plan ends at a usable, persisted favorite + a correct history feed.
- `PlayHistory` uses `Path` rather than `Songs.Id` because the existing schema, playlist tables, and player APIs all key on `Path`.
- Manual database verification is required because `DatabaseHelper` lives in the WinUI app project and depends on `Windows.Storage`.
