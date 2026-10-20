# Optional integrations

Every integration is opt-in. KyappuTone does not bundle API keys, does not ship a
Spotify client secret, and does not send telemetry. Credentials live in Windows
Credential Locker or in local settings as described below. The music database and
caches stay in the app's local state.

| Integration | Credential or setting | Storage |
| --- | --- | --- |
| Last.fm | API key | Local settings |
| Last.fm | API secret, session key | Windows Credential Locker |
| Spotify import | Client ID | Local settings |
| Spotify import | PKCE access and refresh tokens | Windows Credential Locker |
| Metadata lookup | None | SQLite lookup cache |
| Download tools | Tool paths | Local settings; binaries in LocalState |
| Discord presence | Application ID | Local settings |
| Update checks | Fine-grained GitHub token | Windows Credential Locker |

## Last.fm scrobbling

1. Create a Last.fm API account at <https://www.last.fm/api/account/create>.
2. Copy the **API key** and **shared secret**.
3. In KyappuTone, open **Settings → Integrations → Last.fm**.
4. Enter the key and secret, then select **Connect**.
5. Authorize KyappuTone in the browser page that opens.
6. Return to Settings and select **Finish connection**.
7. Turn on **Enable scrobbling**.

Behavior:

- "Now playing" is sent when a track starts.
- A scrobble is queued after the app's listening threshold (60% of the track, and the
  track is at least 30 seconds long).
- Failed deliveries stay in a local SQLite queue and are retried at startup and after the
  next scrobble. A row is discarded after 10 failed attempts.
- **Disconnect** removes the session key. The API key remains so you can reconnect
  without retyping it; the secret remains in the Credential Locker until you clear it.

If authorization fails, check that the API account is active and that the key/secret pair
matches the same Last.fm API account. The API secret is case-sensitive.

## Spotify playlist import

Spotify playback is out of scope: KyappuTone does not stream or download Spotify audio.
Import maps Spotify playlist tracks to files that are already in your library and creates
a normal local playlist from the matches.

1. Create an app at <https://developer.spotify.com/dashboard>.
2. In the app settings, add this exact redirect URI:
   `http://127.0.0.1:51234/callback/`
3. Copy the app's **Client ID**. No client secret is needed; the app uses Authorization
   Code with PKCE.
4. In KyappuTone, open **Settings → Integrations → Spotify import**.
5. Enter the client ID and select **Connect**.
6. Approve the requested playlist-read scopes in the browser. The loopback listener
   completes the sign-in automatically.
7. Choose a playlist and select **Import**.

The imported playlist is named `Spotify - <playlist name>`; a numeric suffix is added if
that name already exists. Matching is local and normalized for case, featured artists,
remaster suffixes and bracketed version text. Tracks with no unambiguous local match are
reported as unmatched.

Troubleshooting:

- **Port 51234 is in use** — close the program using it and reconnect.
- **INVALID_CLIENT / redirect URI mismatch** — the URI in the Spotify dashboard must match
  the URI above exactly, including the trailing slash and the port.
- **No playlists appear** — the Spotify app may still be in development mode. Add your
  Spotify account under the app's user allowlist, or use the account that owns the app.
- **No local matches** — Spotify titles often include remaster/live suffixes. The matcher
  strips common suffixes, but deluxe editions and alternate recordings can still differ.

## Metadata enrichment

MusicBrainz lookup is disabled until you turn it on under
**Settings → Integrations → Metadata enrichment → Enable online lookup**.

1. Open **Library → Metadata**.
2. Select a track and choose **Look up selected**.
3. Review the preview. Changed fields are shown as current value → new value.
4. Choose **Apply** to write the changes, or **Skip** to leave the file and database
   untouched.
5. Optionally enable **Write cover art from Cover Art Archive** before applying.

Only fields that differ are written. Text changes go through TagLib# into the audio file
and then into the library database. Cover art is written only when the checkbox is on.
Responses are cached in SQLite for seven days; **Clear cache** removes them.

MusicBrainz is rate-limited and requires a descriptive client. The app serializes lookups
and waits between requests. Network access happens only while the feature is enabled and
you start a lookup.

## Download tools and Discover

KyappuTone does not bundle yt-dlp or FFmpeg. Install them under
**Settings → Integrations → Download tools → Install or update**.

- yt-dlp is downloaded from its official GitHub release.
- FFmpeg is the BtbN `win64-lgpl` build.
- Both files are checked against the release SHA-256 lists before they are moved into the
  app's LocalState `Tools` folder.
- Custom `yt-dlp.exe` and `ffmpeg.exe` paths can be selected manually.
- The bootstrap downloads x64 binaries. On Windows on ARM they run under x64 emulation.

### YouTube search and downloads

1. Open **Discover → YouTube**.
2. Enter a query and select **Search**. Results come from yt-dlp.
3. Choose **Download** on a result and pick a folder, or cancel the picker to use the
   configured default download folder.
4. Follow progress under **Discover → Downloads**.
5. **Cancel** stops the active process. **Retry** re-queues a cancelled or failed item.
6. If **Import completed downloads** is on, finished files are scanned into the library.

The queue is stored in SQLite and survives restarts. Downloads run one at a time. Audio is
extracted to MP3 through FFmpeg; video is not kept. Download only material you have the
right to save.

## Internet radio

1. Open **Discover → Radio**.
2. Search the Radio Browser directory or choose a saved favorite.
3. Select **Play**. The stream is played through the same player surface as local files.
4. Select **Stop** to end the stream.

Radio Browser is a community directory; stations and stream URLs can disappear without
notice. Favorites are stored locally. While a stream is playing, KyappuTone polls ICY
metadata every 30 seconds and shows the current title in the player and Windows media
controls. Live streams have no fixed duration, so the seek bar and duration labels are
hidden until playback returns to a local file.

## Discord Rich Presence

1. Create an application at <https://discord.com/developers/applications>.
2. Copy its **Application ID**. This ID is public, not a token or secret.
3. In KyappuTone, open **Settings → Integrations → Discord Rich Presence**.
4. Paste the ID, save it, and enable presence.
5. Keep the Discord desktop client running. Presence uses Discord's local IPC socket.

The app sends the current track title and artist, or the radio station name, plus a
playing timestamp. No OAuth token, message content, or listening history is sent to
Discord. If Discord is not running, presence silently stays off and playback continues.
Live validation of presence needs your own application ID.

## Private GitHub update checks

The repository is private. The update checker can read GitHub Releases only when you
supply a fine-grained token.

1. Create a fine-grained personal access token for `YoshKoz/KyappuTone` with
   **Contents: read** only.
2. Open **Settings → About → Private update checks**.
3. Paste the token and select **Save**.
4. Use **Check for New Version**, or enable **Check for updates at startup**.

The token is stored in Windows Credential Locker, never in LocalSettings or SQLite.
Without a token the check is skipped. The app reads the latest release metadata and opens
the release page in your browser for a manual MSIX install. It does not download or run
an installer silently.

## Network endpoints

KyappuTone contacts only the services needed by the features you use:

- `musicbrainz.org` and `coverartarchive.org` for metadata lookups and cover writes.
- `api.github.com` for release checks when a token is configured.
- `ws.audioscrobbler.com` and `www.last.fm` for Last.fm.
- `accounts.spotify.com`, `api.spotify.com`, and the local loopback listener for Spotify
  import.
- `all.api.radio-browser.info` for radio search and station data; the selected stream host
  for playback.
- `github.com` for yt-dlp and FFmpeg release downloads; `youtube.com` and related hosts
  through yt-dlp for searches and downloads.
- Discord through its local IPC socket when presence is enabled.

There is no telemetry endpoint, analytics SDK, or crash-upload service. Crash logs remain
in the app's local state under `LocalState\Logs\crash.log`.

## Removing credentials

- Last.fm: **Disconnect** removes the session key; clear the key and secret fields to
  remove them from local storage.
- Spotify: **Disconnect** removes the stored PKCE tokens and client ID.
- GitHub: **Clear** removes the token from Credential Locker.
- Discord: disable presence and clear the application ID.
