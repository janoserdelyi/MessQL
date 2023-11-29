namespace mssqlcli;

// i'm thinking about a diffferent way to calc widths - proportions. ignore data types
// roll though the column names and the first few records, finding the max width of the field name or data (stringified)
// then build widths in proportion to the console width

public class ColumnConfig
{
	public ColumnConfig (
		string name,
		int width
	) {
		Name = name;
		Width = width;
	}

	public string Name { get; set; }
	public int Width { get; set; }
}
