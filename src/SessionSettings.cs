using System.Text;

namespace mssqlcli;

/// <summary>
/// session settings, changed with backslash commands (\pset, \timing, \set) and persisted by putting those lines in ~/.messqlrc
/// </summary>
public sealed class SessionSettings
{
	// how a NULL value is displayed. psql's default is an empty string
	public string NullDisplay { get; set; } = string.Empty;

	// \timing : show how long each query took
	public bool Timing { get; set; }

	// \set HISTFILE : where history is saved. ":DBNAME" in it is replaced with the database name, for a history per database
	public string HistFile { get; set; } = DEFAULT_HISTFILE;

	// \set HISTSIZE : how many history entries are kept. below zero means unlimited
	public int HistSize { get; set; } = History.DEFAULT_SIZE;

	public const string DEFAULT_HISTFILE = "~/.messql_history";

	/// <summary>
	/// apply a \set command. args are the tokens following \set. no arguments lists the variables.
	/// a variable with no value goes back to its default
	/// </summary>
	public (bool success, string message) Set (
		IReadOnlyList<string> args
	) {
		if (args.Count == 0) {
			return (true, $"HISTFILE = '{HistFile}'{Environment.NewLine}HISTSIZE = '{HistSize}'");
		}

		string name = args[0];

		switch (name.ToUpperInvariant ()) {
			case "HISTFILE":
				// psql lets "~/.psql_history- :DBNAME" be written with a space, so join the pieces
				HistFile = args.Count > 1 ? string.Concat (args.Skip (1)) : DEFAULT_HISTFILE;
				return (true, $"HISTFILE is '{HistFile}'.");
			case "HISTSIZE":
				if (args.Count == 1) {
					HistSize = History.DEFAULT_SIZE;
				} else if (int.TryParse (args[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int size)) {
					HistSize = size;
				} else {
					return (false, $"\\set: invalid value \"{args[1]}\" for \"HISTSIZE\": integer expected");
				}

				return (true, $"HISTSIZE is '{HistSize}'.");
			default:
				return (false, $"\\set: unknown variable: {name}");
		}
	}

	/// <summary>
	/// the history file to use: a leading ~ is the home directory, and :DBNAME is the database
	/// </summary>
	public string ResolveHistFile (
		string? database
	) {
		string path = HistFile;

		if (path == "~" || path.StartsWith ("~/", StringComparison.Ordinal) || path.StartsWith ("~\\", StringComparison.Ordinal)) {
			path = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile) + path[1..];
		}

		if (path.Contains (":DBNAME", StringComparison.Ordinal)) {
			string safe = string.Concat ((database ?? string.Empty).Select (c => Path.GetInvalidFileNameChars ().Contains (c) ? '_' : c));
			path = path.Replace (":DBNAME", safe, StringComparison.Ordinal);
		}

		return path;
	}

	/// <summary>
	/// apply a \timing command. no argument toggles, otherwise on/off (or true/false, yes/no, 1/0 like psql)
	/// </summary>
	public (bool success, string message) SetTiming (
		IReadOnlyList<string> args
	) {
		if (args.Count == 0) {
			Timing = !Timing;
		} else {
			switch (args[0].ToLowerInvariant ()) {
				case "on":
				case "true":
				case "yes":
				case "1":
					Timing = true;
					break;
				case "off":
				case "false":
				case "no":
				case "0":
					Timing = false;
					break;
				default:
					return (false, $"unrecognized value \"{args[0]}\" for \"\\timing\": Boolean expected");
			}
		}

		return (true, $"Timing is {(Timing ? "on" : "off")}.");
	}

	/// <summary>
	/// apply a \pset command. args are the tokens following \pset
	/// </summary>
	/// <returns>success, and a message to show the user</returns>
	public (bool success, string message) Pset (
		IReadOnlyList<string> args
	) {
		if (args.Count == 0) {
			return (true, $"null    {quote (NullDisplay)}");
		}

		string option = args[0].ToLowerInvariant ();

		switch (option) {
			case "null":
				// like psql, no value resets it to the default (empty)
				NullDisplay = args.Count > 1 ? args[1] : string.Empty;
				return (true, $"Null display is {quote (NullDisplay)}.");
			default:
				return (false, $"\\pset: unknown option: {args[0]}");
		}
	}

	private static string quote (
		string value
	) {
		return "\"" + value + "\"";
	}

	/// <summary>
	/// split the arguments of a backslash command, psql style : whitespace separated, with
	/// 'single quoted' (a doubled '' is a literal quote) and "double quoted" values allowed to contain spaces
	/// </summary>
	public static List<string> SplitArguments (
		string text
	) {
		var args = new List<string> ();
		var current = new StringBuilder ();
		bool inToken = false;
		char quoteChar = '\0';

		for (int i = 0; i < text.Length; i++) {
			char c = text[i];

			if (quoteChar != '\0') {
				if (c == quoteChar) {
					if (quoteChar == '\'' && i + 1 < text.Length && text[i + 1] == '\'') {
						_ = current.Append ('\'');
						i++;
					} else {
						quoteChar = '\0';
					}
				} else {
					_ = current.Append (c);
				}

				continue;
			}

			if (c == '\'' || c == '"') {
				quoteChar = c;
				inToken = true;
			} else if (char.IsWhiteSpace (c)) {
				if (inToken) {
					args.Add (current.ToString ());
					_ = current.Clear ();
					inToken = false;
				}
			} else {
				_ = current.Append (c);
				inToken = true;
			}
		}

		if (inToken) {
			args.Add (current.ToString ());
		}

		return args;
	}
}
