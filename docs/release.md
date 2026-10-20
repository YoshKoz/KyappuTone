# Release checklist

KyappuTone uses a four-part MSIX version and a matching semantic product version. The
current development package is `0.3.0.0`; the product version is `0.3.0`.

## Version rules

- Bump `Package.appxmanifest` → `Identity Version` for every MSIX that will be installed
  over an older build.
- Keep `<Version>`, `<AssemblyVersion>` and `<FileVersion>` in `KyappuTone.csproj` in
  sync with the package version.
- Tag public releases as `vMAJOR.MINOR.PATCH` (for example `v0.3.0`). The GitHub update
  checker ignores drafts and prereleases.
- Never reuse a version. An MSIX cannot be downgraded in place.

## Build an unsigned test package

```powershell
dotnet restore .\KyappuTone.sln
dotnet build .\KyappuTone.sln -p:Platform=x64 -c Release -p:AppxPackageSigningEnabled=false
```

Output is under `AppPackages\`. Unsigned packages can only be registered in Developer
Mode on a machine that trusts the build output.

## Production signing

The repository currently uses the development publisher `CN=KyappuTone Development`.
Before distributing outside the development machine:

1. Obtain a code-signing certificate whose subject exactly matches the manifest
   `Publisher`, or change `Publisher` to the certificate subject.
2. Export the certificate as a password-protected PFX. Do not commit it.
3. Build with the certificate:

   ```powershell
   dotnet build .\KyappuTone.sln `
     -p:Platform=x64 `
     -c Release `
     -p:AppxPackageSigningEnabled=true `
     -p:PackageCertificateKeyFile=C:\secure\KyappuTone.pfx `
     -p:PackageCertificatePassword=$env:KYAPPUTONE_CERT_PASSWORD
   ```

4. Repeat for `-p:Platform=ARM64`.
5. Sign with a trusted timestamp server if the certificate supports it.
6. Test installing the signed MSIX on a clean Windows VM before publishing.

If the certificate is an EV/HSM certificate, use the vendor's signing tool instead of a
PFX; keep the same publisher identity.

## GitHub release

1. Run the full test suite and a clean build for x64 and ARM64.
2. Create a tag `vX.Y.Z` and push it.
3. Attach the signed x64 and ARM64 MSIX files (and their SHA-256 checksums) to the GitHub
   release.
4. Publish the release. The sideloaded update check reads
   `https://api.github.com/repos/YoshKoz/KyappuTone/releases/latest`; private repositories
   return 404, so the repository must be public for that check to work.
5. Download the release MSIX manually and verify the publisher before installing.

The app currently opens the GitHub release page for manual installation; it does not
silently download or execute an update. Automatic MSIX handoff remains future work.

## Smoke test before release

- Launch on a clean profile and scan a small test folder.
- Play, pause, seek, crossfade, EQ and sleep timer.
- Restart and confirm settings, playlists, history and favorites survive.
- Import a playlist from a local M3U and from Spotify.
- Scrobble one short test track to Last.fm and confirm it appears on the profile.
- Open Settings → About → Check for New Version.
- Confirm no crash log appears in `LocalState\Logs\crash.log`.
