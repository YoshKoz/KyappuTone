using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using KyappuTone.Core.Integrations.GitHub;

namespace KyappuTone.Common.Services;

public sealed record UpdateInfo(Version Version, string Tag, string ReleaseUrl, string Notes);

/// <summary>
/// Checks the GitHub Releases feed for a newer KyappuTone build. The repository is private,
/// so the caller supplies a fine-grained token with Contents: read. The app does not download
/// or execute an update automatically; the release page is opened for a manual MSIX install.
/// </summary>
public static class UpdateService
{
	public const string Repository = "YoshKoz/KyappuTone";
	private static readonly HttpClient Http = CreateClient();

	public static async Task<UpdateInfo?> GetLatestReleaseAsync(string? token = null)
	{
		if (string.IsNullOrWhiteSpace(token))
			return null;

		try
		{
			using var request = new HttpRequestMessage(
				HttpMethod.Get,
				$"https://api.github.com/repos/{Repository}/releases/latest");
			request.Headers.UserAgent.ParseAdd("KyappuTone");
			request.Headers.Accept.ParseAdd("application/vnd.github+json");
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

			using var response = await Http.SendAsync(request);
			if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
				return null;
			if (!response.IsSuccessStatusCode)
				return null;

			var release = GitHubReleaseParser.Parse(await response.Content.ReadAsStringAsync());
			return release is null
				? null
				: new UpdateInfo(release.Version, release.Tag, release.ReleaseUrl, release.Notes);
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
}
