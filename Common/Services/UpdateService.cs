using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace KyappuTone.Common.Services;

public sealed record UpdateInfo(Version Version, string Tag, string ReleaseUrl, string Notes);

/// <summary>
/// Checks the public GitHub Releases feed for a newer KyappuTone build. The app does not
/// download or execute an update automatically; the release page is opened for a manual
/// MSIX install.
/// </summary>
public static class UpdateService
{
	public const string Repository = "YoshKoz/KyappuTone";
	private static readonly HttpClient Http = CreateClient();

	public static async Task<UpdateInfo?> GetLatestReleaseAsync()
	{
		try
		{
			using var request = new HttpRequestMessage(
				HttpMethod.Get,
				$"https://api.github.com/repos/{Repository}/releases/latest");
			request.Headers.UserAgent.ParseAdd("KyappuTone");
			request.Headers.Accept.ParseAdd("application/vnd.github+json");

			using var response = await Http.SendAsync(request);
			if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
				return null;
			if (!response.IsSuccessStatusCode)
				return null;

			var body = await response.Content.ReadAsStringAsync();
			using var document = JsonDocument.Parse(body);
			var root = document.RootElement;

			if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
				return null;
			if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.ValueKind == JsonValueKind.True)
				return null;

			var tag = GetString(root, "tag_name");
			var url = GetString(root, "html_url");
			if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url))
				return null;

			var versionText = tag.TrimStart('v', 'V');
			if (!Version.TryParse(versionText, out var version))
				return null;

			return new UpdateInfo(
				version,
				tag,
				url,
				GetString(root, "body") ?? string.Empty);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
			return null;
		}
	}

	public static bool IsNewer(Version current, Version latest)
	{
		return Normalize(latest).CompareTo(Normalize(current)) > 0;

		static Version Normalize(Version version)
			=> new(version.Major, version.Minor, Math.Max(0, version.Build),
				version.Revision > 0 ? version.Revision : 0);
	}

	private static HttpClient CreateClient()
	{
		var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		return client;
	}

	private static string? GetString(JsonElement element, string name)
		=> element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
