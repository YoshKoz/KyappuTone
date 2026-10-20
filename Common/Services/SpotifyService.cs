using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KyappuTone.Core.Integrations.Spotify;
using Windows.Security.Credentials;
using Windows.Storage;
using Windows.System;

namespace KyappuTone.Common.Services;

public sealed record SpotifyPlaylistInfo(string Id, string Name, int TrackCount);

public sealed record SpotifyTrackInfo(string Title, string Artist, string Album, int DurationMs, bool IsLocal);

public sealed record SpotifyImportResult(string PlaylistName, int Matched, int Total, int Unmatched);

/// <summary>
/// Imports Spotify playlists into the local library using the user's own Spotify app and PKCE.
/// No client secret is required, and no Spotify audio is downloaded or streamed.
/// </summary>
public sealed class SpotifyService
{
	public static SpotifyService Instance { get; } = new();

	private const string AuthorizeEndpoint = "https://accounts.spotify.com/authorize";
	private const string TokenEndpoint = "https://accounts.spotify.com/api/token";
	private const string ApiRoot = "https://api.spotify.com/v1";
	private const string RedirectUri = "http://127.0.0.1:51234/callback/";
	private const int RedirectPort = 51234;
	private const string Scopes = "playlist-read-private playlist-read-collaborative";
	private const string VaultResource = "KyappuTone.Spotify";
	private const string RefreshTokenUser = "refresh-token";

	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
	private string? _accessToken;
	private DateTimeOffset _accessTokenExpiresAt;

	private SpotifyService() { }

	public bool IsConnected => !string.IsNullOrEmpty(GetVaultSecret(RefreshTokenUser));

	public string ClientId => ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.SpotifyClientId)]?.ToString() ?? string.Empty;

	public void SaveClientId(string clientId)
		=> ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.SpotifyClientId)] = clientId.Trim();

	public void Disconnect()
	{
		RemoveVaultSecret(RefreshTokenUser);
		_accessToken = null;
		_accessTokenExpiresAt = default;
	}

	/// <summary>Opens Spotify authorization in the browser and waits for the loopback callback.</summary>
	public async Task ConnectAsync()
	{
		var clientId = RequireClientId();
		var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
		var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
		var state = Base64Url(RandomNumberGenerator.GetBytes(24));

		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
		var callbackTask = WaitForCallbackAsync(state, timeout.Token);

		var authorizeUrl = $"{AuthorizeEndpoint}?client_id={Uri.EscapeDataString(clientId)}"
			+ $"&response_type=code&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
			+ $"&code_challenge_method=S256&code_challenge={Uri.EscapeDataString(challenge)}"
			+ $"&scope={Uri.EscapeDataString(Scopes)}&state={Uri.EscapeDataString(state)}";

		if (!await Launcher.LaunchUriAsync(new Uri(authorizeUrl)))
		{
			timeout.Cancel();
			try
			{
				await callbackTask;
			}
			catch (OperationCanceledException)
			{
				// The listener is cancelled below.
			}

			throw new InvalidOperationException("Windows could not open the Spotify authorization page.");
		}

		var code = await callbackTask;
		await ExchangeCodeAsync(clientId, code, verifier);
	}

	public async Task<List<SpotifyPlaylistInfo>> GetPlaylistsAsync()
	{
		var playlists = new List<SpotifyPlaylistInfo>();
		var url = $"{ApiRoot}/me/playlists?limit=50";

		for (var page = 0; page < 10 && !string.IsNullOrEmpty(url); page++)
		{
			using var document = await ApiGetAsync(url);
			var root = document.RootElement;
			if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
			{
				foreach (var item in items.EnumerateArray())
				{
					var id = GetString(item, "id");
					var name = GetString(item, "name");
					if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
						continue;

					var trackCount = 0;
					if ((item.TryGetProperty("items", out var tracks) || item.TryGetProperty("tracks", out tracks))
						&& tracks.TryGetProperty("total", out var total)
						&& total.TryGetInt32(out var count))
						trackCount = count;

					playlists.Add(new SpotifyPlaylistInfo(id, name, trackCount));
				}
			}

			url = GetString(root, "next") ?? string.Empty;
		}

		return playlists.OrderBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	public async Task<List<SpotifyTrackInfo>> GetPlaylistTracksAsync(string playlistId)
	{
		var tracks = new List<SpotifyTrackInfo>();
		var url = $"{ApiRoot}/playlists/{Uri.EscapeDataString(playlistId)}/items?limit=50";

		for (var page = 0; page < 100 && !string.IsNullOrEmpty(url); page++)
		{
			using var document = await ApiGetAsync(url);
			var root = document.RootElement;
			if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
			{
				foreach (var wrapper in items.EnumerateArray())
				{
					// Spotify renamed "track" to "item" in playlist payloads; support both.
					JsonElement track;
					if (!wrapper.TryGetProperty("item", out track)
						&& !wrapper.TryGetProperty("track", out track))
						continue;
					if (track.ValueKind != JsonValueKind.Object)
						continue;
					if (!string.Equals(GetString(track, "type"), "track", StringComparison.OrdinalIgnoreCase))
						continue;

					var title = GetString(track, "name") ?? string.Empty;
					var artist = string.Empty;
					if (track.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
					{
						artist = string.Join(", ", artists.EnumerateArray()
							.Select(a => GetString(a, "name"))
							.Where(name => !string.IsNullOrWhiteSpace(name)));
					}

					var album = string.Empty;
					if (track.TryGetProperty("album", out var albumElement))
						album = GetString(albumElement, "name") ?? string.Empty;

					var isLocal = track.TryGetProperty("is_local", out var local) && local.ValueKind == JsonValueKind.True;
					var duration = track.TryGetProperty("duration_ms", out var durationElement)
						&& durationElement.TryGetInt32(out var durationMs)
							? durationMs
							: 0;

					tracks.Add(new SpotifyTrackInfo(title, artist, album, duration, isLocal));
				}
			}

			url = GetString(root, "next") ?? string.Empty;
		}

		return tracks;
	}

	/// <summary>Creates a KyappuTone playlist from tracks that can be matched to local files.</summary>
	public async Task<SpotifyImportResult> ImportPlaylistAsync(string playlistId, string playlistName)
	{
		var tracks = await GetPlaylistTracksAsync(playlistId);
		var songs = await DatabaseHelper.Instance.LoadSongsFromDB(SongProperty.Title, ascending: true);
		var index = new TrackMatchIndex(songs.Select(song => new TrackCandidate(song.Path, song.Title, song.Artists)));

		var matched = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var track in tracks)
		{
			if (track.IsLocal || string.IsNullOrWhiteSpace(track.Title))
				continue;

			var match = index.Find(track.Title, track.Artist);
			if (match is not null && seen.Add(match.Path))
				matched.Add(match.Path);
		}

		if (matched.Count == 0)
			return new SpotifyImportResult(string.Empty, 0, tracks.Count, tracks.Count);

		var localName = await GetUniquePlaylistName($"Spotify - {playlistName}");
		await DatabaseHelper.Instance.CreatePlaylist(localName);
		await DatabaseHelper.Instance.AddSongsToPlaylist(localName, matched);

		return new SpotifyImportResult(localName, matched.Count, tracks.Count, tracks.Count - matched.Count);
	}

	public static string RedirectUriForSetup => RedirectUri;

	private async Task<string> WaitForCallbackAsync(string expectedState, CancellationToken cancellationToken)
	{
		var listener = new TcpListener(IPAddress.Loopback, RedirectPort);
		try
		{
			listener.Start();
		}
		catch (SocketException)
		{
			throw new InvalidOperationException(
				$"Port {RedirectPort} is in use. Close the program using it, then try again.");
		}

		try
		{
			while (true)
			{
				using var client = await listener.AcceptTcpClientAsync(cancellationToken);
				using var stream = client.GetStream();
				using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
				var requestLine = await reader.ReadLineAsync(cancellationToken);
				if (string.IsNullOrEmpty(requestLine))
					continue;

				var parts = requestLine.Split(' ');
				if (parts.Length < 2)
					continue;

				var target = parts[1];
				var path = target.Split('?')[0];
				if (!string.Equals(path, "/callback/", StringComparison.OrdinalIgnoreCase)
					&& !string.Equals(path, "/callback", StringComparison.OrdinalIgnoreCase))
				{
					await WriteResponseAsync(stream, "404 Not Found", "Not found.");
					continue;
				}

				var query = ParseQuery(target);
				if (query.TryGetValue("error", out var error))
				{
					await WriteResponseAsync(stream, "400 Bad Request", "Spotify authorization was cancelled.");
					throw new InvalidOperationException($"Spotify authorization failed: {error}.");
				}

				if (!query.TryGetValue("state", out var state) || !string.Equals(state, expectedState, StringComparison.Ordinal))
				{
					await WriteResponseAsync(stream, "400 Bad Request", "State check failed.");
					throw new InvalidOperationException("Spotify authorization failed the state check.");
				}

				if (!query.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
				{
					await WriteResponseAsync(stream, "400 Bad Request", "No authorization code.");
					continue;
				}

				await WriteResponseAsync(stream, "200 OK", "Spotify is connected. You can close this tab and return to KyappuTone.");
				return code;
			}
		}
		finally
		{
			listener.Stop();
		}
	}

	private async Task ExchangeCodeAsync(string clientId, string code, string verifier)
	{
		using var content = new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["client_id"] = clientId,
			["grant_type"] = "authorization_code",
			["code"] = code,
			["redirect_uri"] = RedirectUri,
			["code_verifier"] = verifier,
		});

		using var response = await _http.PostAsync(TokenEndpoint, content);
		var body = await response.Content.ReadAsStringAsync();
		using var document = JsonDocument.Parse(body);
		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException($"Spotify token request failed: {ReadSpotifyError(document.RootElement, response.StatusCode)}");

		var root = document.RootElement;
		var accessToken = GetString(root, "access_token");
		var refreshToken = GetString(root, "refresh_token");
		if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken))
			throw new InvalidOperationException("Spotify did not return the expected tokens.");

		SetVaultSecret(RefreshTokenUser, refreshToken);
		_accessToken = accessToken;
		_accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(GetExpiresIn(root));
	}

	private async Task<string> GetAccessTokenAsync(bool forceRefresh = false)
	{
		if (!forceRefresh && !string.IsNullOrEmpty(_accessToken) && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
			return _accessToken;

		var refreshToken = GetVaultSecret(RefreshTokenUser)
			?? throw new InvalidOperationException("Connect Spotify first.");
		var clientId = RequireClientId();

		using var content = new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["client_id"] = clientId,
			["grant_type"] = "refresh_token",
			["refresh_token"] = refreshToken,
		});

		using var response = await _http.PostAsync(TokenEndpoint, content);
		var body = await response.Content.ReadAsStringAsync();
		using var document = JsonDocument.Parse(body);
		if (!response.IsSuccessStatusCode)
			throw new InvalidOperationException($"Spotify token refresh failed: {ReadSpotifyError(document.RootElement, response.StatusCode)}");

		var root = document.RootElement;
		var accessToken = GetString(root, "access_token")
			?? throw new InvalidOperationException("Spotify did not return an access token.");
		var rotatedRefreshToken = GetString(root, "refresh_token");
		if (!string.IsNullOrEmpty(rotatedRefreshToken))
			SetVaultSecret(RefreshTokenUser, rotatedRefreshToken);

		_accessToken = accessToken;
		_accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(GetExpiresIn(root));
		return accessToken;
	}

	private async Task<JsonDocument> ApiGetAsync(string url)
	{
		for (var attempt = 0; ; attempt++)
		{
			var token = await GetAccessTokenAsync(forceRefresh: attempt > 0);
			using var request = new HttpRequestMessage(HttpMethod.Get, url);
			request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

			using var response = await _http.SendAsync(request);
			var body = await response.Content.ReadAsStringAsync();

			if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
			{
				_accessToken = null;
				_accessTokenExpiresAt = default;
				continue;
			}

			if (!response.IsSuccessStatusCode)
			{
				using var errorDocument = JsonDocument.Parse(body);
				throw new InvalidOperationException($"Spotify request failed: {ReadSpotifyError(errorDocument.RootElement, response.StatusCode)}");
			}

			return JsonDocument.Parse(body);
		}
	}

	private async Task<string> GetUniquePlaylistName(string baseName)
	{
		var names = await DatabaseHelper.Instance.GetAllPlaylistNames(includeAuto: true);
		var existing = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (!existing.Contains(baseName))
			return baseName;

		for (var suffix = 2; ; suffix++)
		{
			var candidate = $"{baseName} ({suffix})";
			if (!existing.Contains(candidate))
				return candidate;
		}
	}

	private string RequireClientId()
	{
		var clientId = ClientId;
		if (string.IsNullOrWhiteSpace(clientId))
			throw new InvalidOperationException("Enter your Spotify client ID first.");
		return clientId;
	}

	private static Dictionary<string, string> ParseQuery(string target)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		var index = target.IndexOf('?');
		if (index < 0 || index == target.Length - 1)
			return result;

		foreach (var pair in target[(index + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var separator = pair.IndexOf('=');
			var key = separator < 0 ? pair : pair[..separator];
			var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
			result[WebUtility.UrlDecode(key)] = WebUtility.UrlDecode(value);
		}

		return result;
	}

	private static async Task WriteResponseAsync(NetworkStream stream, string status, string message)
	{
		var body = $"<!doctype html><html><head><meta charset=\"utf-8\"><title>KyappuTone</title></head><body><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
		var bodyBytes = Encoding.UTF8.GetBytes(body);
		var header = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
		var headerBytes = Encoding.ASCII.GetBytes(header);
		await stream.WriteAsync(headerBytes);
		await stream.WriteAsync(bodyBytes);
		await stream.FlushAsync();
	}

	private static int GetExpiresIn(JsonElement root)
		=> root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? Math.Max(60, seconds) : 3600;

	private static string? GetString(JsonElement element, string name)
		=> element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static string ReadSpotifyError(JsonElement root, HttpStatusCode statusCode)
	{
		var description = GetString(root, "error_description");
		if (!string.IsNullOrEmpty(description))
			return description;

		if (root.TryGetProperty("error", out var error))
		{
			if (error.ValueKind == JsonValueKind.String)
				return error.GetString() ?? statusCode.ToString();

			if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message)
				&& message.ValueKind == JsonValueKind.String)
				return message.GetString() ?? statusCode.ToString();
		}

		return statusCode.ToString();
	}

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private static string? GetVaultSecret(string user)
	{
		try
		{
			var credential = new PasswordVault().Retrieve(VaultResource, user);
			credential.RetrievePassword();
			return credential.Password;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static void SetVaultSecret(string user, string secret)
	{
		RemoveVaultSecret(user);
		new PasswordVault().Add(new PasswordCredential(VaultResource, user, secret));
	}

	private static void RemoveVaultSecret(string user)
	{
		try
		{
			var vault = new PasswordVault();
			vault.Remove(vault.Retrieve(VaultResource, user));
		}
		catch (Exception)
		{
			// Nothing stored.
		}
	}
}
