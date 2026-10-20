using System.Collections.ObjectModel;
using System.Diagnostics;
using KyappuTone.Common.Services;
using KyappuTone.Core.Acquisition;
using KyappuTone.Core.Radio;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;

namespace KyappuTone.Views;

/// <summary>One YouTube search result shown in the Discover page.</summary>
public sealed class YouTubeResultRow
{
	public YouTubeSearchResult Result { get; }
	public string Title => Result.Title;
	public string Uploader => string.IsNullOrWhiteSpace(Result.Uploader) ? "Unknown channel" : Result.Uploader;
	public string DurationText
	{
		get
		{
			var seconds = Result.Duration ?? 0;
			return seconds > 0
				? TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss")
				: string.Empty;
		}
	}

	public YouTubeResultRow(YouTubeSearchResult result) => Result = result;
}

/// <summary>One download queue row formatted for the Discover page.</summary>
public sealed class DownloadRowView
{
	public int Id { get; }
	public string Title { get; }
	public string StatusText { get; }
	public double Progress { get; }
	public string Error { get; }
	public string TargetFolder { get; }
	public string OutputPath { get; }
	public bool CanRetry { get; }
	public bool CanCancel { get; }
	public bool CanOpen { get; }

	public DownloadRowView(DownloadQueueRow row)
	{
		Id = row.Id;
		Title = string.IsNullOrWhiteSpace(row.Title) ? row.Url : row.Title;
		StatusText = row.Status switch
		{
			"queued" => "Queued",
			"running" => $"Downloading {row.Progress:0}%",
			"completed" => "Completed",
			"cancelled" => "Cancelled",
			"failed" => "Failed",
			_ => row.Status,
		};
		Progress = row.Progress;
		Error = row.Error;
		TargetFolder = row.TargetFolder;
		OutputPath = row.OutputPath;
		CanRetry = row.Status is "failed" or "cancelled";
		CanCancel = row.Status is "queued" or "running";
		CanOpen = row.Status == "completed" || Directory.Exists(row.TargetFolder);
	}
}

/// <summary>YouTube search, download queue, and radio entry point.</summary>
public sealed partial class DiscoverPage : Page
{
	private readonly ObservableCollection<YouTubeResultRow> _youtubeResults = new();
	private readonly ObservableCollection<DownloadRowView> _downloadRows = new();
	private readonly ObservableCollection<RadioStation> _radioFavorites = new();
	private readonly ObservableCollection<RadioStation> _radioResults = new();
	private DispatcherTimer? _icyTimer;
	private RadioStation? _playingStation;
	private bool _subscribed;
	private bool _searching;
	private bool _radioSearching;

	public DiscoverPage()
	{
		InitializeComponent();
		YouTubeResultsList.ItemsSource = _youtubeResults;
		DownloadsList.ItemsSource = _downloadRows;
		RadioFavoritesList.ItemsSource = _radioFavorites;
		RadioResultsList.ItemsSource = _radioResults;
		Loaded += DiscoverPage_Loaded;
		Unloaded += DiscoverPage_Unloaded;
	}

	private async void DiscoverPage_Loaded(object sender, RoutedEventArgs e)
	{
		if (!_subscribed)
		{
			DownloadService.Instance.QueueChanged += DownloadService_QueueChanged;
			_subscribed = true;
		}

		await LoadToolStatusAsync();
		await LoadDownloadsAsync();
		await LoadRadioFavoritesAsync();
	}

	private void DiscoverPage_Unloaded(object sender, RoutedEventArgs e)
	{
		if (!_subscribed)
			return;

		DownloadService.Instance.QueueChanged -= DownloadService_QueueChanged;
		_subscribed = false;
	}

	private async Task LoadToolStatusAsync()
	{
		try
		{
			var status = await ToolBootstrapService.GetStatusAsync();
			ToolStatusText.Text = status.YtDlpReady && status.FfmpegReady
				? $"yt-dlp {status.YtDlpVersion} · FFmpeg {status.FfmpegVersion}"
				: "Download tools are missing. Install them in Settings → Integrations → Download tools.";
			YouTubeSearchButton.IsEnabled = status.YtDlpReady;
			YouTubeQueryBox.IsEnabled = status.YtDlpReady;
		}
		catch (Exception ex)
		{
			ToolStatusText.Text = $"Could not read tool status: {ex.Message}";
		}
	}

	private async void YouTubeSearch_Click(object sender, RoutedEventArgs e)
		=> await SearchYouTubeAsync();

	private async void YouTubeQueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == Windows.System.VirtualKey.Enter)
			await SearchYouTubeAsync();
	}

	private async Task SearchYouTubeAsync()
	{
		if (_searching)
			return;

		var query = YouTubeQueryBox.Text.Trim();
		if (query.Length == 0)
		{
			GlobalNotification.Info("Enter a YouTube search query.");
			return;
		}

		_searching = true;
		YouTubeSearchButton.IsEnabled = false;
		YouTubeSearchProgress.IsActive = true;
		YouTubeSearchProgress.Visibility = Visibility.Visible;
		try
		{
			var results = await YouTubeService.SearchAsync(query);
			_youtubeResults.Clear();
			foreach (var result in results)
				_youtubeResults.Add(new YouTubeResultRow(result));

			if (_youtubeResults.Count == 0)
				GlobalNotification.Info("No YouTube results found.");
		}
		catch (Exception ex)
		{
			GlobalNotification.Error($"YouTube search failed: {ex.Message}");
		}
		finally
		{
			_searching = false;
			YouTubeSearchButton.IsEnabled = true;
			YouTubeSearchProgress.IsActive = false;
			YouTubeSearchProgress.Visibility = Visibility.Collapsed;
		}
	}

	private async void DownloadResult_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: YouTubeResultRow row })
			return;

		var picker = new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
		{
			SuggestedStartLocation = PickerLocationId.MusicLibrary,
			CommitButtonText = "Download here",
		};

		var folder = await picker.PickSingleFolderAsync();
		var target = folder?.Path ?? DownloadService.DefaultDownloadFolder;
		try
		{
			await DownloadService.Instance.EnqueueAsync(row.Result.Url, row.Title, target);
			DiscoverTabs.SelectedIndex = 1;
			await LoadDownloadsAsync();
			GlobalNotification.Info($"{row.Title} added to the download queue.");
		}
		catch (Exception ex)
		{
			GlobalNotification.Error($"Could not queue download: {ex.Message}");
		}
	}

	private async void RefreshDownloads_Click(object sender, RoutedEventArgs e)
		=> await LoadDownloadsAsync();

	private async void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
		=> await OpenFolderAsync(DownloadService.DefaultDownloadFolder);

	private async Task LoadDownloadsAsync()
	{
		try
		{
			var rows = await DownloadService.Instance.GetQueueAsync();
			_downloadRows.Clear();
			foreach (var row in rows.OrderByDescending(item => item.Id))
				_downloadRows.Add(new DownloadRowView(row));

			DownloadsEmptyState.Visibility = _downloadRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			DownloadsList.Visibility = _downloadRows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
			DownloadsSummaryText.Text = _downloadRows.Count == 0
				? string.Empty
				: $"{_downloadRows.Count} item{(_downloadRows.Count == 1 ? string.Empty : "s")}";
		}
		catch (Exception ex)
		{
			GlobalNotification.Error($"Could not load downloads: {ex.Message}");
		}
	}

	private void DownloadService_QueueChanged(object? sender, EventArgs e)
	{
		DispatcherQueue.TryEnqueue(async () => await LoadDownloadsAsync());
	}

	private async void RetryDownload_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: DownloadRowView row })
		{
			await DownloadService.Instance.RetryAsync(row.Id);
			await LoadDownloadsAsync();
		}
	}

	private async void CancelDownload_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: DownloadRowView row })
		{
			await DownloadService.Instance.CancelAsync(row.Id);
			await LoadDownloadsAsync();
		}
	}

	private async void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: DownloadRowView row })
			return;

		var folder = string.IsNullOrWhiteSpace(row.OutputPath)
			? row.TargetFolder
			: Path.GetDirectoryName(row.OutputPath) ?? row.TargetFolder;
		await OpenFolderAsync(folder);
	}

	private static Task OpenFolderAsync(string folder)
	{
		try
		{
			if (!Directory.Exists(folder))
				Directory.CreateDirectory(folder);

			Process.Start(new ProcessStartInfo
			{
				FileName = folder,
				UseShellExecute = true,
			});
		}
		catch (Exception ex)
		{
			GlobalNotification.Error($"Could not open folder: {ex.Message}");
		}

		return Task.CompletedTask;
	}

	private async void RadioSearch_Click(object sender, RoutedEventArgs e)
		=> await SearchRadioAsync();

	private async void RadioQueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == Windows.System.VirtualKey.Enter)
			await SearchRadioAsync();
	}

	private async Task SearchRadioAsync()
	{
		if (_radioSearching)
			return;

		var query = RadioQueryBox.Text.Trim();
		if (query.Length == 0)
		{
			GlobalNotification.Info("Enter a station name or genre.");
			return;
		}

		_radioSearching = true;
		RadioSearchButton.IsEnabled = false;
		RadioSearchProgress.IsActive = true;
		try
		{
			var stations = await RadioService.SearchAsync(query);
			_radioResults.Clear();
			foreach (var station in stations)
				_radioResults.Add(station);

			if (_radioResults.Count == 0)
				GlobalNotification.Info("No radio stations found.");
		}
		catch (Exception ex)
		{
			GlobalNotification.Error($"Radio search failed: {ex.Message}");
		}
		finally
		{
			_radioSearching = false;
			RadioSearchButton.IsEnabled = true;
			RadioSearchProgress.IsActive = false;
		}
	}

	private async Task LoadRadioFavoritesAsync()
	{
		try
		{
			var favorites = await RadioService.GetFavoritesAsync();
			_radioFavorites.Clear();
			foreach (var station in favorites)
				_radioFavorites.Add(station);
		}
		catch (Exception ex)
		{
			GlobalNotification.Error($"Could not load radio favorites: {ex.Message}");
		}
	}

	private async void RadioFavorite_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: RadioStation station })
			return;

		try
		{
			var added = await RadioService.ToggleFavoriteAsync(station);
			await LoadRadioFavoritesAsync();
			GlobalNotification.Info(added
				? $"{station.Name} added to radio favorites."
				: $"{station.Name} removed from radio favorites.");
		}
		catch (Exception ex)
		{
			GlobalNotification.Error($"Could not update radio favorite: {ex.Message}");
		}
	}

	private async void RadioPlay_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: RadioStation station })
			return;

		_playingStation = station;
		RadioNowPlayingText.Text = $"Connecting to {station.Name}...";
		await MusicPlayer.Instance.LoadStreamAsync(station);
		if (MusicPlayer.Instance.CurrentStream is null)
		{
			_playingStation = null;
			RadioNowPlayingText.Text = "No station playing.";
			return;
		}

		RadioNowPlayingText.Text = station.Name;
		await RadioService.TouchFavoriteAsync(station.Uuid);
		StartIcyTimer();
		await Task.Delay(2500);
		await PollIcyAsync();
	}

	private void StopRadio_Click(object sender, RoutedEventArgs e)
	{
		MusicPlayer.Instance.StopStream();
		_playingStation = null;
		StopIcyTimer();
		RadioNowPlayingText.Text = "No station playing.";
	}

	private void StartIcyTimer()
	{
		StopIcyTimer();
		_icyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
		_icyTimer.Tick += async (_, _) => await PollIcyAsync();
		_icyTimer.Start();
	}

	private void StopIcyTimer()
	{
		if (_icyTimer is null)
			return;

		_icyTimer.Stop();
		_icyTimer = null;
	}

	private async Task PollIcyAsync()
	{
		var station = _playingStation;
		if (station is null)
			return;

		var title = await IcyMetadataService.GetCurrentTitleAsync(station.Url);
		if (string.IsNullOrWhiteSpace(title) || !ReferenceEquals(_playingStation, station))
			return;

		MusicPlayer.Instance.SetStreamTitle(title);
		RadioNowPlayingText.Text = title;
	}

	private void OpenSettings_Click(object sender, RoutedEventArgs e)
		=> App.Current.NavService.NavigateTo(typeof(SettingsPage));
}
