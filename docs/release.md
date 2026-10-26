# Release checklist

KyappuTone uses a three-part product version and a four-part MSIX version. The current
preview is product `0.4.0`, package `0.4.0.0`.

## Version rules

- Bump the product version in `KyappuTone.csproj` (`Version`) and the package identity in
  `Package.appxmanifest` (`Identity Version`) for every MSIX that will be installed over an
  older build. `scripts/package-release.ps1` updates both, plus `AssemblyVersion` and
  `FileVersion`.
- Keep the MSIX four-part version in sync: product `X.Y.Z` becomes package `X.Y.Z.0`.
- Each MSIX version part must be between 0 and 65535.
- Tag releases as `vMAJOR.MINOR.PATCH` (for example `v0.4.0`). The GitHub update checker
  ignores drafts and prereleases.
- Never reuse a version. Windows cannot downgrade an MSIX in place.

## Package a build

The packaging script lives at `scripts/package-release.ps1`. It builds Release, creates the
MSIX under `AppPackages\<version>\`, names it by architecture and signing mode, and writes
a SHA-256 file next to it.

### Unsigned package

```powershell
.\scripts\package-release.ps1 -Platform x64 -Version 0.4.0 -Unsigned
```

An unsigned MSIX is suitable for CI and inspection, not normal installation. For local
development, register the loose build with Developer Mode enabled:

```powershell
Add-AppxPackage -Register .\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml
```

### Self-signed development package

```powershell
.\scripts\package-release.ps1 -Platform x64 -Version 0.4.0 -CreateDevelopmentCertificate
```

The first run creates a self-signed certificate for `CN=KyappuTone Development`, installs
it into the current user's `TrustedPeople` and `Root` stores, exports a password-protected
PFX under `%LOCALAPPDATA%\KyappuTone\Signing`, and reuses that PFX on later runs. The
script writes the development `.cer` next to the package for other test machines.

**This certificate is not production-trusted.** Another machine must import the `.cer`
into `TrustedPeople` (and optionally `Root` for local signature verification) before it
can install the package. Trusting a self-signed root means trusting anything signed by
that development key, so use it only on test machines. Never distribute this certificate
or package as a public release.

Uninstall a development build with:

```powershell
Get-AppxPackage YoshKoz.KyappuTone | Remove-AppxPackage
```

### Production PFX

There is no production certificate in this repository. To produce a production-signed
package:

1. Obtain a code-signing certificate. Its subject must exactly equal the manifest
   `Publisher` (`CN=KyappuTone Development` in the current source). Either use a
   certificate with that exact subject or update `Publisher` in `Package.appxmanifest` to
   the certificate subject before building.
2. Export the certificate as a password-protected PFX outside the repository.
3. Build with the PFX:

   ```powershell
   $secure = Read-Host -AsSecureString
   $plain = [System.Net.NetworkCredential]::new('', $secure).Password
   .\scripts\package-release.ps1 -Platform x64 -Version 0.4.0 `
     -CertificatePath C:\secure\KyappuTone.pfx `
     -CertificatePassword $plain
   ```

4. Repeat for `-Platform ARM64`.
5. Sign with a trusted timestamp server if the certificate supports it.
6. Test installation on a clean Windows VM before publishing.

If the certificate is EV/HSM-backed, use the vendor's signing tool instead of a PFX, but
keep the same publisher identity. Do not commit a PFX, its password, or exported private
keys. `*.pfx`, `*.p12`, `AppPackages/`, and local database files are ignored by Git.

## Verify the package

- Confirm the package opens with `Get-AppxPackageManifest` and the identity version matches
  the requested version.
- Confirm `Get-AuthenticodeSignature` reports `Valid` for a signed package and `NotSigned`
  for an unsigned package.
- Recompute SHA-256 and compare it with the `.sha256` file:

  ```powershell
  Get-FileHash .\AppPackages\0.4.0\KyappuTone-0.4.0-x64-dev-signed.msix -Algorithm SHA256
  Get-Content .\AppPackages\0.4.0\KyappuTone-0.4.0-x64-dev-signed.msix.sha256
  ```

- Install on the development machine and confirm the library database, playlists, and
  settings survive. Do not test a development-signed package on a machine that has not
  trusted the certificate; that failure is expected.

## GitHub release

1. Run the full test suite and a clean build for x64 and ARM64.
2. Run the packaging script for both architectures.
3. Create a tag `vX.Y.Z` and push it.
4. Attach the MSIX files, their `.sha256` files, and — for development builds only — the
   development `.cer`.
5. State clearly in the release notes whether the package is unsigned, development-signed,
   or production-signed.
6. Publish the release. The sideloaded update check reads
   `https://api.github.com/repos/YoshKoz/KyappuTone/releases/latest` with an optional
   fine-grained token stored in Windows Credential Locker. The token needs only
   `Contents: read`. Without a token the check is skipped and Settings explains why.
7. Download the release MSIX manually and verify the publisher and checksum before
   installing.

The app currently opens the GitHub release page for manual installation; it does not
silently download or execute an update.

## Smoke test before release

- Launch on a clean profile and scan a small test folder.
- Play, pause, seek, crossfade, EQ and sleep timer.
- Open Folders, Metadata, Discover, YouTube, Downloads, and Radio once.
- Restart and confirm settings, playlists, history and favorites survive.
- Import a playlist from a local M3U and, if credentials are available, from Spotify.
- Scrobble one short test track to Last.fm and confirm it appears on the profile.
- Open Settings → About → Check for New Version.
- Confirm no crash log appears in `LocalState\Logs\crash.log`.
- Confirm the generated packages match their SHA-256 files.

Tone-generation playback and long audio soak loops are not part of the routine release
gate; run them only when audio-engine changes require it.

## Final verification commands

```powershell
dotnet test .\tests\KyappuTone.Core.Tests
dotnet test .\tests\KyappuTone.Audio.Tests
dotnet build .\KyappuTone.sln -p:Platform=x64 -c Debug -p:AppxPackageSigningEnabled=false
dotnet build .\KyappuTone.sln -p:Platform=x64 -c Release -p:AppxPackageSigningEnabled=false
dotnet build .\KyappuTone.sln -p:Platform=ARM64 -c Debug -p:AppxPackageSigningEnabled=false
git status --short --branch
git ls-files | Select-String -Pattern '(?i)(\.pfx$|\.p12$|\.db3$|crash\.log|LocalState)'
```

Every build must finish with zero warnings and zero errors.
