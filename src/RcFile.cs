namespace mssqlcli;

/// <summary>
/// reads ~/.messqlrc, in the spirit of psql's ~/.psqlrc. one command per line.
/// blank lines and "--" comments are ignored. currently the only supported command is \pset
/// </summary>
public static class RcFile
{
	public const string FILE_NAME = ".messqlrc";

	public static void Load (
		PrintSettings settings
	) {
		string path = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.UserProfile), FILE_NAME);

		if (!File.Exists (path)) {
			return;
		}

		string[] lines;
		try {
			lines = File.ReadAllLines (path);
		} catch (Exception oops) when (oops is IOException || oops is UnauthorizedAccessException) {
			Console.WriteLine ($"{path}: could not read: {oops.Message}");
			return;
		}

		for (int i = 0; i < lines.Length; i++) {
			string line = lines[i].Trim ();

			if (line.Length == 0 || line.StartsWith ("--", StringComparison.Ordinal)) {
				continue;
			}

			if (line[0] != '\\') {
				Console.WriteLine ($"{path}:{i + 1}: only backslash commands are supported. skipping `{line}`");
				continue;
			}

			// like the interactive commands, tolerate a trailing semi-colon
			var args = PrintSettings.SplitArguments (line.TrimEnd (';'));
			string cmd = args[0].ToLowerInvariant ();
			args.RemoveAt (0);

			if (cmd != @"\pset") {
				Console.WriteLine ($"{path}:{i + 1}: command {cmd} is not supported in {FILE_NAME}. skipping");
				continue;
			}

			var (success, message) = settings.Pset (args);
			if (!success) {
				Console.WriteLine ($"{path}:{i + 1}: {message}");
			}
		}
	}
}
