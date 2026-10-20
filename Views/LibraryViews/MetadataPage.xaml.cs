using System.Collections.ObjectModel;
using KyappuTone.Common.Services;
using KyappuTone.Core.Metadata;

namespace KyappuTone.Views.LibraryViews;

/// <summary>Looks up and previews MusicBrainz metadata for tracks with missing fields.</summary>
public sealed partial class MetadataPage : Page
{
	private readonly ObservableCollection<Song> _songs = new();
	private bool _loading;

	private sealed record CandidateOption(MetadataCandidate Candidate)
	{
		public string Display =>
			$"{Candidate.Title} — {Candidate.Artist} — {Candidate.Album} ({Candidate.Year?.ToString() ?? "year unknown"})";
	}

	private sealed record ApplyChoice(MetadataCandidate Candidate, bool WriteCover);

	public MetadataPage()
	{
		InitializeComponent();
		TracksList.ItemsSource = _songs;
		Loaded += MetadataPage_Loaded;
	}

	private async void MetadataPage_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

	private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

	private void OpenSettings_Click(object sender, RoutedEventArgs e)
		=> App.Current.NavService.NavigateTo(typeof(SettingsPage));

	private async Task RefreshAsync()
	{
		if (_loading)
			return;

		_loading = true;
		RefreshButton.IsEnabled = false;
		try
		{
			var enabled = bool.Parse(
				Windows.Storage.ApplicationData.Current.LocalSettings
					.Values[nameof(LocalSave.MetadataLookupEnabled)]?.ToString() ?? "false");

			DisabledPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
			TracksList.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
			EmptyPanel.Visibility = Visibility.Collapsed;
			LookupButton.IsEnabled = enabled;

			if (!enabled)
			{
				_songs.Clear();
				return;
			}

			var all = await DatabaseHelper.Instance.LoadSongsFromDB();
			var missing = all.Where(NeedsMetadata)
				.OrderBy(song => song.Artists, StringComparer.OrdinalIgnoreCase)
				.ThenBy(song => song.Album, StringComparer.OrdinalIgnoreCase)
				.ThenBy(song => song.Title, StringComparer.OrdinalIgnoreCase)
				.ToList();

			_songs.Clear();
			foreach (var song in missing)
				_songs.Add(song);

			EmptyPanel.Visibility = _songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			TracksList.Visibility = _songs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
		}
		catch (Exception ex)
		{
			GlobalNotification.Error($"Could not load metadata page: {ex.Message}");
		}
		finally
		{
			_loading = false;
			RefreshButton.IsEnabled = true;
		}
	}

	private async void LookupButton_Click(object sender, RoutedEventArgs e)
	{
		var selected = TracksList.SelectedItems.OfType<Song>().ToList();
		if (selected.Count == 0)
		{
			GlobalNotification.Info("Select one or more tracks first.");
			return;
		}

		LookupButton.IsEnabled = false;
		var applied = 0;
		var skipped = 0;
		var failed = 0;

		try
		{
			foreach (var song in selected)
			{
				try
				{
					var candidates = await MetadataService.SearchAsync(song);
					if (candidates.Count == 0)
					{
						skipped++;
						continue;
					}

					var choice = await ShowCandidateDialogAsync(song, candidates);
					if (choice is null)
					{
						skipped++;
						continue;
					}

					await TagMetadataWriter.ApplyAsync(song, choice.Candidate, choice.WriteCover);
					_songs.Remove(song);
					applied++;
				}
				catch (Exception ex)
				{
					failed++;
					GlobalNotification.Error($"Could not update {song.Title}: {ex.Message}");
				}
			}
		}
		finally
		{
			LookupButton.IsEnabled = true;
			EmptyPanel.Visibility = _songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
			TracksList.Visibility = _songs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
		}

		GlobalNotification.Info(
			$"Metadata: {applied} applied, {skipped} skipped, {failed} failed.");
	}

	private async Task<ApplyChoice?> ShowCandidateDialogAsync(
		Song song,
		IReadOnlyList<MetadataCandidate> candidates)
	{
		var options = candidates.Select(candidate => new CandidateOption(candidate)).ToList();
		var combo = new ComboBox
		{
			ItemsSource = options,
			DisplayMemberPath = nameof(CandidateOption.Display),
			SelectedIndex = 0,
			HorizontalAlignment = HorizontalAlignment.Stretch,
		};

		var diffPanel = new StackPanel { Spacing = 6 };
		var coverCheck = new CheckBox
		{
			Content = "Write cover art from Cover Art Archive",
			IsChecked = true,
		};
		var content = new StackPanel
		{
			Spacing = 12,
			MinWidth = 560,
		};
		content.Children.Add(new TextBlock
		{
			Text = $"{song.Title} — {song.Artists}",
			FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
			TextWrapping = TextWrapping.Wrap,
		});
		content.Children.Add(combo);
		content.Children.Add(diffPanel);
		content.Children.Add(coverCheck);

		var dialog = new ContentDialog
		{
			Title = "Metadata match",
			Content = new ScrollViewer
			{
				MaxHeight = 420,
				Content = content,
			},
			PrimaryButtonText = "Apply",
			CloseButtonText = "Skip",
			DefaultButton = ContentDialogButton.Primary,
			XamlRoot = XamlRoot,
		};

		void UpdatePreview()
		{
			diffPanel.Children.Clear();
			if (combo.SelectedItem is not CandidateOption option)
				return;

			coverCheck.IsEnabled = !string.IsNullOrWhiteSpace(option.Candidate.CoverUrl);
			if (!coverCheck.IsEnabled)
				coverCheck.IsChecked = false;

			var diff = MetadataDiffBuilder.Build(
				new MetadataValues(song.Title, song.Artists, song.Album, song.Year, song.Genre),
				option.Candidate);

			if (!diff.HasChanges)
			{
				diffPanel.Children.Add(new TextBlock
				{
					Text = "No text changes. Cover art can still be written.",
					Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
				});
			}
			else
			{
				foreach (var change in diff.Changes)
				{
					var row = new Grid { ColumnSpacing = 10 };
					row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
					row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
					row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

					var field = new TextBlock { Text = change.Field, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
					var current = new TextBlock { Text = string.IsNullOrWhiteSpace(change.Current) ? "(empty)" : change.Current, TextWrapping = TextWrapping.Wrap };
					var proposed = new TextBlock { Text = change.Proposed, TextWrapping = TextWrapping.Wrap };
					Grid.SetColumn(field, 0);
					Grid.SetColumn(current, 1);
					Grid.SetColumn(proposed, 2);
					row.Children.Add(field);
					row.Children.Add(current);
					row.Children.Add(proposed);
					diffPanel.Children.Add(row);
				}
			}

			dialog.IsPrimaryButtonEnabled = diff.HasChanges
				|| (coverCheck.IsChecked == true && !string.IsNullOrWhiteSpace(option.Candidate.CoverUrl));
		}

		combo.SelectionChanged += (_, _) => UpdatePreview();
		coverCheck.Checked += (_, _) => UpdatePreview();
		coverCheck.Unchecked += (_, _) => UpdatePreview();
		UpdatePreview();

		var result = await dialog.ShowAsync();
		return result == ContentDialogResult.Primary && combo.SelectedItem is CandidateOption chosen
			? new ApplyChoice(chosen.Candidate, coverCheck.IsChecked == true)
			: null;
	}

	private static bool NeedsMetadata(Song song)
		=> IsMissing(song.Album) || IsMissing(song.Year) || IsMissing(song.Genre);

	private static bool IsMissing(string? value)
		=> string.IsNullOrWhiteSpace(value)
			|| value.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase);
}
