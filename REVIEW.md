# KyappuTone Phase 0 — Code Review

**Base:** 1c3438f  **Head:** f3ce328  **Date:** 2026-10-20

---

## Strengths

- **Clean mechanical rebrand** — Zero stale `Tunetastic` identifiers in source (verified by the plan's grep check). Namespaces, project/solution files, manifest identity, and UI strings all consistently renamed to `KyappuTone`.
- **ImageSharp removed, SkiaSharp pipeline added** — `CoverArtProcessor` is a focused, well-tested library. Unit tests cover resize, re-encode, and garbage-input rejection (5/5 pass). The fallback to `Assets/AppIcon.png` on `ArgumentException` satisfies Review Focus #1.
- **CI pipeline works** — GitHub Actions matrix builds x64 + ARM64, runs tests on x64, uploads unsigned MSIX artifact. Run 39395337086 succeeded.
- **Two runtime crashes fixed** — `SettingsPage.UpdateExtentionListOnUI` now marshals via `DispatcherQueue` (RPC_E_WRONG_THREAD); `MainPage.CheckForUpdate` catches `0x803FB112` on sideloaded packages. Both verified by manual launch.
- **NOTICE and LICENSE correct** — GPLv3 preserved byte-identical; NOTICE credits Tunetastic authors and upstream commit.
- **Fixture script and smoke checklist exist** — `scripts/make-fixtures.ps1` generates test audio with non-ASCII filenames, cover art, and corrupt image; `scripts/smoke-checklist.md` enumerates all Review Focus items.
- **Tests pass** — `dotnet test KyappuTone.Core.Tests` → 5/5 green.
- **Build succeeds** — Clean Debug x64 build (3 warnings: NU1510×2, CS8604×1) after killing stray process.

---

## Issues

### Critical (Must Fix)

#### 1. Long paths (>260 chars) not handled in LibraryScanner
**File:** `Common/Operations/LibraryScanner.cs` (lines 127–134, 160–187)
**What's wrong:** `Directory.EnumerateFiles` and `TagLib.File.Create` throw `PathTooLongException` on paths exceeding `MAX_PATH` (260 chars) when long-path support isn't opted in. The scan aborts instead of skipping the file with a log entry.
**Why it matters:** Review Focus #4 explicitly requires "skipped with a log entry rather than throwing out of the scan." Windows 10 1903+ supports long paths via manifest or registry, but the app doesn't opt in and doesn't catch the exception.
**How to fix:**
- Add `<ApplicationManifest>app.manifest</ApplicationManifest>` with `<longPathAware>true</longPathAware>` (already present in `app.manifest`? verify).
- Wrap `Directory.EnumerateFiles` and `TagLib.File.Create` in `try/catch (PathTooLongException)`, log via `GlobalNotification.Error` / `failedFiles.Add`, continue.
- Consider using `\\?\` extended-length path prefix for file I/O.

#### 2. Locked files during scan not gracefully skipped
**File:** `Common/Operations/LibraryScanner.cs` (lines 160–187, 244–331)
**What's wrong:** `ExtractSongMetadata` catches general `Exception` and builds a fallback song, but `TagLib.File.Create` on a locked file throws `IOException` (sharing violation). The catch block swallows it and returns a partial song with `succeeded=false`, which *is* added to `failedFiles` and later shown via `GlobalNotification.Error`. However:
- The fallback path uses `StorageFile.GetFileFromPathAsync` which *also* fails on locked files.
- No specific handling/logging for "file in use" vs other errors.
- Review Focus #3 expects "skipped and logged, not abort the run" — the scan continues, but the error surface is a generic "Failed to read metadata for:" notification, not a clear "file locked" message.
**Why it matters:** Users with OneDrive, antivirus, or another player holding a file get a confusing error instead of a clear "skipped: file in use" log entry.
**How to fix:** In `ExtractSongMetadata`, catch `IOException` / `UnauthorizedAccessException` specifically, check `HResult` for sharing violation (0x80070020), log a distinct "File in use: {path}" message, return `(fallbackSong, false)`.

#### 3. ImageResizer fallback reads `AppIcon.png` on every call — no caching, no null-check
**File:** `Common/Helpers/ImageResizer.cs` (lines 61–65, 77–85)
**What's wrong:** `ProcessWithFallback` reads `Assets/AppIcon.png` from disk *every time* a thumbnail is created. If the file is missing (e.g., packaging issue), `File.ReadAllBytes` throws `FileNotFoundException` which is *not* caught (only `ArgumentException` from the processor is caught). The app crashes.
**Why it matters:** Review Focus #1 requires "never a zero-byte file or an unhandled exception." A missing fallback asset is a single point of failure for all cover art.
**How to fix:**
- Cache the fallback bytes in a `static readonly byte[]` initialized once at startup (or lazy init with proper error handling).
- If `AppIcon.png` is missing, embed a minimal 1×1 transparent PNG as a `byte[]` literal in the assembly as last resort.

#### 4. Random filename generator uses `new Random()` per call — collision risk under load
**File:** `Common/Helpers/ImageResizer.cs` (line 90)
**What's wrong:** `RandomCoverFileName()` creates a new `Random()` instance each call. In tight loops (parallel scan), multiple calls within the same millisecond produce identical seeds → identical filenames → `File.WriteAllBytes` overwrites previous thumbnail.
**Why it matters:** Parallel scan (DOP=4–8) generates thumbnails concurrently. Collisions lose cover art silently.
**How to fix:** Use `static readonly Random _rng = new();` or `Random.Shared` (.NET 6+).

---

### Important (Should Fix)

#### 5. No long-path opt-in in `app.manifest`
**File:** `app.manifest`
**What's wrong:** The manifest doesn't contain `<longPathAware>true</longPathAware>` inside `<application>`. Without it, `Directory.EnumerateFiles` throws on paths > 260 chars even on Windows 10 1903+.
**Why it matters:** Prerequisite for fixing Issue #1.
**How to fix:** Add to `app.manifest`:
```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <longPathAware xmlns="http://schemas.microsoft.com/SMI/2019/WindowsSettings">true</longPathAware>
  </windowsSettings>
</application>
```

#### 6. `CoverArtProcessor.Decode` throws on empty byte array but `ImageResizer` passes fallback *after* catch
**File:** `src/KyappuTone.Core/Imaging/CoverArtProcessor.cs` (line 37) vs `Common/Helpers/ImageResizer.cs` (line 79)
**What's wrong:** `Decode` throws `ArgumentException` for `source.Length == 0`. `ProcessWithFallback` catches `ArgumentException` from the *processor* and retries with fallback. But if `pictures[0].Data.Data` is empty, the first `process(imageData)` throws, the catch retries with fallback — this works. However, if `AppIcon.png` itself is empty/corrupt, the retry *also* throws and escapes uncaught.
**Why it matters:** Double-failure scenario crashes the thumbnail pipeline.
**How to fix:** In `ProcessWithFallback`, wrap the fallback retry in its own `try/catch` and, on failure, return a minimal embedded PNG byte array (1×1 transparent) instead of throwing.

#### 7. `LibraryScanner.ScanLibraries` deletes *entire* Thumbnails folder on every scan
**File:** `Common/Operations/LibraryScanner.cs` (lines 114–115)
**What's wrong:** `Directory.Delete(path, true)` wipes all thumbnails, forcing full regeneration. For large libraries this is I/O waste and loses thumbnails for tracks not re-scanned (incremental scan not implemented in Phase 0).
**Why it matters:** Performance regression on re-scan; not a correctness issue but notable.
**How to fix:** Defer to Phase 1 incremental scan work. Document as known limitation.

#### 8. CI workflow doesn't fail on test failures (implicit pass)
**File:** `.github/workflows/build.yml` (line 26)
**What's wrong:** `dotnet test` step has no explicit `|| exit 1` but GitHub Actions fails the step on non-zero exit by default. However, the `if: matrix.platform == 'x64'` means ARM64 leg doesn't run tests — acceptable per plan, but should be explicit.
**Why it matters:** Clarity; ARM64 test skip is intentional but not documented in the workflow.
**How to fix:** Add comment `# Tests only on x64 per Phase 0 plan; ARM64 build-only`.

#### 9. `make-fixtures.ps1` depends on system `ffmpeg` in PATH; repo ships FFmpeg DLLs only
**File:** `scripts/make-fixtures.ps1` (line 6)
**What's wrong:** The plan's Step 1 says "If `ffmpeg.exe` or the `libmp3lame` encoder is absent from `Assets/FFmpeg`, replace the mp3 line with a second flac file and note the substitution." The script uses `Get-Command ffmpeg` (PATH) and `libmp3lame` encoder. The repo's `Assets/FFmpeg` has DLLs only, no `ffmpeg.exe`. The script will throw if ffmpeg isn't on PATH.
**Why it matters:** Reproducibility — a fresh checkout on a machine without ffmpeg in PATH fails the fixture generation.
**How to fix:** Either bundle `ffmpeg.exe` in `Assets/FFmpeg` (LGPL compliant) or change script to generate only FLAC/AAC (no mp3) and document the substitution as the plan instructs.

#### 10. `Package.appxmanifest` `MaxVersionTested` stuck at 19041
**File:** `Package.appxmanifest` (lines 34–35)
**What's wrong:** `MaxVersionTested="10.0.19041.0"` (Windows 10 2004). Current OS is 26300. Not blocking, but signals untested-on-newer-OS.
**Why it matters:** Store certification may flag; Windows may apply compat shims.
**How to fix:** Update to `10.0.26100.0` (matching SDK) or current build.

---

### Minor (Nice to Have)

#### 11. `System.Private.Uri` PackageReference unnecessary (NU1510 warning)
**File:** `KyappuTone.csproj` (line 215)
**What's wrong:** Package is part of the shared framework; explicit reference triggers NU1510.
**How to fix:** Remove the `<PackageReference Include="System.Private.Uri" ... />` line.

#### 12. CS8604 nullable warning in `MainPlayerPage.xaml.cs:818`
**File:** `Views/MainPlayerPage.xaml.cs` (line 818)
**What's wrong:** `Clipboard.SetText(text)` where `text` may be null.
**How to fix:** Guard with `if (text != null) Clipboard.SetText(text);`.

#### 13. `ImageResizer.RandomCoverFileName` allocates per call (string builder + LINQ)
**File:** `Common/Helpers/ImageResizer.cs` (line 90)
**What's wrong:** Minor allocation churn in hot path (thumbnail generation).
**How to fix:** Use `Random.Shared.NextBytes` + base64/hex encoding, or `Guid.NewGuid().ToString("N")[..10]`.

#### 14. Smoke checklist items for playback/shell integration marked "Not verified" in baseline
**File:** `docs/perf/2026-10-20-phase0-baseline.md` (lines 47–51)
**What's wrong:** Playback, tray, thumbnail toolbar, taskbar overlay, SMTC, lock screen, lyrics, tag editor, playlists, export, locked file, long path, corrupt art, unsigned MSIX install — all "Not verified."
**Why it matters:** Phase 0 exit criteria: "app builds with no license files, launches, scans and plays a library, all existing shell integration still works." The baseline admits shell integration is unverified.
**How to fix:** Run the checklist manually (requires Developer Mode for MSIX install) and update baseline with actual results. This is a *process* gap, not a code bug.

#### 15. `.gitignore` missing `bin/`, `obj/` for the new `src/` and `tests/` projects
**File:** `.gitignore` (only 2 lines added: `AppPackages/`, `*.pfx`, `*.cer`, `sixlabors.lic`)
**What's wrong:** The root `.gitignore` from Tunetastic may not cover `src/KyappuTone.Core/bin`, `obj`, `tests/.../bin`, `obj`.
**How to fix:** Add `src/**/bin/`, `src/**/obj/`, `tests/**/bin/`, `tests/**/obj/` to `.gitignore`.

---

## Declined to Judge (outside plan/spec)

| Behavior | Reason |
|----------|--------|
| Audio engine (Flyleaf) behavior, gapless, EQ, visualizers | Phase 1 scope per spec §11 |
| Incremental/auto-scan, rename detection | Phase 2 scope |
| YouTube search/download, Spotify import, radio | Phase 3 scope |
| Command palette, mini player, themes, Discord, updater | Phase 4 scope |
| Localization / non-English UI | Spec §8.7: English-only for v1 |
| Crash reporting / telemetry | Spec §12.9: none in v1 |
| CD ripping, DRM removal | Spec §4: non-goals |
| ARM64 runtime testing (CI builds but doesn't run) | Plan Task 5 ruling: tests skipped on ARM64 leg |

---

## Recommendations

1. **Fix the four Critical issues before merge** — they directly violate Review Focus requirements.
2. **Run the smoke checklist manually** with Developer Mode enabled to verify shell integration (tray, SMTC, taskbar overlay, lock screen) and edge cases (locked file, long path, corrupt art). Update `docs/perf/2026-10-20-phase0-baseline.md` with real results.
3. **Add long-path manifest opt-in** (`app.manifest`) — unblocks Issue #1 fix.
4. **Cache `AppIcon.png` bytes** in `ImageResizer` with embedded ultimate fallback — eliminates missing-asset crash.
5. **Use `Random.Shared`** for filename generation — eliminates collision risk.
6. **Consider `dotnet build -p:Platform=ARM64` locally** before push — CI passes but local ARM64 build unverified.
7. **Document the `AppPackages/` output exclusion** in `.gitignore` for the new project structure.

---

## Assessment

**Ready to merge?** **No — With fixes**

**Reasoning:** The implementation correctly delivers the Phase 0 plan: rebranded repo, .NET 10 retarget, ImageSharp→SkiaSharp migration with tests, CI, fixtures, and baseline. Two runtime crashes were fixed. However, **four Critical issues** violate the explicit Review Focus requirements (long paths, locked files, missing fallback asset crash, thumbnail collision). These must be fixed before the tag `v0.0.1-alpha` represents a shippable foundation. The Important issues (#5–#10) should be addressed in the same PR for completeness. The Minor items can follow up.