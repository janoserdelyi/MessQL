using System.Data;

namespace mssqlcli;

public struct ColumnDefinitions
{
	// width of the " | " between display columns
	private const int SEPARATOR_WIDTH = 3;
	// a column is never shrunk below this unless it is naturally narrower
	private const int MIN_SHRUNK_WIDTH = 4;

	public ColumnDefinitions (
		DataTable table,
		int windowWidth
	) {
		WindowWidth = windowWidth;
		calculateColumnWidths (table);
	}

	public IList<ColumnConfig> Columns { get; } = new List<ColumnConfig> ();
	public int WindowWidth { get; set; }

	private readonly void calculateColumnWidths (
		DataTable table
	) {
		if (table == null) {
			return;
		}

		// natural width of a column : the longest of its name or any of its values (every row)
		foreach (System.Data.DataColumn col in table.Columns) {
			Columns.Add (new ColumnConfig (col.ColumnName, col.ColumnName.Length));
		}

		foreach (System.Data.DataRow row in table.Rows) {
			for (int i = 0; i < row.ItemArray.Length; i++) {
				int len = row.ItemArray[i]?.ToString ()?.Length ?? 0;
				if (len > Columns[i].Width) {
					Columns[i].Width = len;
				}
			}
		}

		if (Columns.Count == 0) {
			return;
		}

		// space left for data once the separators are accounted for. one less than the window
		// so a full-width row doesn't wrap the cursor onto the next line
		int available = WindowWidth - ((Columns.Count - 1) * SEPARATOR_WIDTH) - 1;
		int[] natural = Columns.Select ((c) => c.Width).ToArray ();

		if (natural.Sum () <= available) {
			// everything fits. no stretching, no truncating
			return;
		}

		// doesn't fit : shrink the widest columns first. find the largest cap such that
		// every column capped at it (but never below its floor) fits in the window
		int[] floors = natural.Select ((w) => Math.Min (w, MIN_SHRUNK_WIDTH)).ToArray ();

		int lo = 0;
		int hi = natural.Max ();
		while (lo < hi) {
			int mid = (lo + hi + 1) / 2;
			if (widthAtCap (natural, floors, mid) <= available) {
				lo = mid;
			} else {
				hi = mid - 1;
			}
		}

		int cap = lo;

		for (int i = 0; i < Columns.Count; i++) {
			Columns[i].Width = Math.Min (natural[i], Math.Max (cap, floors[i]));
		}

		// hand out any leftover space (less than one column's worth) to columns that were cut
		int slack = available - Columns.Sum ((c) => c.Width);
		for (int i = 0; i < Columns.Count && slack > 0; i++) {
			if (Columns[i].Width < natural[i]) {
				Columns[i].Width++;
				slack--;
			}
		}
	}

	private static int widthAtCap (
		int[] natural,
		int[] floors,
		int cap
	) {
		int sum = 0;
		for (int i = 0; i < natural.Length; i++) {
			sum += Math.Min (natural[i], Math.Max (cap, floors[i]));
		}

		return sum;
	}
}
