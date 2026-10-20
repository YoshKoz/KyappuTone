using System.Collections.ObjectModel;

namespace KyappuTone.Views.LibraryViews;

/// <summary>Shows every song marked as a favorite and starts playback from it.</summary>
public sealed partial class FavoritesPage : Page
{
	private const string PlaylistKey = "Favorites";
	private readonly ObservableCollection<Song> _songs = new();

	public FavoritesPage()
	{
		InitializeComponent();
		SongListView.ItemsSource = _songs;
	}

	private async void Page_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

	private async Task ReloadAsync()
	{
		var songs = await DatabaseHelper.Instance.GetFavoriteSongs();

		_songs.Clear();
		foreach (var song in songs) _songs.Add(song);

		var empty = _songs.Count == 0;
		SongListView.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
		EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
	}

	private List<string> Paths() => _songs.Select(song => song.Path).ToList();

	private void PlayAll_Click(object sender, RoutedEventArgs e)
	{
		if (_songs.Count == 0) return;

		MusicPlayer.Instance.ToggleShuffle(ShuffleMode.Off);
		Windows.Storage.ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.CurrentPlayinglist)] = PlaylistKey;
		MusicPlayer.Instance.LoadPlaylist(Paths());
	}

	private void ShuffleAndPlay_Click(object sender, RoutedEventArgs e)
	{
		if (_songs.Count == 0) return;

		var start = _songs[Random.Shared.Next(_songs.Count)];
		MusicPlayer.Instance.ToggleShuffle(ShuffleMode.On);
		Windows.Storage.ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.CurrentPlayinglist)] = PlaylistKey;
		MusicPlayer.Instance.LoadPlaylist(Paths(), start.Path);
	}

	private void SongListView_ItemClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is not Song song || _songs.Count == 0) return;

		Windows.Storage.ApplicationData.Current.LocalSettings.Values[nameof(LocalSave.CurrentPlayinglist)] = PlaylistKey;
		MusicPlayer.Instance.LoadPlaylist(Paths(), song.Path);
	}

	private async void RemoveFavorite_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not FrameworkElement { Tag: string path } || string.IsNullOrEmpty(path)) return;

		await DatabaseHelper.Instance.SetFavorite(path, false);
		await ReloadAsync();
	}
}
