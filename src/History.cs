using System.Text;

namespace mssqlcli;

/// <summary>
/// command history, browsed with the up/down arrows. optionally persisted to a file in psql's
/// (libedit's) "_HiStOrY_V2_" format, so a history file can be shared between psql and messql.
///
/// one entry per line. to keep an entry on one line, characters are escaped as a backslash + octal:
/// a space is \040, a backslash is \134, a tab is \011. a line break (multi-line queries) is \^A, which is
/// what psql writes. non-ASCII text is stored as plain UTF-8
/// </summary>
public static class History
{
	public const string FILE_HEADER = "_HiStOrY_V2_";
	public const int DEFAULT_SIZE = 500;

	/// <summary>
	/// set where history is persisted, and how many entries are kept. this does not read or write anything.
	/// path is null/empty to not persist. maxSize below zero means unlimited
	/// </summary>
	public static void Configure (
		string? path,
		int maxSize
	) {
		_path = string.IsNullOrWhiteSpace (path) ? null : path;
		_maxSize = maxSize;
		trim ();
		_historyOrd = _history.Count;
	}

	/// <summary>
	/// like Configure, then if that points to a different file than before, swap the in-memory history for the new file's.
	/// used when switching databases, so one database's history is never written into another's file
	/// </summary>
	public static void Reload (
		string? path,
		int maxSize
	) {
		string? newPath = string.IsNullOrWhiteSpace (path) ? null : path;
		bool changed = newPath != _path;

		if (changed) {
			_history.Clear ();
		}

		Configure (path, maxSize);

		if (changed) {
			Load ();
		}
	}

	/// <summary>
	/// read the configured history file into memory, ahead of anything already in memory.
	/// a missing file is fine, it is created the first time something is saved
	/// </summary>
	public static void Load () {
		if (_path == null || !File.Exists (_path)) {
			return;
		}

		byte[] bytes;
		try {
			bytes = File.ReadAllBytes (_path);
		} catch (Exception oops) when (oops is IOException || oops is UnauthorizedAccessException) {
			Console.WriteLine ($"{_path}: could not read history: {oops.Message}");
			return;
		}

		var loaded = new List<string> ();
		int start = 0;
		bool first = true;
		for (int i = 0; i <= bytes.Length; i++) {
			if (i < bytes.Length && bytes[i] != (byte)'\n') {
				continue;
			}

			int end = i;
			// tolerate a file with windows line endings
			if (end > start && bytes[end - 1] == (byte)'\r') {
				end--;
			}

			string entry = decode (bytes.AsSpan (start, end - start));
			start = i + 1;

			if (first) {
				first = false;
				if (entry == FILE_HEADER) {
					continue;
				}
			}

			if (!string.IsNullOrWhiteSpace (entry)) {
				loaded.Add (entry);
			}
		}

		_history.InsertRange (0, loaded);
		trim ();
		_historyOrd = _history.Count;
	}

	public static void Add (
		string cmd
	) {
		if (string.IsNullOrWhiteSpace (cmd)) {
			return;
		}

		cmd = cmd.TrimEnd ();

		// no dupes
		if (!_history.Contains (cmd)) {
			_history.Add (cmd);
			trim ();
			save ();
		}

		// a new command was entered; start browsing from the bottom again
		_historyOrd = _history.Count;
	}

	public static string? GetPrevious () {
		if (_historyOrd <= 0 || _history.Count == 0) {
			return null;
		}

		_historyOrd--;
		return _history[_historyOrd];
	}

	/// <summary>
	/// returns the next (newer) history entry.
	/// returns <see cref="string.Empty"/> when moving past the newest entry, meaning the input should go back to blank.
	/// returns null when there's nowhere to go (already at the blank prompt)
	/// </summary>
	public static string? GetNext () {
		if (_historyOrd >= _history.Count) {
			return null;
		}

		_historyOrd++;

		if (_historyOrd >= _history.Count) {
			_historyOrd = _history.Count;
			return string.Empty;
		}

		return _history[_historyOrd];
	}

	public static void ResetOrdinal () {
		_historyOrd = _history.Count;
	}

	// drop the oldest entries beyond the maximum
	private static void trim () {
		if (_maxSize >= 0 && _history.Count > _maxSize) {
			_history.RemoveRange (0, _history.Count - _maxSize);
		}
	}

	private static void save () {
		if (_path == null) {
			return;
		}

		var text = new StringBuilder ();
		_ = text.Append (FILE_HEADER).Append ('\n');
		foreach (string entry in _history) {
			_ = text.Append (encode (entry)).Append ('\n');
		}

		try {
			// history can hold sensitive queries. a new file is readable by the owner only
			var options = new FileStreamOptions () {
				Mode = FileMode.Create,
				Access = FileAccess.Write,
				Share = FileShare.Read
			};
			if (!OperatingSystem.IsWindows ()) {
				options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
			}

			using var writer = new StreamWriter (_path, new UTF8Encoding (false), options);
			writer.Write (text.ToString ());
		} catch (Exception oops) when (oops is IOException || oops is UnauthorizedAccessException) {
			// say it once, rather than after every query
			if (!_saveFailureReported) {
				Console.WriteLine ();
				Console.WriteLine ($"{_path}: could not save history: {oops.Message}");
				_saveFailureReported = true;
			}

			return;
		}

		_saveFailureReported = false;
	}

	/// <summary>
	/// turn an entry into a single line
	/// </summary>
	internal static string encode (
		string entry
	) {
		var sb = new StringBuilder (entry.Length + 16);
		for (int i = 0; i < entry.Length; i++) {
			char c = entry[i];
			switch (c) {
				case '\r':
					// \r\n is a single line break
					if (i + 1 < entry.Length && entry[i + 1] == '\n') {
						i++;
					}

					_ = sb.Append (@"\^A");
					break;
				case '\n':
					_ = sb.Append (@"\^A");
					break;
				case ' ':
					_ = sb.Append (@"\040");
					break;
				case '\\':
					_ = sb.Append (@"\134");
					break;
				default:
					if (c < 0x20 || c == 0x7f) {
						_ = sb.Append ('\\').Append (Convert.ToString (c, 8).PadLeft (3, '0'));
					} else {
						_ = sb.Append (c);
					}

					break;
			}
		}

		return sb.ToString ();
	}

	/// <summary>
	/// turn a line from a history file back into an entry
	/// </summary>
	internal static string decode (
		ReadOnlySpan<byte> line
	) {
		var bytes = new List<byte> (line.Length);
		for (int i = 0; i < line.Length; i++) {
			byte b = line[i];

			if (b != (byte)'\\' || i + 1 >= line.Length) {
				bytes.Add (b);
				continue;
			}

			byte next = line[i + 1];
			if (next == (byte)'^' && i + 2 < line.Length) {
				// \^A : a control character. ^A (0x01) is how psql stores a line break
				byte control = (byte)(line[i + 2] & 0x1f);
				bytes.Add (control == 0x01 ? (byte)'\n' : control);
				i += 2;
			} else if (next >= (byte)'0' && next <= (byte)'7') {
				// up to three octal digits
				int value = 0;
				int digits = 0;
				while (digits < 3 && i + 1 < line.Length && line[i + 1] >= (byte)'0' && line[i + 1] <= (byte)'7') {
					value = (value * 8) + (line[i + 1] - (byte)'0');
					i++;
					digits++;
				}

				bytes.Add ((byte)value);
			} else if (next == (byte)'\\') {
				bytes.Add ((byte)'\\');
				i++;
			} else {
				// not an escape we know. keep it as written
				bytes.Add (b);
			}
		}

		return Encoding.UTF8.GetString (bytes.ToArray ());
	}

	private static readonly List<string> _history = [];
	private static int _historyOrd;
	private static string? _path;
	private static int _maxSize = DEFAULT_SIZE;
	private static bool _saveFailureReported;
}
