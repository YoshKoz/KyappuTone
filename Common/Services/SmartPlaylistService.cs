using System.Text.Json;

namespace KyappuTone.Common.Services;

/// <summary>Rules for a rule-based playlist. All conditions are ANDed together.</summary>
public sealed record SmartPlaylistRules(
	string? GenreContains = null,
	string? ArtistContains = null,
	int? YearFrom = null,
	int? YearTo = null,
	int? MinPlayCount = null);

/// <summary>Evaluates and refreshes smart playlists against the local library.</summary>
public static class SmartPlaylistService
{
	public static string Serialize(SmartPlaylistRules rules)
		=> JsonSerializer.Serialize(rules);

	public static SmartPlaylistRules? Deserialize(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return null;

		try
		{
			return JsonSerializer.Deserialize<SmartPlaylistRules>(json);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public static async Task<List<Song>> EvaluateAsync(SmartPlaylistRules rules)
	{
		var songs = await DatabaseHelper.Instance.LoadSongsFromDB(SongProperty.Title, ascending: true);
		IEnumerable<Song> result = songs;

		if (!string.IsNullOrWhiteSpace(rules.GenreContains))
			result = result.Where(song => song.Genre.Contains(rules.GenreContains, StringComparison.OrdinalIgnoreCase));

		if (!string.IsNullOrWhiteSpace(rules.ArtistContains))
			result = result.Where(song => song.Artists.Contains(rules.ArtistContains, StringComparison.OrdinalIgnoreCase));

		if (rules.YearFrom is not null || rules.YearTo is not null)
			result = result.Where(song => TryGetYear(song, out var year)
				&& (rules.YearFrom is null || year >= rules.YearFrom)
				&& (rules.YearTo is null || year <= rules.YearTo));

		if (rules.MinPlayCount is > 0)
			result = result.Where(song => song.PlayCount >= rules.MinPlayCount);

		return result.ToList();
	}

	/// <summary>Re-evaluates a smart playlist and updates its stored song rows.</summary>
	public static async Task RefreshAsync(string playlistName)
	{
		var info = await DatabaseHelper.Instance.GetPlaylistInfo(playlistName);
		if (info is null || !string.Equals(info.Kind, "smart", StringComparison.OrdinalIgnoreCase))
			return;

		var rules = Deserialize(info.RulesJson);
		if (rules is null)
			return;

		var songs = await EvaluateAsync(rules);
		await DatabaseHelper.Instance.ReplacePlaylistSongs(playlistName, songs.Select(song => song.Path).ToList());
	}

	private static bool TryGetYear(Song song, out int year)
		=> int.TryParse(song.Year?.Trim(), out year);
}
