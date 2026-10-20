using System.Collections.ObjectModel;
using KyappuTone.Common.Services;
using KyappuTone.Views.PlaylistViews;

namespace KyappuTone.Views.LibraryViews;

/// <summary>One auto-managed folder playlist shown on the Folders page.</summary>
public sealed class FolderPlaylistRow
{
	public string Name { get; init; } = string.Empty;
	public string FolderPath { get; init; } = string.Empty;
	public int Count { get; init; }
	public string CountLabel => Count == 1 ? "1 track" : $"{Count} tracks";
}

/// <summary>Lists the auto-managed folder playlists and opens them like normal playlists.</summary>
public sealed partial class FoldersPage : Page
{
	private readonly ObservableCollection<FolderPlaylistRow> _rows = new();
	private bool _loading;

	public FoldersPage()
	{
		InitializeComponent();
		FoldersList.ItemsSource = _rows;
		Loaded += FoldersPage_Loaded;
	}

	private async void FoldersPage_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

	private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

	private async Task RefreshAsync()
	{
		if (_loading)
			return;

		_loading = true;
		RefreshButton.IsEnabled = false;
		EmptyStateTitle.Text = "Loading folder playlists...";
		EmptyStateMessage.Text = "Scanning the first-level folders in your libraries.";
		EmptyState.Visibility = Visibility.Visible;
		FoldersList.Visibility = Visibility.Collapsed;
		try
		{
			await FolderPlaylistService.ReconcileAsync();
			var playlists = await DatabaseHelper.Instance.GetAutoPlaylistSummaries();

			_rows.Clear();
			foreach (var playlist in playlists)
			{
				_rows.Add(new FolderPlaylistRow
				{
					Name = playlist.Name,
					FolderPath = GetFolderPath(playlist.RulesJson),
					Count = playlist.TrackCount,
				});
			}

			EmptyStateTitle.Text = "No folder playlists yet";
			EmptyStateMessage.Text = "Add a library folder, then run a scan.";
			EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			FoldersList.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
		}
		catch (Exception ex)
		{
			GlobalNotification.Error($"Could not load folder playlists: {ex.Message}");
		}
		finally
		{
			_loading = false;
			RefreshButton.IsEnabled = true;
		}
	}

	private static string GetFolderPath(string? rulesJson)
	{
		if (string.IsNullOrWhiteSpace(rulesJson))
			return string.Empty;

		try
		{
			var rules = System.Text.Json.JsonSerializer.Deserialize<KyappuTone.Core.Library.FolderPlaylistRules>(rulesJson);
			return rules?.FolderPath ?? string.Empty;
		}
		catch (System.Text.Json.JsonException)
		{
			return string.Empty;
		}
	}

	private void FoldersList_ItemClick(object sender, ItemClickEventArgs e)
	{
		if (e.ClickedItem is FolderPlaylistRow row)
			App.Current.NavService.NavigateTo(typeof(PlayListTemplate), row.Name);
	}
}
