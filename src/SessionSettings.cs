using System.Text;

namespace mssqlcli;

/// <summary>
/// session settings, changed with backslash commands (\pset, \timing) and persisted by putting those lines in ~/.messqlrc
/// </summary>
public sealed class SessionSettings
{
	// how a NULL value is displayed. psql's default is an empty string
	public string NullDisplay { get; set; } = string.Empty;

	// \timing : show how long each query took
	public bool Timing { get; set; }

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
