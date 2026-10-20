# KyappuTone Phase 0 — Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the private KyappuTone repository as a rebranded, license-clean, .NET 10 build of the Tunetastic codebase that still builds, launches, scans, and plays.

**Architecture:** Seed a fresh private repo from the existing Tunetastic clone, retarget it to .NET 10 and Windows SDK 28000, then perform a mechanical identity rename. A new pure .NET library `KyappuTone.Core` takes over cover-art processing on SkiaSharp so the SixLabors ImageSharp package and its license file requirement disappear. CI builds both architectures and runs the first unit tests.

**Tech Stack:** .NET 10 (C# 14), WinUI 3 / Windows App SDK 2.5.1, MSIX, SkiaSharp 4.153.1, xUnit, Shouldly, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-20-merged-music-player-design.md`

## Global Constraints

- Windows only. Targets `x64` and `ARM64`; minimum OS `10.0.18362.0`.
- .NET SDK `10.0.402`; `TargetFramework` `net10.0-windows10.0.28000.0`; C# `14` (`LangVersion` `Latest`).
- Windows App SDK `2.5.1`; Windows SDK build tools `10.0.28000.2705`.
- License is GPLv3. `LICENSE` stays byte-identical. `NOTICE` credits the Tunetastic authors.
- No Shiranami code, assets, screenshots, or copy may enter the repository.
- The build must succeed with **no** `sixlabors.lic` file and no paid license of any kind.
- Repository `YoshKoz/KyappuTone` stays **private** until a release is published.
- Local checkout path: `C:\Tools\KyappuTone`.
- English-only UI strings. No localization framework work in this phase.
- Single process only. No helper service, no daemon, no background agent.

## Review Focus

Phase 0 touches identity, build, and cover art. These are the inputs most likely to break silently:

1. A track with no embedded pictures, or with a picture whose bytes are not a valid image — must fall back to `Assets/AppIcon.png` and produce a readable PNG, never a zero-byte file or an unhandled exception.
2. Non-ASCII and emoji in tags and folder names (Japanese, Polish diacritics) — must survive scan, DB insert, and display after the rename.
3. A file locked by another process during scan/playback — must be skipped and logged, not abort the run.
4. A path longer than 260 characters — must be skipped with a log entry rather than throwing out of the scan.
5. Installing the renamed MSIX over an older Tunetastic package — must not be treated as an upgrade of the same identity; both can coexist until the old one is removed manually.

## Task 1: Seed the private repository

**Files:**
- Create: `C:\Tools\KyappuTone\` (whole tree)
- Create: `NOTICE`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: existing clone at `C:\Users\yoshi\Tunetastic`
- Produces: private `YoshKoz/KyappuTone` repo with `main` at the seed commit

- [ ] **Step 1: Confirm the source clone has no uncommitted app changes**

Run:
```powershell
git -C C:\Users\yoshi\Tunetastic status --porcelain
```
Expected: only `docs/superpowers/specs/2026-10-20-merged-music-player-design.md` (untracked). Any other entry means stop and ask before copying.

- [ ] **Step 2: Copy the working tree without build or Git internals**

Run:
```powershell
robocopy C:\Users\yoshi\Tunetastic C:\Tools\KyappuTone /E /XD .git obj bin AppPackages /XF *.user /NFL /NDL /NJH /NJS
```
Expected: exit code `0`–`7` (robocopy treats copies as success). Exit code `8` or higher means failure.

- [ ] **Step 3: Initialise Git and add the attribution notice**

Run:
```powershell
Set-Location C:\Tools\KyappuTone
git init -b main
```

Create `NOTICE` with exactly:

```
KyappuTone
Copyright (C) 2026 Yoshi Tacke

This program is free software: you can redistribute it and/or modify it under
the terms of the GNU General Public License as published by the Free Software
Foundation, either version 3 of the License, or (at your option) any later
version.

KyappuTone is derived from Tunetastic (https://github.com/AMit-KP/Tunetastic),
Copyright (C) the Tunetastic contributors, licensed under GPLv3. The user
interface, project identity, package identity, and image pipeline have been
modified. Upstream source: https://github.com/AMit-KP/Tunetastic at commit
79bc3cb.

This program is distributed in the hope that it will be useful, but WITHOUT
ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
```

- [ ] **Step 4: Extend `.gitignore` for local build artefacts**

Append to the existing `.gitignore`:

```
AppPackages/
*.pfx
*.cer
sixlabors.lic
```

- [ ] **Step 5: Commit the seed**

Run:
```powershell
git add -A
git commit -m "chore: seed KyappuTone from Tunetastic (GPLv3)"
```
Expected: commit created, `git log --oneline -1` shows the message.

- [ ] **Step 6: Create the private GitHub repository and push**

Run:
```powershell
gh repo create YoshKoz/KyappuTone --private --source . --remote origin --push
```
Expected: repository URL printed and push succeeds.

- [ ] **Step 7: Verify privacy and remote**

Run:
```powershell
gh repo view YoshKoz/KyappuTone --json isPrivate,visibility
git remote -v
```
Expected: `"isPrivate": true`, `"visibility": "PRIVATE"`, and `origin` pointing at `github.com/YoshKoz/KyappuTone`.

## Task 2: Retarget to .NET 10 and clean build configuration

**Files:**
- Modify: `Tunetastic.csproj` (renamed in Task 3)
- Test: build output and runtime launch

**Interfaces:**
- Consumes: seeded repo from Task 1
- Produces: a Debug build that runs on `net10.0-windows10.0.28000.0` with no hardcoded machine paths

- [ ] **Step 1: Record the current baseline build failure or success**

Run:
```powershell
dotnet build .\Tunetastic.csproj -p:Platform=x64 -c Debug -p:AppxPackageSigningEnabled=false -v:m
```
Expected: SUCCESS on .NET 9, or FAIL with messages naming missing SDK/targeting packs. Save the first error line; it is the comparison point for Step 3.

- [ ] **Step 2: Retarget the project properties**

In `Tunetastic.csproj` replace:
```xml
<TargetFramework>net9.0-windows10.0.26100.0</TargetFramework>
```
with:
```xml
<TargetFramework>net10.0-windows10.0.28000.0</TargetFramework>
```

Keep `TargetPlatformMinVersion` at `10.0.18362.0`.

Replace the hardcoded package directory:
```xml
<AppxPackageDir>C:\Users\amitp\source\repos\Tunetastic\AppPackages\</AppxPackageDir>
```
with:
```xml
<AppxPackageDir>$(MSBuildThisFileDirectory)AppPackages\</AppxPackageDir>
```

Set signing off by default:
```xml
<AppxPackageSigningEnabled>false</AppxPackageSigningEnabled>
```
and delete the `<PackageCertificateThumbprint>` element.

- [ ] **Step 3: Build and capture warnings**

Run:
```powershell
dotnet build .\Tunetastic.csproj -p:Platform=x64 -c Debug -p:AppxPackageSigningEnabled=false -v:m 2>&1 | Tee-Object -FilePath C:\Tools\KyappuTone\build-net10.log
```
Expected: `Build succeeded`, `0 Error(s)`. Record the warning count from the last line; it becomes the ceiling for later tasks.

- [ ] **Step 4: Launch the built app**

Run:
```powershell
$exe = Get-ChildItem .\bin\x64\Debug\net10.0-windows10.0.28000.0\Tunetastic.exe -ErrorAction SilentlyContinue
if (-not $exe) { Get-ChildItem .\bin -Recurse -Filter Tunetastic.exe | Select-Object -First 1 -ExpandProperty FullName }
```
Expected: an executable path is printed. Launch it, confirm the window appears, close it. If launch fails, note the exception text in `build-net10.log` before continuing.

- [ ] **Step 5: Commit**

```powershell
git add Tunetastic.csproj .gitignore
git commit -m "build: retarget to .NET 10 and Windows SDK 28000"
```

## Task 3: Rebrand identity, namespaces, and package manifest

**Files:**
- Modify: all `*.cs`, `*.xaml`, `*.sln`, `*.csproj`, `*.appxmanifest`, `Assets/NavViewMenu/AppData.json`
- Rename: `Tunetastic.csproj` → `KyappuTone.csproj`, `Tunetastic.sln` → `KyappuTone.sln`
- Test: build + launch + grep for stale names

**Interfaces:**
- Consumes: retargeted project from Task 2
- Produces: root namespace `KyappuTone`, startup object `KyappuTone.Startup`, MSIX identity `YoshKoz.KyappuTone`

- [ ] **Step 1: Write the stale-name check (failing test)**

Run:
```powershell
Get-ChildItem . -Recurse -File -Include *.cs,*.xaml,*.csproj,*.sln,*.appxmanifest,*.json,*.xml,*.tt |
  Where-Object { $_.FullName -notmatch '\\\.git\\|\\obj\\|\\bin\\|\\AppPackages\\|\\docs\\superpowers\\' } |
  Select-String -Pattern 'Tunetastic' |
  Measure-Object
```
Expected: a count greater than zero (currently 86 C# files, 23 XAML files, plus manifest and solution). This count must become zero by Step 6.

- [ ] **Step 2: Replace the identifier everywhere**

Run:
```powershell
$files = Get-ChildItem . -Recurse -File -Include *.cs,*.xaml,*.csproj,*.sln,*.appxmanifest,*.json,*.xml,*.tt,*.md |
  Where-Object { $_.FullName -notmatch '\\\.git\\|\\obj\\|\\bin\\|\\AppPackages\\|\\docs\\superpowers\\|\\LICENSE$|\\NOTICE$' }
foreach ($f in $files) {
  $text = Get-Content -LiteralPath $f.FullName -Raw
  if ($text -match 'Tunetastic') {
    $new = $text -replace 'Tunetastic', 'KyappuTone'
    Set-Content -LiteralPath $f.FullName -Value $new -NoNewline -Encoding utf8
  }
}
```
Expected: no output. `README.md` is rewritten in Task 5, so its transient state does not matter.

- [ ] **Step 3: Rename the project and solution files**

Run:
```powershell
Rename-Item .\Tunetastic.csproj .\KyappuTone.csproj
Rename-Item .\Tunetastic.sln .\KyappuTone.sln
```
Then inside `KyappuTone.sln` confirm the project entry reads `Project("{...}") = "KyappuTone", "KyappuTone.csproj", "{...}"`. The replace in Step 2 already renamed the display name; fix the file path if it still says `Tunetastic.csproj`.

- [ ] **Step 4: Update the package manifest identity**

In `Package.appxmanifest` set exactly:
```xml
<Identity Name="YoshKoz.KyappuTone" Publisher="CN=KyappuTone Development" Version="1.66.19.0" />
<DisplayName>KyappuTone</DisplayName>
<PublisherDisplayName>YoshKoz</PublisherDisplayName>
```
and in `uap:VisualElements`:
```xml
DisplayName="KyappuTone"
Description="KyappuTone is a music player for Windows with a local library, synced lyrics, and deep system integration."
```
Set the startup task to `TaskId="KyappuToneStartOnLoginTask" DisplayName="KyappuTone"`.

- [ ] **Step 5: Mark the placeholder branding**

Create `Assets/Branding/README.md`:

```
Placeholder branding.

AppIcon.png, AppIcon.ico, and Assets/Store/* are currently the upstream Tunetastic
artwork and are used only while Phase 0 stabilises the build. They must be replaced
with KyappuTone artwork before any public release (Phase 4).
```

- [ ] **Step 6: Re-run the stale-name check**

Run the same command as Step 1.
Expected: `Count : 0`.

- [ ] **Step 7: Build and launch**

Run:
```powershell
dotnet build .\KyappuTone.sln -p:Platform=x64 -c Debug -p:AppxPackageSigningEnabled=false -v:m
```
Expected: `Build succeeded`, `0 Error(s)`, and a warning count no higher than the Task 2 ceiling plus 10 percent. Launch the app; confirm the window title, tray tooltip, and Settings > About all say `KyappuTone`.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "refactor: rename product identity to KyappuTone"
```

## Task 4: Replace ImageSharp with a tested SkiaSharp cover pipeline

**Files:**
- Create: `src/KyappuTone.Core/KyappuTone.Core.csproj`
- Create: `src/KyappuTone.Core/Imaging/CoverArtProcessor.cs`
- Create: `tests/KyappuTone.Core.Tests/KyappuTone.Core.Tests.csproj`
- Create: `tests/KyappuTone.Core.Tests/Imaging/CoverArtProcessorTests.cs`
- Modify: `Common/Helpers/ImageResizer.cs`
- Modify: `KyappuTone.csproj` (remove ImageSharp, add project reference)

**Interfaces:**
- Consumes: `KyappuTone.Core` (new)
- Produces:
  - `KyappuTone.Core.Imaging.CoverArtProcessor.ResizeToPng(byte[] source, int maxWidth, int maxHeight) : byte[]`
  - `KyappuTone.Core.Imaging.CoverArtProcessor.ReencodePng(byte[] source) : byte[]`
  - `CoverArtProcessor` throws `System.ArgumentException` when `source` is not a decodable image.

- [ ] **Step 1: Create the Core library project**

`src/KyappuTone.Core/KyappuTone.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>Latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SkiaSharp" Version="4.153.1" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the test project and write the failing tests**

`tests/KyappuTone.Core.Tests/KyappuTone.Core.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\KyappuTone.Core\KyappuTone.Core.csproj" />
  </ItemGroup>
</Project>
```
If a package version does not resolve, use the latest stable of that package and note it in the commit message.

Register both new projects in the solution:
```powershell
dotnet sln .\KyappuTone.sln add .\src\KyappuTone.Core\KyappuTone.Core.csproj .\tests\KyappuTone.Core.Tests\KyappuTone.Core.Tests.csproj
```
Expected: "Project ... added to the solution." twice.

`tests/KyappuTone.Core.Tests/Imaging/CoverArtProcessorTests.cs`:
```csharp
using KyappuTone.Core.Imaging;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace KyappuTone.Core.Tests.Imaging;

public class CoverArtProcessorTests
{
    private static byte[] MakePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static (int Width, int Height) SizeOf(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        return (bitmap.Width, bitmap.Height);
    }

    [Fact]
    public void ResizeToPng_ScalesWithinBounds_AndPreservesAspect()
    {
        var result = CoverArtProcessor.ResizeToPng(MakePng(40, 20), 10, 10);
        SizeOf(result).ShouldBe((10, 5));
    }

    [Fact]
    public void ResizeToPng_PortraitSource_FitsLongestEdge()
    {
        var result = CoverArtProcessor.ResizeToPng(MakePng(30, 60), 20, 20);
        SizeOf(result).ShouldBe((10, 20));
    }

    [Fact]
    public void ReencodePng_KeepsDimensions()
    {
        var result = CoverArtProcessor.ReencodePng(MakePng(17, 9));
        SizeOf(result).ShouldBe((17, 9));
    }

    [Fact]
    public void ReencodePng_ThrowsOnGarbage()
    {
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        Should.Throw<ArgumentException>(() => CoverArtProcessor.ReencodePng(garbage));
    }

    [Fact]
    public void ResizeToPng_ThrowsOnGarbage()
    {
        var garbage = new byte[] { 0xFF, 0xD8, 0x00, 0x00 };
        Should.Throw<ArgumentException>(() => CoverArtProcessor.ResizeToPng(garbage, 10, 10));
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail to compile**

Run:
```powershell
dotnet test .\tests\KyappuTone.Core.Tests\KyappuTone.Core.Tests.csproj -v:m
```
Expected: build error `The name 'CoverArtProcessor' could not be found`.

- [ ] **Step 4: Implement `CoverArtProcessor`**

`src/KyappuTone.Core/Imaging/CoverArtProcessor.cs`:

```csharp
using SkiaSharp;

namespace KyappuTone.Core.Imaging;

/// <summary>Decode, resize and re-encode embedded cover art without external licenses.</summary>
public static class CoverArtProcessor
{
    /// <summary>Decodes <paramref name="source"/> and fits it inside the given bounds, preserving aspect ratio.</summary>
    /// <exception cref="ArgumentException">Thrown when the bytes are not a decodable image.</exception>
    public static byte[] ResizeToPng(byte[] source, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeight, 1);

        using var bitmap = Decode(source);
        var scale = Math.Min((float)maxWidth / bitmap.Width, (float)maxHeight / bitmap.Height);
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        using var resized = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new ArgumentException("Image resize failed.", nameof(source));
        return Encode(resized);
    }

    /// <summary>Decodes and re-encodes <paramref name="source"/> as PNG at its original size.</summary>
    /// <exception cref="ArgumentException">Thrown when the bytes are not a decodable image.</exception>
    public static byte[] ReencodePng(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var bitmap = Decode(source);
        return Encode(bitmap);
    }

    private static SKBitmap Decode(byte[] source)
    {
        if (source.Length == 0) throw new ArgumentException("Image data is empty.", nameof(source));
        return SKBitmap.Decode(source) ?? throw new ArgumentException("Image data could not be decoded.", nameof(source));
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
```
If `SKBitmap.Resize` on the selected SkiaSharp version takes a different sampling overload, use that version's overload; the observable behaviour in the tests is the contract.

- [ ] **Step 5: Run the tests until green**

Run:
```powershell
dotnet test .\tests\KyappuTone.Core.Tests\KyappuTone.Core.Tests.csproj -v:m
```
Expected: `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 6: Point the app's `ImageResizer` at the processor**

Remove from `KyappuTone.csproj`:
```xml
<PackageReference Include="SixLabors.ImageSharp" Version="4.1.3" />
```
Add before the closing `</Project>`:
```xml
<ItemGroup>
  <ProjectReference Include="src\KyappuTone.Core\KyappuTone.Core.csproj" />
</ItemGroup>
```

Rewrite `Common/Helpers/ImageResizer.cs` so both public overloads keep their exact signatures, keep the existing random filename scheme and fallback-to-`AppIcon.png` behaviour, and delegate image work:

```csharp
using KyappuTone.Core.Imaging;

namespace KyappuTone.Common.Helpers;

public static class ImageResizer
{
    // CreateThumbnailImage(ThumbnailFolder, IPicture[]?, int width, int height)
    //   imageData = pictures?.Length > 0 ? pictures[0].Data.Data : AppIcon bytes (existing fallback)
    //   output = CoverArtProcessor.ResizeToPng(imageData, width, height)
    //   File.WriteAllBytes(thumbnailFilePath, output)
    //
    // CreateThumbnailImage(ThumbnailFolder, IPicture[]?, string? fileName = null)
    //   same fallback, then CoverArtProcessor.ReencodePng(imageData)
    //
    // Wrap only the decode call: on ArgumentException use the AppIcon bytes and retry once.
}
```
Keep the XML doc comments that already exist for the three overloads.

- [ ] **Step 7: Build the app and smoke-test thumbnails**

Run:
```powershell
dotnet build .\KyappuTone.sln -p:Platform=x64 -c Debug -p:AppxPackageSigningEnabled=false -v:m
```
Expected: `Build succeeded`, `0 Error(s)`. Launch the app, scan a folder containing one album with art and one file without art, and confirm both views render a cover (the artless one shows the app icon). Confirm the thumbnails folder contains PNG files larger than 0 bytes.

- [ ] **Step 8: Commit**

```powershell
git add src tests Common/Helpers/ImageResizer.cs KyappuTone.csproj
git commit -m "feat: replace ImageSharp with tested SkiaSharp cover pipeline"
```

## Task 5: CI, README, and build documentation

**Files:**
- Create: `.github/workflows/build.yml` (replace upstream workflow)
- Modify: `README.md`
- Test: GitHub Actions run

**Interfaces:**
- Consumes: buildable repo from Task 4
- Produces: green CI on `main`; documented local build

- [ ] **Step 1: Replace the workflow**

`.github/workflows/build.yml`:
```yaml
name: build

on:
  push:
    branches: [main]
  pull_request:
  workflow_dispatch:

jobs:
  build:
    runs-on: windows-latest
    strategy:
      matrix:
        platform: [x64, ARM64]
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x
      - name: Restore
        run: dotnet restore KyappuTone.sln
      - name: Build
        run: dotnet build KyappuTone.sln -c Debug -p:Platform=${{ matrix.platform }} -p:AppxPackageSigningEnabled=false --no-restore
      - name: Test
        if: matrix.platform == 'x64'
        run: dotnet test tests/KyappuTone.Core.Tests/KyappuTone.Core.Tests.csproj -c Debug --no-build
      - name: Upload MSIX
        if: matrix.platform == 'x64'
        uses: actions/upload-artifact@v4
        with:
          name: KyappuTone-msix-unsigned
          path: AppPackages/**
          if-no-files-found: warn
```

- [ ] **Step 2: Rewrite `README.md`**

Content must cover: what KyappuTone is; that it derives from Tunetastic under GPLv3; requirements (Windows 10 1903+, .NET SDK 10.0.402, Windows App SDK 2.5.1 via NuGet); build command; test command; a note that no license file is needed; link to `LICENSE` and `NOTICE`; and a line stating the repo is private until first release.

- [ ] **Step 3: Push and watch CI**

Run:
```powershell
git add .github/workflows/build.yml README.md
git commit -m "ci: build and test both architectures"
git push
gh run watch --exit-status
```
Expected: run conclusion `success` for both matrix legs.

- [ ] **Step 4: Confirm the MSIX artefact exists**

Run:
```powershell
gh run list --limit 1
gh run download --name KyappuTone-msix-unsigned --dir C:\Users\yoshi\AppData\Local\Temp\kyapputone-msix
Get-ChildItem C:\Users\yoshi\AppData\Local\Temp\kyapputone-msix -Recurse -Filter *.msix | Select-Object FullName, Length
```
Expected: at least one `.msix` with non-zero length. If CI produced no MSIX, record the exact warning in `docs/perf/phase0-notes.md` and continue; packaging is fixed in Phase 4.

## Task 6: Smoke fixtures, baseline measurements, and the Phase 0 tag

**Files:**
- Create: `scripts/make-fixtures.ps1`
- Create: `scripts/smoke-checklist.md`
- Create: `docs/perf/2026-10-20-phase0-baseline.md`
- Test: manual run-through of the checklist

**Interfaces:**
- Consumes: build from Task 4, CI from Task 5
- Produces: reproducible fixtures, measured baseline, tag `v0.0.1-alpha`

- [ ] **Step 1: Write the fixture generator**

`scripts/make-fixtures.ps1`:
```powershell
param([string]$OutDir = "$env:TEMP\kyapputone-fixtures")
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$ffmpeg = Join-Path $PSScriptRoot '..\Assets\FFmpeg\ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) { throw "ffmpeg.exe not found at $ffmpeg" }
& $ffmpeg -hide_banner -loglevel error -f lavfi -i "sine=frequency=440:duration=15" -c:a flac "$OutDir\tone.flac"
& $ffmpeg -hide_banner -loglevel error -f lavfi -i "sine=frequency=660:duration=15" -c:a libmp3lame "$OutDir\two.mp3"
& $ffmpeg -hide_banner -loglevel error -f lavfi -i "sine=frequency=880:duration=15" -c:a aac "$OutDir\three.m4a"
Copy-Item "$OutDir\tone.flac" "$OutDir\tone-日本語-ąćęłńóśźż.flac"
Copy-Item (Join-Path $PSScriptRoot '..\Assets\AppIcon.png') "$OutDir\cover.png"
[System.IO.File]::WriteAllBytes("$OutDir\broken-cover.bin", [byte[]](0x00, 0x01, 0x02, 0x03))
Get-ChildItem $OutDir
```
Run it once and record the output. If `ffmpeg.exe` or the `libmp3lame` encoder is absent from `Assets/FFmpeg`, replace the mp3 line with a second flac file and note the substitution in the baseline doc.

- [ ] **Step 2: Write the manual checklist**

`scripts/smoke-checklist.md` must list, with pass/fail boxes:

- Launch and close without hanging.
- Scan the fixture folder; every fixture appears, including `one-日本語-ąćęłńóśźż.flac` (display check for non-ASCII).
- Play each fixture; seek; next/previous; shuffle; repeat.
- Volume slider; minimize to tray; tray click play/pause; taskbar thumbnail buttons; taskbar overlay shows the track; SMTC flyout shows track and controls.
- Lyrics tab opens; tag editor opens on a fixture and saves a title change; playlist create/rename/delete.
- Corrupt art: attach `broken-cover.bin` as an embedded picture (or copy it over a cover file the scanner reads); the view falls back to the app icon and logs instead of crashing.
- Locked file: hold the next track open exclusively and press play:
  ```powershell
  $fs = [System.IO.File]::Open("$env:TEMP\kyapputone-fixtures\two.mp3", 'Open', 'Read', 'None')
  # play that track in the app, then close the stream
  $fs.Dispose()
  ```
  Expected: a clear error surface, no crash, playback continues after the stream is released.
- Long path: create a folder chain past 260 characters, copy a fixture into it, rescan. Expected: the file is skipped with a log entry, scan completes.
- MSIX identity: with Developer Mode on, install the unsigned MSIX produced by CI; confirm it installs as a separate app entry from any Tunetastic install and can be uninstalled cleanly. If unsigned install is blocked, record `not measured` and the reason.

- [ ] **Step 3: Measure the baseline**

For three consecutive cold starts run:
```powershell
$proc = Start-Process .\bin\x64\Debug\net10.0-windows10.0.28000.0\KyappuTone.exe -PassThru
$proc.WaitForInputIdle(15000) | Out-Null
$sw = [System.Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds 20
$proc.Refresh()
"WorkingSetMB={0:N0}" -f ($proc.WorkingSet64 / 1MB)
$sw.Elapsed.TotalSeconds
Stop-Process -Id $proc.Id -Force
```
Record: idle working set after 20 s, whether the process stayed alive, and scan time for the fixture folder from the app's own scan result label. Write all numbers into `docs/perf/2026-10-20-phase0-baseline.md` with the date, machine, and build hash. Do not invent numbers; if a measurement is unavailable, write `not measured` and why.

- [ ] **Step 4: Run the checklist and record results**

Execute every item in `scripts/smoke-checklist.md`. Record failures as a bullet list in the baseline doc under `Known failures`. Fixing them is Phase 1 scope unless they are build-breaking.

- [ ] **Step 5: Commit and tag**

```powershell
git add scripts docs/perf
git commit -m "test: add smoke fixtures, checklist and phase 0 baseline"
git tag v0.0.1-alpha
git push --follow-tags
```
Expected: tag visible on GitHub.

## Self-review notes

- Phase 0 exit criteria in the spec are covered: build green without license files (Tasks 2–4), rebrand (Task 3), shell integration smoke (Task 6), CI (Task 5), baseline perf (Task 6).
- The Vortice and NAudio spikes moved to Phase 1 in the spec; Phase 0 only has to keep the inherited Flyleaf playback path working, which Task 6 verifies.
- Review Focus items 1–4 are pinned: corrupt art → Task 4 unit tests plus the Task 6 checklist; non-ASCII, locked files, long paths → Task 6 checklist; MSIX identity → Task 6 checklist.
- Later phases (audio core, library intelligence, acquisition, polish) get their own plans once Phase 0 is tagged.
