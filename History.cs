namespace mssqlcli;

public static class History
{
	public static void Add (
		string cmd
	) {
		if (string.IsNullOrEmpty (cmd)) {
			return;
		}
		if (history.Contains (cmd)) { // no dupes
			return;
		}

		cmd = cmd.TrimEnd ();

		history.Add (cmd);
		historyOrd = history.Count;
	}

	public static string? GetPrevious () {
		if (historyOrd <= 0) {
			return null;
		}
		if (history.Count == 0) {
			return null;
		}
		historyOrd--;
		return history[historyOrd];
	}

	public static string? GetNext () {
		if (history.Count > 0 && historyOrd > history.Count - 1) {
			return null;
		}
		if (history.Count == 0) {
			return null;
		}
		historyOrd++;

		// if you keep down-arrowing, put a ceiling on the ordinal, but don't keep returning the last item. return nothing
		if (historyOrd > history.Count - 1) {
			historyOrd = history.Count - 1;
			return null;
		}

		return history[historyOrd];
	}

	public static void ResetOrdinal () {
		historyOrd = history.Count;
	}

	private static readonly IList<string> history = new List<string> ();
	private static int historyOrd = -1;
}