# KyappuTone

<img src="Assets/Branding/kyapputone-icon.svg" width="160" alt="KyappuTone logo">

A music player for Windows. Local library, synced lyrics, tagging, playlists, and deep
integration with the Windows shell (media controls, tray, taskbar overlay, lock screen).

KyappuTone is derived from [Tunetastic](https://github.com/AMit-KP/Tunetastic) and is
licensed under the GNU General Public License v3.0. See `LICENSE` and `NOTICE`.

The repository is private until the first release.

## Status

Phase 0: foundation. Rebranded, retargeted to .NET 10, ImageSharp replaced with
SkiaSharp. Feature work (audio core, library intelligence, downloads, radio, polish)
lands in later phases; see `docs/superpowers/specs/`.

## Requirements

- Windows 10 version 1903 (build 18362) or later, or Windows 11
- [.NET SDK 10.0.402](https://dotnet.microsoft.com/download) or a newer 10.0.x SDK
- Windows App SDK 2.5.1 and the Windows SDK build tools are restored from NuGet
- No paid license file and no `sixlabors.lic` are required to build

## Build

```powershell
dotnet restore .\KyappuTone.sln
dotnet build .\KyappuTone.sln -p:Platform=x64 -c Debug -p:AppxPackageSigningEnabled=false
```

The app is packaged (MSIX). Running the loose executable from `bin` requires a package
identity, so use Visual Studio's F5 with the packaging project, or register the build
output with Developer Mode enabled:

```powershell
Add-AppxPackage -Register .\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml
```

## Test

```powershell
dotnet test .\tests\KyappuTone.Core.Tests\KyappuTone.Core.Tests.csproj
```

## CI

`.github/workflows/build.yml` builds x64 and ARM64, runs the unit tests on x64, and
uploads the unsigned MSIX as an artifact.

## Credits

Upstream: Tunetastic by AMit-KP and contributors (GPLv3). Image processing uses
SkiaSharp (MIT). Audio and media plumbing uses FFmpeg (LGPL) and FlyleafLib.
