using KyappuTone.Core.Library;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace KyappuTone.Views.LibraryViews;

/// <summary>Shows listening totals, a 30-day bar chart and a weekly heatmap.</summary>
public sealed partial class StatisticsPage : Page
{
	private const int Days = 30;
	private const int ChartHeight = 120;

	public StatisticsPage()
	{
		InitializeComponent();
	}

	private async void Page_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();

	private async Task LoadAsync()
	{
		var rows = await DatabaseHelper.Instance.GetPlayHistory(DateTime.UtcNow.AddDays(-Days));
		var entries = rows.Select(row => new PlayHistoryEntry(
			row.Path,
			new DateTimeOffset(DateTime.SpecifyKind(row.StartedAt, DateTimeKind.Utc)),
			row.MsPlayed,
			row.Completed));

		var stats = ListeningStats.Compute(entries, DateTimeOffset.UtcNow, TimeZoneInfo.Local, Days);

		TotalListeningText.Text = FormatDuration(stats.TotalListening);
		TotalPlaysText.Text = stats.TotalPlays.ToString();
		ActiveDaysText.Text = stats.Daily.Count(day => day.Listened > TimeSpan.Zero).ToString();
		EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

		RenderDaily(stats.Daily);
		RenderHeatmap(stats.Hourly);
	}

	private void RenderDaily(IReadOnlyList<DailyListening> daily)
	{
		DailyGrid.Children.Clear();
		DailyGrid.ColumnDefinitions.Clear();

		var peak = daily.Count == 0 ? 0 : daily.Max(day => day.Listened.TotalMinutes);

		for (var index = 0; index < daily.Count; index++)
		{
			DailyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			var day = daily[index];
			var height = peak <= 0 ? 2 : Math.Max(2, ChartHeight * day.Listened.TotalMinutes / peak);
			var bar = new Border
			{
				Height = height,
				Margin = new Thickness(1, 0, 1, 0),
				VerticalAlignment = VerticalAlignment.Bottom,
				Background = Accent(0.35 + 0.65 * (peak <= 0 ? 0 : day.Listened.TotalMinutes / peak)),
				CornerRadius = new CornerRadius(2),
			};

			ToolTipService.SetToolTip(bar, $"{day.Date:yyyy-MM-dd}: {FormatDuration(day.Listened)} · {day.Plays} plays");
			Grid.SetColumn(bar, index);
			DailyGrid.Children.Add(bar);
		}
	}

	private void RenderHeatmap(IReadOnlyList<HourlyListening> hourly)
	{
		HeatmapGrid.Children.Clear();
		HeatmapGrid.RowDefinitions.Clear();
		HeatmapGrid.ColumnDefinitions.Clear();

		for (var day = 0; day < 7; day++) HeatmapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		for (var hour = 0; hour < 24; hour++) HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

		var peak = hourly.Count == 0 ? 0 : hourly.Max(slot => slot.Listened.TotalMinutes);

		foreach (var slot in hourly)
		{
			var cell = new Border
			{
				Width = 16,
				Height = 16,
				Margin = new Thickness(1),
				Background = Accent(0.08 + 0.92 * (peak <= 0 ? 0 : slot.Listened.TotalMinutes / peak)),
				CornerRadius = new CornerRadius(2),
			};

			ToolTipService.SetToolTip(cell, $"{slot.Day} {slot.Hour:00}:00 · {FormatDuration(slot.Listened)}");
			Grid.SetRow(cell, (int)slot.Day);
			Grid.SetColumn(cell, slot.Hour);
			HeatmapGrid.Children.Add(cell);
		}
	}

	private static SolidColorBrush Accent(double opacity)
		=> new(Color.FromArgb((byte)Math.Clamp(opacity * 255, 0, 255), 0x4C, 0xC2, 0xFF));

	private static string FormatDuration(TimeSpan duration)
	{
		var hours = (int)duration.TotalHours;
		var minutes = duration.Minutes;
		return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
	}
}
