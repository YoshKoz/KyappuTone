using System.Text;
using Windows.Storage;

namespace KyappuTone.Common.Services;

/// <summary>
/// Writes local crash diagnostics. Nothing is uploaded; logs stay in the app's LocalState folder.
/// </summary>
public static class CrashLogger
{
	private const long MaximumLogBytes = 1_000_000;
	private static int _initialized;

	public static string LogDirectory => Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs");

	public static string LogPath => Path.Combine(LogDirectory, "crash.log");

	public static void Initialize()
	{
		if (Interlocked.Exchange(ref _initialized, 1) != 0)
			return;

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			if (args.ExceptionObject is Exception exception)
				Log("AppDomain", exception);
		};

		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			Log("TaskScheduler", args.Exception);
			args.SetObserved();
		};
	}

	public static void Log(string source, Exception exception)
	{
		try
		{
			Directory.CreateDirectory(LogDirectory);
			RotateIfNeeded();

			var builder = new StringBuilder();
			builder.AppendLine($"[{DateTimeOffset.Now:O}] {source}");
			builder.AppendLine($"Version: {typeof(CrashLogger).Assembly.GetName().Version}");
			builder.AppendLine($"OS: {Environment.OSVersion}");
			builder.AppendLine($"Runtime: {Environment.Version}");
			builder.AppendLine(exception.ToString());
			builder.AppendLine(new string('-', 80));
			builder.AppendLine();

			File.AppendAllText(LogPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch
		{
			// Diagnostics must never mask the original failure.
		}
	}

	private static void RotateIfNeeded()
	{
		var info = new FileInfo(LogPath);
		if (!info.Exists || info.Length < MaximumLogBytes)
			return;

		var previous = Path.Combine(LogDirectory, "crash.old.log");
		File.Move(LogPath, previous, overwrite: true);
	}
}
