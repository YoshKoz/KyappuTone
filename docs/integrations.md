# Optional integrations

Both integrations are opt-in. KyappuTone does not bundle API keys, does not ship a
Spotify client secret, and does not send telemetry. Credentials and tokens are stored in
the Windows Credential Locker; the only other local copy is the offline scrobble queue in
`KyappuTone.db3`.

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
