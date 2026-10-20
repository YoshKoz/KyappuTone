using System.Net.Http;
using System.Text.Json;
using KyappuTone.Core.Integrations.LastFm;
using Windows.Security.Credentials;
using Windows.Storage;

namespace KyappuTone.Common.Services;

/// <summary>
/// Optional Last.fm scrobbling. Credentials are supplied by the user; the API secret and
/// session key are kept in the Windows Credential Locker, never in LocalSettings.
/// </summary>
public sealed class LastFmService
{
	public static LastFmService Instance { get; } = new();

	private const string ApiRoot = "https://ws.audioscrobbler.com/2.0/";
	private const string AuthRoot = "https://www.last.fm/api/auth/";
	private const string VaultResource = "KyappuTone.LastFm";
	private const string SessionUser = "session";
	private const string SecretUser = "api-secret";
	private const int MinimumTrackSeconds = 30;

	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
	private string? _pendingToken;

	private LastFmService() { }

	public bool IsEnabled
	{
		get => bool.Parse(ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.LastFmEnabled)]?.ToString() ?? "false");
		set => ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.LastFmEnabled)] = value;
	}

	public bool IsConnected => !string.IsNullOrEmpty(GetVaultSecret(SessionUser));

	public string Username => ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.LastFmUsername)]?.ToString() ?? string.Empty;

	public bool HasCredentials => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrEmpty(GetVaultSecret(SecretUser));

	private static string ApiKey => ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.LastFmApiKey)]?.ToString() ?? string.Empty;

	private static string ApiSecret => GetVaultSecret(SecretUser) ?? string.Empty;

	public void SaveCredentials(string apiKey, string apiSecret)
	{
		ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.LastFmApiKey)] = apiKey.Trim();
		if (!string.IsNullOrEmpty(apiSecret))
			SetVaultSecret(SecretUser, apiSecret);
	}

	public async Task<string> StartAuthorizationAsync()
	{
		var (key, secret) = RequireCredentials();

		using var tokenResponse = await InvokeAsync("auth.getToken", new Dictionary<string, string>(), key, secret, sessionKey: null)
			?? throw new InvalidOperationException("Last.fm did not return an authorization token.");

		if (!tokenResponse.RootElement.TryGetProperty("token", out var tokenElement)
			|| string.IsNullOrWhiteSpace(tokenElement.GetString()))
			throw new InvalidOperationException("Last.fm did not return an authorization token.");

		_pendingToken = tokenElement.GetString();
		return $"{AuthRoot}?api_key={Uri.EscapeDataString(key)}&token={Uri.EscapeDataString(_pendingToken!)}";
	}

	public async Task<bool> CompleteAuthorizationAsync()
	{
		if (string.IsNullOrEmpty(_pendingToken))
			throw new InvalidOperationException("Start the Last.fm connection first.");

		var (key, secret) = RequireCredentials();
		using var response = await InvokeAsync(
			"auth.getSession",
			new Dictionary<string, string> { ["token"] = _pendingToken },
			key,
			secret,
			sessionKey: null);

		if (response is null
			|| !response.RootElement.TryGetProperty("session", out var session)
			|| !session.TryGetProperty("key", out var sessionKeyElement)
			|| string.IsNullOrWhiteSpace(sessionKeyElement.GetString()))
			throw new InvalidOperationException("Last.fm did not return a session. Authorize the app in the browser first.");

		SetVaultSecret(SessionUser, sessionKeyElement.GetString()!);

		var name = session.TryGetProperty("name", out var nameElement)
			? nameElement.GetString() ?? string.Empty
			: string.Empty;
		ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.LastFmUsername)] = name;
		_pendingToken = null;
		return true;
	}

	public void Disconnect()
	{
		RemoveVaultSecret(SessionUser);
		ApplicationData.Current.LocalSettings.Values.Remove(nameof(LocalSave.LastFmUsername));
		_pendingToken = null;
	}

	public async Task UpdateNowPlayingAsync(string songPath)
	{
		if (!IsEnabled || !IsConnected || !HasCredentials) return;

		var song = await DatabaseHelper.Instance.GetSongByPath(songPath);
		if (song is null || song.Duration < MinimumTrackSeconds) return;

		try
		{
			var (key, secret) = RequireCredentials();
			using var response = await InvokeAsync(
				"track.updateNowPlaying",
				new Dictionary<string, string>
				{
					["artist"] = song.Artists,
					["track"] = song.Title,
					["album"] = song.Album,
					["duration"] = Math.Max(0, (int)song.Duration).ToString(),
				},
				key,
				secret,
				GetVaultSecret(SessionUser));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Last.fm now-playing failed: {ex.Message}");
		}
	}

	/// <summary>Queues a completed scrobble and attempts delivery. Failed rows survive restarts.</summary>
	public async Task ScrobbleAsync(string songPath, TimeSpan played, DateTime startedUtc)
	{
		if (!IsEnabled || !IsConnected || !HasCredentials) return;

		var song = await DatabaseHelper.Instance.GetSongByPath(songPath);
		if (song is null || song.Duration < MinimumTrackSeconds) return;

		var timestamp = new DateTimeOffset(DateTime.SpecifyKind(startedUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
		await DatabaseHelper.Instance.EnqueueScrobbleAsync(
			song.Artists,
			song.Title,
			song.Album,
			Math.Max(0, (int)song.Duration),
			timestamp);

		await FlushQueueAsync();
	}

	public async Task FlushQueueAsync()
	{
		if (!IsConnected || !HasCredentials) return;

		var pending = await DatabaseHelper.Instance.GetPendingScrobblesAsync();
		if (pending.Count == 0) return;

		var (key, secret) = RequireCredentials();
		var session = GetVaultSecret(SessionUser);
		if (string.IsNullOrEmpty(session)) return;

		foreach (var row in pending)
		{
			if (row.Attempts >= 10)
			{
				await DatabaseHelper.Instance.DeleteScrobbleAsync(row.Id);
				continue;
			}

			try
			{
				using var response = await InvokeAsync(
					"track.scrobble",
					new Dictionary<string, string>
					{
						["artist"] = row.Artist,
						["track"] = row.Title,
						["album"] = row.Album,
						["duration"] = row.Duration.ToString(),
						["timestamp"] = row.PlayedAtUnix.ToString(),
					},
					key,
					secret,
					session);

				if (response is null)
				{
					await DatabaseHelper.Instance.MarkScrobbleFailedAsync(row.Id);
					break;
				}

				await DatabaseHelper.Instance.DeleteScrobbleAsync(row.Id);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Last.fm scrobble failed: {ex.Message}");
				await DatabaseHelper.Instance.MarkScrobbleFailedAsync(row.Id);
				break;
			}
		}
	}

	private (string Key, string Secret) RequireCredentials()
	{
		var key = ApiKey;
		var secret = ApiSecret;
		if (string.IsNullOrWhiteSpace(key) || string.IsNullOrEmpty(secret))
			throw new InvalidOperationException("Enter your Last.fm API key and secret first.");
		return (key, secret);
	}

	private async Task<JsonDocument?> InvokeAsync(
		string method,
		Dictionary<string, string> parameters,
		string apiKey,
		string apiSecret,
		string? sessionKey)
	{
		var signedParameters = new Dictionary<string, string>(parameters)
		{
			["method"] = method,
			["api_key"] = apiKey,
		};

		if (!string.IsNullOrEmpty(sessionKey))
			signedParameters["sk"] = sessionKey;

		signedParameters["api_sig"] = LastFmSignature.Sign(signedParameters, apiSecret);
		signedParameters["format"] = "json";

		using var content = new FormUrlEncodedContent(signedParameters);
		using var response = await _http.PostAsync(ApiRoot, content);
		var body = await response.Content.ReadAsStringAsync();

		if (!response.IsSuccessStatusCode)
			return null;

		var document = JsonDocument.Parse(body);
		if (document.RootElement.TryGetProperty("error", out var error))
		{
			var message = document.RootElement.TryGetProperty("message", out var messageElement)
				? messageElement.GetString()
				: error.ToString();
			document.Dispose();
			throw new InvalidOperationException($"Last.fm error {error}: {message}");
		}

		return document;
	}

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
		var vault = new PasswordVault();
		RemoveVaultSecret(user);
		vault.Add(new PasswordCredential(VaultResource, user, secret));
	}

	private static void RemoveVaultSecret(string user)
	{
		try
		{
			var vault = new PasswordVault();
			var credential = vault.Retrieve(VaultResource, user);
			vault.Remove(credential);
		}
		catch (Exception)
		{
			// Nothing stored.
		}
	}
}
