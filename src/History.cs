namespace mssqlcli;

public static class History
{
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

	private static readonly IList<string> _history = new List<string> ();
	private static int _historyOrd = 0;
}
