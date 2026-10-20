using System.Collections.ObjectModel;
using KyappuTone.Core.Library;

namespace KyappuTone.Views;

/// <summary>Landing overview with listening stats, top tracks and albums, recents and a year-ago memory.</summary>
public sealed partial class HomePage : Page
{
	private const int SectionLimit = 10;
	private const int AlbumLimit = 6;

	private readonly ObservableCollection<Song> _topTracks = new();
	private readonly ObservableCollection<AlbumModel> _topAlbums = new();
	private readonly ObservableCollection<Song> _recent = new();
	private readonly ObservableCollection<Song> _added = new();
	private readonly ObservableCollection<Song> _memory = new();

	public HomePage()
	{
		InitializeComponent();

		TopTracksList.ItemsSource = _topTracks;
		TopAlbumsList.ItemsSource = _topAlbums;
		RecentList.ItemsSource = _recent;
		AddedList.ItemsSource = _added;
		MemoryList.ItemsSource = _memory;
	}

	private async void Page_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();

	private async Task LoadAsync()
	{
		GreetingText.Text = DateTime.Now.Hour switch
		{
			< 6 => "Good night",
			< 12 => "Good morning",
			< 18 => "Good afternoon",
			_ => "Good evening",
		};
		DateText.Text = DateTime.Now.ToString("dddd, d MMMM");

		var libraryCount = await DatabaseHelper.Instance.GetSongsCount();
		var empty = libraryCount == 0;
		EmptyLibraryText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
		ContentPanel.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

		var history = await DatabaseHelper.Instance.GetPlayHistory(DateTime.UtcNow.AddDays(-7));
		var entries = history.Select(row => new PlayHistoryEntry(
			row.Path,
			new DateTimeOffset(DateTime.SpecifyKind(row.StartedAt, DateTimeKind.Utc)),
			row.MsPlayed,
			row.Completed));
		var stats = ListeningStats.Compute(entries, DateTimeOffset.UtcNow, TimeZoneInfo.Local, 7);

		ListeningText.Text = FormatDuration(stats.TotalListening);
		PlaysText.Text = stats.TotalPlays.ToString();
		FavoritesText.Text = (await DatabaseHelper.Instance.GetFavoriteSongs()).Count.ToString();

		if (empty) return;

		var topTracks = await DatabaseHelper.Instance.LoadSongsFromDB(SongProperty.PlayCount, false, SectionLimit, "PlayCount > 0");
		Fill(_topTracks, topTracks);
		ShowSection(TopTracksList, TopTracksEmpty, topTracks.Count);

		var topAlbums = await DatabaseHelper.Instance.GetTopAlbumsByPlayCount(AlbumLimit);
		Fill(_topAlbums, topAlbums);
		ShowSection(TopAlbumsList, TopAlbumsEmpty, topAlbums.Count);

		var recent = await DatabaseHelper.Instance.LoadSongsFromDB(SongProperty.DateLastPlayed, false, SectionLimit, "DateLastPlayed IS NOT NULL");
		Fill(_recent, recent);
		ShowSection(RecentList, RecentEmpty, recent.Count);

		var added = await DatabaseHelper.Instance.LoadSongsFromDB(SongProperty.DateAdded, false, SectionLimit);
		Fill(_added, added);
		ShowSection(AddedList, AddedEmpty, added.Count);

		var memory = await DatabaseHelper.Instance.GetSongsPlayedAround(DateTime.UtcNow.AddYears(-1), 7, SectionLimit);
		Fill(_memory, memory);
		MemorySection.Visibility = memory.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
	}

	private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> source)
	{
		target.Clear();
		foreach (var item in source) target.Add(item);
	}

	private static void ShowSection(FrameworkElement list, FrameworkElement emptyText, int count)
	{
		list.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
		emptyText.Visibility = count > 0 ? Visibility.Collapsed : Visibility.Visible;
	}

	private static void Play(IReadOnlyList<Song> songs, string playlistKey, Song? start)
	{
		if (songs.Count == 0) return;

		Windows.Storage.ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.CurrentPlayinglist)] = playlistKey;
		MusicPlayer.Instance.LoadPlaylist(songs.Select(song => song.Path).ToList(), start?.Path);
	}

	private static string FormatDuration(TimeSpan duration)
	{
		var hours = (int)duration.TotalHours;
		var minutes = duration.Minutes;
		return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
	}

	private void TopTracks_ItemClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is Song song) Play(_topTracks, "MostPlayed", song);
	}

	private async void TopAlbums_ItemClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is not AlbumModel album) return;

		var escaped = album.Album.Replace("'", "''");
		var songs = await DatabaseHelper.Instance.LoadSongsFromDB(
			SongProperty.Track,
			true,
			0,
			$"CASE WHEN TRIM(Album) = 'Unknown Album' THEN 'Unknown' ELSE Album END = '{escaped}'");

		if (songs.Count == 0) return;

		Windows.Storage.ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.CurrentPlayinglist)] = "AllSongsViewPage";
		MusicPlayer.Instance.LoadPlaylist(songs.Select(song => song.Path).ToList(), songs[0].Path);
	}

	private void Recent_ItemClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is Song song) Play(_recent, "RecentlyPlayed", song);
	}

	private void Added_ItemClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is Song song) Play(_added, "RecentlyAdded", song);
	}

	private void Memory_ItemClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is Song song) Play(_memory, "AllSongsViewPage", song);
	}
}
