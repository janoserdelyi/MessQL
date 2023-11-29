using System.Data;

namespace mssqlcli;

public struct ColumnDefinitions
{
	public ColumnDefinitions (
		DataTable table,
		int windowWidth,
		int testDepth = 4
	) {
		WindowWidth = windowWidth;
		TestDepth = testDepth;
		calculateColumnWidths (table);
	}

	public IList<ColumnConfig> Columns { get; } = new List<ColumnConfig> ();
	public int WindowWidth { get; set; }
	public int TestDepth { get; set; }

	private void calculateColumnWidths (
		DataTable table
	) {
		if (table == null) {
			return;
		}

		// first roll through and get the column names and m in widths based on the length of the names
		foreach (System.Data.DataColumn col in table.Columns) {
			//Console.Write (col.ColumnName + " | ");
			Columns.Add (new ColumnConfig (col.ColumnName, col.ColumnName.Length));
		}
		if (table.Rows.Count == 0) {
			goto proportionalWidthCalc;
		}

		// now for a number of rows as defined by the test depth, check the value lengths
		int curDepth = 0;

		foreach (System.Data.DataRow row in table.Rows) {

			if (curDepth > TestDepth) {
				break;
			}

			for (int i = 0; i < row.ItemArray.Length; i++) {
				if (row.ItemArray[i] == null) {
					continue;
				}
				if (row.ItemArray[i]!.ToString ()!.Length > Columns[i].Width) {
					Columns[i].Width = row.ItemArray[i]!.ToString ()!.Length;
				}
			}

			curDepth++;
		}

	proportionalWidthCalc:

		int rawWidthSum = Columns.Sum ((w) => { return w.Width; });

		// the total width (windowwidth) needs to be taken down a notch due to rendering concerns
		// between each display column is a " | "
		WindowWidth = WindowWidth - ((Columns.Count - 1) * 3);

		// now calc for proportions against the window width
		// if anything ends up being less than the name width, maintain the name width
		for (int i = 0; i < Columns.Count; i++) {
			int newwidth = (Columns[i].Width * WindowWidth) / rawWidthSum;

			if (newwidth > Columns[i].Name.Length) {
				Columns[i].Width = newwidth;
			}
		}
	}
}
