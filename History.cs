namespace mssqlcli;

public static class History
{
	public static void Add (
		string cmd
	) {
		if (string.IsNullOrEmpty (cmd)) {
			return;
		}

		if (_history.Contains (cmd)) { // no dupes
			return;
		}

		cmd = cmd.TrimEnd ();

		_history.Add (cmd);
		_historyOrd = _history.Count;
	}

	public static string? GetPrevious () {
		if (_historyOrd <= 0) {
			return null;
		}

		if (_history.Count == 0) {
			return null;
		}

		_historyOrd--;
		return _history[_historyOrd];
	}

	public static string? GetNext () {
		if (_history.Count > 0 && _historyOrd > _history.Count - 1) {
			return null;
		}

		if (_history.Count == 0) {
			return null;
		}

		_historyOrd++;

		// if you keep down-arrowing, put a ceiling on the ordinal, but don't keep returning the last item. return nothing
		if (_historyOrd > _history.Count - 1) {
			_historyOrd = _history.Count - 1;
			return null;
		}

		return _history[_historyOrd];
	}

	public static void ResetOrdinal () {
		_historyOrd = _history.Count;
	}

	private static readonly IList<string> _history = new List<string> ();
	private static int _historyOrd = -1;
}
