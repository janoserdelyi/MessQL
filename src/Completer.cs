namespace mssqlcli;

/// <summary>
/// tab completion. pure text in / text out so it has no console dependencies.
/// completes T-SQL keywords, schema names and table/view names. "dbo.&lt;tab&gt;" completes only the tables in that schema
/// </summary>
public static class Completer
{
	public sealed record Result (
		int Start,
		int End,
		string Replacement,
		IReadOnlyList<string> Options
	);

	private static readonly string[] _keywords = [
		"add", "all", "alter", "and", "any", "as", "asc", "begin", "between", "by", "case", "cast", "check",
		"column", "commit", "constraint", "convert", "create", "cross", "cursor", "database", "declare",
		"default", "delete", "desc", "distinct", "drop", "else", "end", "escape", "except", "exec", "execute",
		"exists", "fetch", "foreign", "from", "full", "function", "go", "grant", "group", "having", "if", "in",
		"index", "inner", "insert", "intersect", "into", "is", "join", "key", "left", "like", "merge", "not",
		"null", "offset", "on", "only", "open", "option", "or", "order", "outer", "over", "partition", "primary",
		"procedure", "references", "return", "returns", "revoke", "right", "rollback", "rows", "schema",
		"select", "set", "table", "temporary", "then", "top", "transaction", "truncate", "union", "unique",
		"update", "use", "values", "view", "when", "where", "while", "with"
	];

	private static (string Schema, string Name)[] _objects = [];
	private static string[] _schemas = [];

	/// <summary>
	/// the tables and views of the current database, as (schema, name). replaces whatever was set before
	/// </summary>
	public static void SetCatalog (
		IEnumerable<(string Schema, string Name)> objects
	) {
		_objects = [.. objects];
		_schemas = [.. _objects.Select (o => o.Schema).Distinct (StringComparer.OrdinalIgnoreCase)];
	}

	/// <summary>
	/// work out what tab should do for the word ending at the cursor. null means there is nothing to complete
	/// </summary>
	public static Result? Complete (
		string text,
		int cursor
	) {
		int start = cursor;
		while (start > 0 && isWordChar (text[start - 1])) {
			start--;
		}

		// "\pset" and friends are not sql
		if (start > 0 && text[start - 1] == '\\') {
			return null;
		}

		// "schema.<word>". find the schema
		string? qualifier = null;
		if (start > 0 && text[start - 1] == '.') {
			int qualifierEnd = start - 1;
			int qualifierStart = qualifierEnd;
			while (qualifierStart > 0 && isWordChar (text[qualifierStart - 1])) {
				qualifierStart--;
			}

			// ".x", or "db.schema.x" which is not supported
			if (qualifierStart == qualifierEnd || (qualifierStart > 0 && text[qualifierStart - 1] == '.')) {
				return null;
			}

			qualifier = text[qualifierStart..qualifierEnd];
		} else if (start == cursor) {
			// nothing typed yet
			return null;
		}

		// the word is replaced up to the end of the identifier the cursor is in
		int end = cursor;
		while (end < text.Length && isWordChar (text[end])) {
			end++;
		}

		string typed = text[start..cursor];

		// keywords follow the case being typed: "SEL" gives "SELECT", "sel" gives "select"
		bool upper = typed.Any (char.IsLetter) && !typed.Any (char.IsLower);

		// text, and whether it's a schema. a unique schema match is followed by "." instead of a space
		var candidates = new List<(string Text, bool IsSchema)> ();

		if (qualifier != null) {
			foreach ((string schema, string name) in _objects) {
				if (string.Equals (schema, qualifier, StringComparison.OrdinalIgnoreCase)) {
					candidates.Add ((name, false));
				}
			}
		} else {
			foreach (string keyword in _keywords) {
				candidates.Add ((upper ? keyword.ToUpperInvariant () : keyword, false));
			}

			foreach (string schema in _schemas) {
				candidates.Add ((schema, true));
			}

			foreach ((_, string name) in _objects) {
				candidates.Add ((name, false));
			}
		}

		var matches = new List<(string Text, bool IsSchema)> ();
		foreach (var candidate in candidates) {
			if (candidate.Text.StartsWith (typed, StringComparison.OrdinalIgnoreCase)
				&& !matches.Any (m => string.Equals (m.Text, candidate.Text, StringComparison.OrdinalIgnoreCase))) {
				matches.Add (candidate);
			}
		}

		if (matches.Count == 0) {
			return null;
		}

		matches.Sort ((a, b) => StringComparer.OrdinalIgnoreCase.Compare (a.Text, b.Text));
		var options = matches.Select (m => m.Text).ToList ();

		if (matches.Count == 1) {
			// a unique match is finished off like most shells do. a schema gets its dot, anything else a space
			return new Result (start, end, matches[0].Text + (matches[0].IsSchema ? "." : " "), options);
		}

		// keep what was typed as typed and only append the characters every candidate agrees on
		string prefix = commonPrefix (options);
		string replacement = prefix.Length > typed.Length ? typed + prefix[typed.Length..] : typed;

		return new Result (start, end, replacement, options);
	}

	private static string commonPrefix (
		List<string> words
	) {
		string first = words[0];
		int length = first.Length;

		foreach (string word in words) {
			int i = 0;
			while (i < length && i < word.Length && char.ToLowerInvariant (first[i]) == char.ToLowerInvariant (word[i])) {
				i++;
			}

			length = i;
		}

		return first[..length];
	}

	private static bool isWordChar (
		char c
	) {
		return char.IsLetterOrDigit (c) || c == '_' || c == '@' || c == '#' || c == '$';
	}
}
