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
		historyOrd--;
		if (historyOrd <= 0) {
			return null;
		}
		if (history.Count == 0) {
			return null;
		}
		return history[historyOrd];
	}

	public static string? GetNext () {
		historyOrd++;
		if (history.Count > 0 && historyOrd > history.Count - 1) {
			return null;
		}
		if (history.Count == 0) {
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