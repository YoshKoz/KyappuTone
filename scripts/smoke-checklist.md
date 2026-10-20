# KyappuTone Phase 0 smoke checklist

Run against a Debug x64 build with fixtures from `scripts/make-fixtures.ps1`.
Record pass/fail and the observation next to each item.

Setup
- [ ] Generate fixtures: `pwsh -File scripts\make-fixtures.ps1`
- [ ] Build: `dotnet build .\KyappuTone.sln -p:Platform=x64 -c Debug -p:AppxPackageSigningEnabled=false`
- [ ] Register/launch the packaged app (Developer Mode) or run from Visual Studio F5

Core
- [ ] App launches to the main window, no crash dialog
- [ ] Scan the fixture folder; every fixture appears, including `one-日本語-ąćęłńóśźż.flac`
- [ ] Play each fixture; audio is audible and the seek bar advances
- [ ] Seek within a track; playback continues from the new position
- [ ] Next / previous track work from the player UI
- [ ] Shuffle toggles and changes the next track
- [ ] Repeat toggles (off/all/one)
- [ ] Volume slider changes loudness; mode switch (system/app) behaves

Shell integration
- [ ] Minimize-to-tray hides the window and keeps playback running
- [ ] Tray click toggles play/pause; tray tooltip shows the current track
- [ ] Taskbar thumbnail preview shows prev/play/next buttons and they work
- [ ] Taskbar overlay shows the current track and controls
- [ ] SMTC flyout (Win+N / media key) shows track metadata and controls
- [ ] Taskbar progress reflects playback position
- [ ] Lock screen (Windows 11) shows media controls

Library features
- [ ] Lyrics tab opens; synced lyrics highlight when present
- [ ] Tag editor opens on a fixture, a title change saves, and the list refreshes
- [ ] Playlist create / rename / delete works
- [ ] Playlist export produces a file in the Downloads folder

Edge cases (Review Focus)
- [ ] Corrupt art: a track whose embedded picture is `broken-cover.bin` renders the app icon and logs, no crash
- [ ] Missing art: a track with no embedded picture renders the app icon
- [ ] Locked file: hold the next track open exclusively, press play, expect a clear error, no crash
      ```powershell
      $fs = [System.IO.File]::Open("$env:TEMP\kyapputone-fixtures\two.mp3", 'Open', 'Read', 'None')
      # press play on that track in the app, then release
      $fs.Dispose()
      ```
- [ ] Long path (>260 chars): copy a fixture into a deep folder chain, rescan, expect the file is skipped with a log entry and the scan completes
- [ ] MSIX identity: install the unsigned CI MSIX with Developer Mode on; it appears as a separate app from any Tunetastic install and uninstalls cleanly

Shutdown
- [ ] Close the app; process exits within a few seconds with no orphan process
