using System.CommandLine;
using System.CommandLine.Help;
using System.Data;
using System.Text;
using com.janoserdelyi.DataSource;
using Microsoft.Data.SqlClient;

namespace mssqlcli;

public class Program
{
	public static int Main (
		string[] args
	) {

		//Console.WriteLine ("IsErrorRedirected : " + Console.IsErrorRedirected);
		//Console.WriteLine ("IsInputRedirected : " + Console.IsInputRedirected);
		//Console.WriteLine ("IsOutputRedirected : " + Console.IsOutputRedirected);

		// this is likely to change in the future
		if (Console.IsInputRedirected || Console.IsErrorRedirected || Console.IsOutputRedirected) {
			Console.WriteLine ("Console output is being redirected. This app does not function that way. Press [enter] to exit");
			_ = Console.ReadLine ();
			return -1;
		}

		var rootCommand = new RootCommand ("MSSQL cli in the spirit of psql");

		/*
		psql options i'll be implementing:
		-d, --dbname=DBNAME      database name to connect to
		-h, --host=HOSTNAME      database server host or socket directory
		-p, --port=PORT          database server port (default: "5432")
		-U, --username=USERNAME  database user name (default: "janos")
		-W, --password           force password prompt (should happen automatically)

		i will also likely scan for a config file in the ~/.config directory somewhere
		*/

		var configs = parseConfig ();

		var dbnameOption = new Option<string> ("--dbname", "-d") {
			Description = "database name to connect to"
		};

		var hostOption = new Option<string> ("--host", "-h") {
			Description = "database server host",
			DefaultValueFactory = _ => "localhost"
		};

		var portOption = new Option<int> ("--port", "-p") {
			Description = "database server port (default: \"1433\")",
			DefaultValueFactory = _ => 1433
		};

		var usernameOption = new Option<string> ("--username", "-u") {
			Description = "database user name"
		};

		var passwordpromptOption = new Option<bool> ("--password", "-W") {
			Description = "force password prompt (should happen automatically)",
			DefaultValueFactory = _ => false
		};

		rootCommand.Options.Add (dbnameOption);
		rootCommand.Options.Add (hostOption);
		rootCommand.Options.Add (portOption);
		rootCommand.Options.Add (usernameOption);
		rootCommand.Options.Add (passwordpromptOption);

		// System.CommandLine's built-in help option claims "-h", which shadows --host.
		// like psql, "-h" means host. help stays available as "--help" and "-?"
		var helpOption = rootCommand.Options.OfType<HelpOption> ().First ();
		_ = helpOption.Aliases.Remove ("-h");

		var optionsConfig = new ConfigExtension ();

		rootCommand.SetAction (parseResult => {
			optionsConfig.Database = parseResult.GetValue (dbnameOption)?.Trim ();
			optionsConfig.Host = parseResult.GetValue (hostOption)?.Trim ();
			optionsConfig.Port = parseResult.GetValue (portOption);
			optionsConfig.User = parseResult.GetValue (usernameOption)?.Trim ();
			optionsConfig.PasswordPrompt = parseResult.GetValue (passwordpromptOption);
		});

		int commandParseResult = rootCommand.Parse (args).Invoke ();

		// show the help provided from messql, not from when connected to a database
		if (args.Length == 1 && (args[0] == "--help" || args[0] == "-?")) {
			return commandParseResult;
		}

		// look for matches in the config. first match is what gets used
		Config? matchingConfig = null;

		if (configs != null && configs.Count > 0) {
			foreach (var config in configs) {
				if (config.Database != optionsConfig.Database) {
					continue;
				}

				if (config.Port != optionsConfig.Port) {
					continue;
				}

				if (config.Host != optionsConfig.Host) {
					continue;
				}

				if (!string.IsNullOrEmpty (optionsConfig.User) && config.User != optionsConfig.User) {
					continue;
				}

				// not trying to match passwords. password will be attempted and possibly fail

				matchingConfig = config;
				break;
			}
		}

		if (matchingConfig != null) {
			// a blank user means integrated security (kerberos). no password needed
			if (!string.IsNullOrEmpty (matchingConfig.User) && string.IsNullOrEmpty (matchingConfig.Password)) {
				optionsConfig.PasswordPrompt = true;
			}

			optionsConfig.User = matchingConfig.User;
			optionsConfig.Password = matchingConfig.Password;
		}

		if (optionsConfig.PasswordPrompt == true) {
		passwordprompt:
			Console.Write ("password : ");
			string? password = Console.ReadLine ();

			if (string.IsNullOrEmpty (password)) {
				Console.WriteLine ("Invalid. a password must be supplied");
				goto passwordprompt;
			}

			optionsConfig.Password = password;
		}

		// clear the buffer
		Console.Clear ();

		// DataSource's ConnectionPropertyBag can't express TrustServerCertificate or ApplicationIntent
		// for MSSQL (and ignores Port), so the connection string is built with SqlConnectionStringBuilder.
		// no username => Integrated Security (Kerberos ticket, no password)
		var connectionStringBuilder = new SqlConnectionStringBuilder () {
			DataSource = $"tcp:{optionsConfig.Host},{optionsConfig.Port}",
			InitialCatalog = optionsConfig.Database ?? string.Empty,
			Encrypt = SqlConnectionEncryptOption.Mandatory,
			// set to false if the internal CA is installed
			TrustServerCertificate = true,
			ConnectTimeout = 15,
			ApplicationIntent = ApplicationIntent.ReadOnly
		};

		if (string.IsNullOrEmpty (optionsConfig.User)) {
			connectionStringBuilder.IntegratedSecurity = true;
		} else {
			connectionStringBuilder.UserID = optionsConfig.User;
			connectionStringBuilder.Password = optionsConfig.Password ?? string.Empty;
		}

		string connectionString = connectionStringBuilder.ConnectionString;

		// test the connection. output some version info
		string? versionstring = new Connect (new Connection (new SqlConnection (connectionString), new ConnectionPropertyBag () { DatabaseType = DatabaseType.MSSQL, Name = "mssql" })).Query ("select @@version as version;").Go<string?> ((cmd) => {
			using (IDataReader dr = cmd.ExecuteReader ()) {
				if (dr.Read ()) {
					return cmd.DRH!.GetString ("version");
				}

				return null;
			}
		});

		Console.WriteLine (versionstring);
		Console.WriteLine ();

	returnAfterExit:

		string prompt = $"{optionsConfig.Database}=# ";
		var builder = new System.Text.StringBuilder ();
		string? executingQuery = null;
		bool breakOut = false;
		// expanded display. needs to be 'global' and out of the loop
		bool expandedDisplay = false;

	start:
		if (breakOut == true) {
			return -1;
		}

		Console.Write (prompt);

		_ = builder.Clear ();
		executingQuery = null;
		char? lastNonWhiteSpaceCharacter = null;
		// index into the builder where the next typed character goes
		int cursor = 0;

		try {
			while (true) {

				bool tableDefinition = false;
				string tableDefinitionName = string.Empty;
				bool showSource = false;
				string sourceName = string.Empty;

				var input = Console.ReadKey (intercept: true);

				var currentInput = builder.ToString ();

				if (input.Key == ConsoleKey.Backspace) {
					// remove the character before the cursor
					if (cursor > 0) {
						string before = builder.ToString ();
						builder.Remove (cursor - 1, 1);
						cursor--;

						if (cursor == builder.Length && !isLineBreak (before[cursor])) {
							// deleting the last character on a line. no need to redraw
							Console.Write ("\b \b");
						} else {
							redrawInput (before, cursor + 1, builder.ToString (), cursor, prompt);
						}
					}

					continue;
				}

				if (input.Key == ConsoleKey.Delete) {
					// remove the character under the cursor
					if (cursor < builder.Length) {
						string before = builder.ToString ();
						builder.Remove (cursor, 1);
						redrawInput (before, cursor, builder.ToString (), cursor, prompt);
					}

					continue;
				}

				if (input.Key == ConsoleKey.Escape) {
					continue;
				}

				if (input.Key == ConsoleKey.Enter) {

					// just hitting [enter]
					if (builder.Length == 0) {
						Console.WriteLine ();
						Console.Write (prompt);
						continue;
					}

					// enter always acts on the whole entry, wherever the cursor happens to be
					if (cursor < builder.Length) {
						moveCursor (builder.ToString (), cursor, builder.Length, prompt);
						cursor = builder.Length;
					}

					_ = builder.Append (input.KeyChar);

					// the entry is only complete if it ends in a semi-colon. look at the actual buffer,
					// since it may have been recalled from history and edited
					lastNonWhiteSpaceCharacter = builder.ToString ().TrimEnd ().LastOrDefault ();

					// check if the first character in the buffer is a \
					// if so it's a special command
					if (builder[0] == '\\') {

						(string cmd, string? param) = parseMacro (builder.ToString ());

						switch (cmd) {
							case @"\q":
								Console.WriteLine ();
								Console.WriteLine ("exiting");
								breakOut = true;
								break;
							case @"\?":
								Console.WriteLine ();
								Console.WriteLine (writeHelp ());
								break;
							case @"\x":
								// change the display view for general queries
								if (expandedDisplay == false) {
									Console.WriteLine ();
									Console.WriteLine ("Expanded display is on.");
									expandedDisplay = true;
								} else {
									Console.WriteLine ();
									Console.WriteLine ("Expanded display is off.");
									expandedDisplay = false;
								}

								break;
							case @"\dt":
								if (string.IsNullOrEmpty (param) || param == "*") {
									// all tables in all schemas
									/*
														  List of relations
									 Schema |              Name              | Type  |    Owner
									--------+--------------------------------+-------+--------------
									 public | ab_template                    | table | nestinyadmin
									 public | accessrole                     | table | nestinyadmin
									*/
									executingQuery = $"select s.name as [schema_name], t.name as [table_name], t.type_desc from sys.schemas s join sys.tables t on t.schema_id = s.schema_id order by s.name, t.name;";
								} else {
									if (param.Contains ('.') == false) {
										// all tables in a schema
										executingQuery = $"select s.name as [schema_name], t.name as [table_name], t.type_desc from sys.schemas s join sys.tables t on t.schema_id = s.schema_id where s.name = '{param}' order by t.name;";
									} else {
										string[] dtparts = param.Split ('.');
										if (dtparts[1] == "*" || dtparts[1] == "") {
											// all tables in a schema. again
											executingQuery = $"select s.name as [schema_name], t.name as [table_name], t.type_desc from sys.schemas s join sys.tables t on t.schema_id = s.schema_id where s.name = '{dtparts[0]}' order by t.name;";
										} else if (dtparts[1].Contains ('*')) {
											// wildcard the table name
											dtparts[1] = dtparts[1].Replace ('*', '%');
											executingQuery = $"select s.name as [schema_name], t.name as [table_name], t.type_desc from sys.schemas s join sys.tables t on t.schema_id = s.schema_id where s.name = '{dtparts[0]}' and t.name like '{dtparts[1]}' order by t.name;";
										} else {
											// exact schema.tablename
											executingQuery = $"select s.name as [schema_name], t.name as [table_name], t.type_desc from sys.schemas s join sys.tables t on t.schema_id = s.schema_id where s.name = '{dtparts[0]}' and t.name = '{dtparts[1]}';";
										}
									}
								}

								break;
							case @"\d":
							case @"\d+":
								if (string.IsNullOrEmpty (param)) {
									Console.WriteLine ();
									Console.WriteLine ("\\d commands require schema.tablename");
									break;
								}

								if (param.EndsWith (";")) {
									param = param.TrimEnd (';');
								}

								tableDefinition = true;

								if (param.IndexOf ('.') == -1) {
									param = "dbo." + param;
								}

								string[] dparts = param.Split ('.');
								tableDefinitionName = param;

								string columndefs = "ORDINAL_POSITION as [Ord], COLUMN_NAME as [Column], DATA_TYPE as [Type], CHARACTER_MAXIMUM_LENGTH as [Maxlen], IS_NULLABLE as [Nullable], COLUMN_DEFAULT as [Default]";

								if (cmd.EndsWith ("+")) {
									columndefs += ", CHARACTER_SET_NAME as [Charset], COLLATION_NAME as [Collation]";
								}

								executingQuery = $"select {columndefs} FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '{dparts[0]}' and TABLE_NAME = '{dparts[1]}';";

								executingQuery += $"select name, type_desc, is_unique, is_primary_key from sys.indexes where [object_id] = OBJECT_ID('{param}');";
								// FK's
								executingQuery += $@"SELECT
	obj.object_id as [objid],
	obj.name AS FK_NAME,
    sch1.name AS [schema_name],
    tab1.name AS [table],
    col1.name AS [column],
	sch2.name AS [referenced_schema_name],
    tab2.name AS [referenced_table],
    col2.name AS [referenced_column]
FROM
	sys.foreign_key_columns fkc
	INNER JOIN sys.objects obj ON obj.object_id = fkc.constraint_object_id
	INNER JOIN sys.tables tab1 ON tab1.object_id = fkc.parent_object_id
	INNER JOIN sys.schemas sch1 ON tab1.schema_id = sch1.schema_id
	INNER JOIN sys.columns col1 ON col1.column_id = parent_column_id AND col1.object_id = tab1.object_id
	INNER JOIN sys.tables tab2 ON tab2.object_id = fkc.referenced_object_id
	INNER JOIN sys.schemas sch2 ON tab2.schema_id = sch2.schema_id
	INNER JOIN sys.columns col2 ON col2.column_id = referenced_column_id AND col2.object_id = tab2.object_id
where
	fkc.parent_object_id = OBJECT_ID('{param}');";

								/*
	SELECT CONSTRAINT_NAME
	FROM INFORMATION_SCHEMA.CONSTRAINT_TABLE_USAGE
	WHERE TABLE_NAME = 'Customers';

	SELECT name, type_desc, is_unique, is_primary_key
	FROM sys.indexes
	WHERE [object_id] = OBJECT_ID('dbo.Customers')

	select * from sys.objects where [object_id] = OBJECT_ID('foo.inventory');

	-- get FK's
	select * from INFORMATION_SCHEMA.TABLE_CONSTRAINTS where TABLE_SCHEMA = 'foo' and TABLE_NAME = 'inventory' and CONSTRAINT_TYPE = 'FOREIGN KEY';

	select * from sys.indexes where [object_id] = OBJECT_ID('foo.inventory');
	select name, type_desc, is_unique, is_primary_key from sys.indexes where [object_id] = OBJECT_ID('foo.inventory');
								*/

								break;
							case @"\dn":
								if (string.IsNullOrEmpty (param) || param == "*") {
									executingQuery = "select * from sys.schemas order by principal_id, name;";
								} else {
									if (param.Contains ('*')) {
										// wildcard the table name
										param = param.Replace ('*', '%');
										executingQuery = $"select * from sys.schemas where name like '{param}' order by principal_id, name;";
									} else {
										executingQuery = $"select * from sys.schemas where name = '{param}';";
									}
								}

								break;
							case @"\df":
							case @"\dfs":
								// list functions and stored procedures. \dfS includes system objects
								{
									string? dfSchema = null;
									string? dfName = null;
									string dfParam = (param ?? string.Empty).TrimEnd (';');

									if (dfParam.Contains ('.')) {
										string[] dfparts = dfParam.Split ('.', 2);
										dfSchema = dfparts[0];
										dfName = dfparts[1];
									} else if (dfParam != string.Empty) {
										dfName = dfParam;
									}

									string dfWhere = "o.type in ('P','PC','FN','IF','TF','FS','FT','AF','X')";
									if (cmd == @"\df") {
										dfWhere += " and o.is_ms_shipped = 0";
									}

									dfWhere += likeClause ("s.name", dfSchema);
									dfWhere += likeClause ("o.name", dfName);

									executingQuery = $"select s.name as [schema_name], o.name as [name], case o.type when 'P' then 'procedure' when 'PC' then 'procedure (clr)' when 'X' then 'procedure (extended)' when 'AF' then 'aggregate' when 'FN' then 'function (scalar)' when 'FS' then 'function (clr scalar)' when 'IF' then 'function (inline table)' when 'TF' then 'function (table)' when 'FT' then 'function (clr table)' else o.type_desc end as [type] from sys.objects o join sys.schemas s on s.schema_id = o.schema_id where {dfWhere} order by s.name, o.name;";
								}

								break;
							case @"\sf":
								// show the source of a function or stored procedure
								if (string.IsNullOrEmpty (param)) {
									Console.WriteLine ();
									Console.WriteLine ("\\sf requires a function or procedure name");
									break;
								}

								sourceName = param.TrimEnd (';');
								showSource = true;
								executingQuery = $"select m.definition from sys.sql_modules m where m.[object_id] = OBJECT_ID('{sourceName.Replace ("'", "''")}');";
								break;
							case @"\conninfo":
								// You are connected to database "nestiny" as user "nestinyadmin" on host "localhost" (address "::1") at port "54320".
								Console.WriteLine ();
								Console.WriteLine ($"You are connected to database \"{optionsConfig.Database}\" as user \"{optionsConfig.User}\" on host \"{optionsConfig.Host}\" at port \"{optionsConfig.Port}\".");
								break;
							default:
								Console.WriteLine ();
								Console.WriteLine ("unknown command");
								break;
						}

						//goto start;
						_ = builder.Clear ();
					}

					// Enter can be for multi-line. check that the last non-whitespace character was a semi-colon.
					// if semi-colon it's time to execute. also store command history
					if (lastNonWhiteSpaceCharacter == ';' || executingQuery != null) {
						// eval the query
						// write the results
						// start over

						Console.WriteLine ();

						// special commands can populate the executingQuery variable
						if (executingQuery == null) {
							History.Add (builder.ToString ());
							executingQuery = builder.ToString ();
						}

						// Assumes that connection is a valid SqlConnection object.
						// replace all carriage returns and line feeds with a space. prevent problems with multi-line queries
						if (executingQuery != null) {

							executingQuery = executingQuery.Replace ('\n', ' ').Replace ('\r', ' ');

							using var sqlConnection = new SqlConnection (connectionString);
							using var adapter = new SqlDataAdapter (executingQuery, sqlConnection);
							using var dataset = new DataSet ();
							_ = adapter.Fill (dataset);

							if (dataset.Tables.Count == 0) {
								Console.WriteLine ("no results found");
								Console.Write (prompt);
								goto start;
							}

							if (showSource == true) {
								object? source = dataset.Tables[0].Rows.Count > 0 ? dataset.Tables[0].Rows[0][0] : null;
								if (source == null || source == DBNull.Value) {
									Console.WriteLine ($"no source found for \"{sourceName}\" (does not exist, has no T-SQL source, or is encrypted)");
								} else {
									Console.WriteLine (source.ToString ());
								}
							} else if (tableDefinition == true) {
								Console.WriteLine (displayTableDefinition (dataset, tableDefinitionName));
							} else {
								Console.WriteLine (displayResults (dataset.Tables[0], expandedDisplay));
							}
						}

						goto start;
					}

					if (breakOut == true) {
						break;
					}

					Console.WriteLine ();
					Console.Write (prompt);
					cursor = builder.Length;
					continue;
				}

				if (input.Key == ConsoleKey.Tab) {
					// tab completion?

					// for now allow tabs in formatting queries
					//continue;
				}

				if (input.Key == ConsoleKey.UpArrow) {
					string? previousCommand = History.GetPrevious ();

					if (!string.IsNullOrEmpty (previousCommand)) {
						replaceInput (builder, ref cursor, prompt, previousCommand);
					}

					continue;
				}

				if (input.Key == ConsoleKey.DownArrow) {
					string? nextCommand = History.GetNext ();

					// null means there is nothing to do (not browsing history, or nothing newer)
					if (nextCommand != null) {
						// an empty string means we've arrowed down past the newest entry: back to a blank prompt
						replaceInput (builder, ref cursor, prompt, nextCommand);
					}

					continue;
				}

				if (input.Key == ConsoleKey.LeftArrow) {
					if (cursor > 0) {
						moveCursor (builder.ToString (), cursor, cursor - 1, prompt);
						cursor--;
					}

					continue;
				}

				if (input.Key == ConsoleKey.RightArrow) {
					if (cursor < builder.Length) {
						moveCursor (builder.ToString (), cursor, cursor + 1, prompt);
						cursor++;
					}

					continue;
				}

				if (input.Key == ConsoleKey.Home) {
					// start of the current line
					int home = lineStart (builder.ToString (), cursor);
					moveCursor (builder.ToString (), cursor, home, prompt);
					cursor = home;
					continue;
				}

				if (input.Key == ConsoleKey.End) {
					// end of the current line
					string text = builder.ToString ();
					int end = cursor;
					while (end < text.Length && !isLineBreak (text[end])) {
						end++;
					}

					moveCursor (text, cursor, end, prompt);
					cursor = end;
					continue;
				}

				// ignore other non-printing keys (function keys, etc.)
				if (char.IsControl (input.KeyChar) && input.Key != ConsoleKey.Tab) {
					continue;
				}

				if (cursor == builder.Length) {
					_ = builder.Append (input.KeyChar);
					Console.Write (input.KeyChar);
					cursor++;
				} else {
					// inserting in the middle of the entry
					string before = builder.ToString ();
					_ = builder.Insert (cursor, input.KeyChar);
					cursor++;
					redrawInput (before, cursor - 1, builder.ToString (), cursor, prompt);
				}


			}
		} catch (Exception oops) {
			Console.WriteLine (oops.Message);
			goto returnAfterExit;
		}
		// finally? some cleanup/disposal/console reset?

		return commandParseResult;
	}

	private static bool isLineBreak (
		char c
	) => c == '\n' || c == '\r';

	// number of line breaks before the index. ie the zero-based row of that index within the entry
	private static int lineIndex (
		string text,
		int index
	) {
		int count = 0;
		for (int i = 0; i < index && i < text.Length; i++) {
			if (isLineBreak (text[i])) {
				count++;
			}
		}

		return count;
	}

	// index of the first character of the line containing the index
	private static int lineStart (
		string text,
		int index
	) {
		int i = index;
		while (i > 0 && !isLineBreak (text[i - 1])) {
			i--;
		}

		return i;
	}

	// place the cursor on screen at the index. top is the screen row of the entry's first line
	private static void setCursor (
		string text,
		int index,
		int top,
		string prompt
	) {
		int row = lineIndex (text, index);
		int col = prompt.Length + index - lineStart (text, index);
		Console.SetCursorPosition (Math.Min (col, Console.BufferWidth - 1), top + row);
	}

	// move the on-screen cursor from one index of the entry to another
	private static void moveCursor (
		string text,
		int from,
		int to,
		string prompt
	) {
		int top = Console.CursorTop - lineIndex (text, from);
		setCursor (text, to, top, prompt);
	}

	/// <summary>
	/// erase the displayed entry (including continuation lines), show the new text, and leave the cursor at newCursor
	/// </summary>
	private static void redrawInput (
		string oldText,
		int oldCursor,
		string newText,
		int newCursor,
		string prompt
	) {
		int oldLines = lineIndex (oldText, oldText.Length) + 1;
		int top = Math.Max (0, Console.CursorTop - lineIndex (oldText, oldCursor));

		string blank = new string (' ', Math.Max (0, Console.BufferWidth - 1));
		for (int i = 0; i < oldLines; i++) {
			Console.SetCursorPosition (0, top + i);
			Console.Write (blank);
		}

		Console.SetCursorPosition (0, top);

		// multi-line commands: every line gets its own prompt
		string[] parts = newText.Split (new char[] { '\n', '\r' });
		for (int i = 0; i < parts.Length; i++) {
			Console.Write (prompt);
			Console.Write (parts[i]);
			if (i < parts.Length - 1) {
				Console.Write (Environment.NewLine);
			}
		}

		// writing new lines at the bottom of the window scrolls it, so work out the top again
		int newTop = Console.CursorTop - (parts.Length - 1);
		setCursor (newText, newCursor, newTop, prompt);
	}

	/// <summary>
	/// replace the current entry (eg. with a history item). the cursor goes to the end
	/// </summary>
	private static void replaceInput (
		System.Text.StringBuilder builder,
		ref int cursor,
		string prompt,
		string replacement
	) {
		string oldText = builder.ToString ();
		_ = builder.Clear ();
		_ = builder.Append (replacement);
		redrawInput (oldText, cursor, replacement, replacement.Length, prompt);
		cursor = replacement.Length;
	}

	private static string displayResults (
		DataTable table,
		bool expandedDisplay = false
	) {

		int windowwidth = Console.WindowWidth;
		int rowcount = table.Rows.Count;
		int columncount = table.Columns.Count;

		var columnDefs = new ColumnDefinitions (table, windowwidth);

		var sb = new StringBuilder ();

		if (expandedDisplay) {
			// get widest name
			int widestnamewidth = columnDefs.Columns.ToList<ColumnConfig> ().Max (el => el.Name.Length);
			int datawidth = windowwidth - widestnamewidth - 3;

			int rownum = 1;
			foreach (System.Data.DataRow row in table.Rows) {
				_ = sb.AppendLine ($"-[ RECORD {rownum} ]".PadRight (windowwidth, '-'));

				string[] names = columnDefs.Columns.ToList<ColumnConfig> ().Select ((el) => { return el.Name.PadRight (widestnamewidth); }).ToArray<string> ();

				for (int i = 0; i < row.ItemArray.Length; i++) {
					_ = sb.Append (names[i]).Append (" | ").Append (SafeTruncAndPad (row.ItemArray[i]?.ToString (), datawidth)).Append (Environment.NewLine);
				}

				rownum++;
			}
		} else {
			string[] names = columnDefs.Columns.ToList<ColumnConfig> ().Select ((el) => { return SafeTruncAndPad (el.Name, el.Width); }).ToArray<string> ();

			_ = sb.AppendLine (string.Join<string> (" | ", names));
			_ = sb.AppendLine ("-".PadRight (windowwidth, '-'));

			foreach (System.Data.DataRow row in table.Rows) {
				for (int i = 0; i < row.ItemArray.Length; i++) {
					if (i > 0) {
						_ = sb.Append (" | ");
					}

					_ = sb.Append (SafeTruncAndPad (row.ItemArray[i]?.ToString (), columnDefs.Columns[i].Width));
				}

				_ = sb.Append (Environment.NewLine);
			}
		}

		_ = sb.AppendLine ();
		_ = sb.AppendLine ($"rows ({rowcount})");

		return sb.ToString ();
	}

	/*
	NON-expanded example from postgresql

									 Table "vault.account"
			   Column            |           Type           | Collation | Nullable | Default
	-----------------------------+--------------------------+-----------+----------+---------
	 id                          | text                     |           | not null |
	 name                        | text                     |           | not null |
	 account_type                | text                     |           | not null |
	 active                      | boolean                  |           | not null | true
	 create_dt                   | timestamp with time zone |           | not null | now()
	 available_balance           | numeric(13,2)            |           | not null | 0
	 account_num_hint            | text                     |           | not null |
	 available_balance_update_dt | timestamp with time zone |           | not null | now()
	Indexes:
		"account_pkey" PRIMARY KEY, btree (id)
	Referenced by:
		TABLE "vault.account_office" CONSTRAINT "account_office_account_id_fkey" FOREIGN KEY (account_id) REFERENCES vault.account(id)
		TABLE "vault.exchange" CONSTRAINT "exchange_account_id_fkey" FOREIGN KEY (account_id) REFERENCES vault.account(id) ON UPDATE CASCADE
		TABLE "vault.transaction" CONSTRAINT "transaction_account_paid_id_fkey" FOREIGN KEY (account_paid_id) REFERENCES vault.account(id)


	create table foo.inventory_reference (id int primary key identity, inventory_id int references foo.inventory(id) not null, description nvarchar(max) null);

	EXPANDED example from postgresql

	-[ RECORD 1 ]---------------+------------------------------
	id                          | acct_11gpdymtef1m6w
	name                        | cincy
	account_type                | checking
	active                      | t
	create_dt                   | 2021-10-13 18:18:35.746348+00
	available_balance           | 14278.59
	account_num_hint            | 0232
	available_balance_update_dt | 2023-05-18 02:40:11.903009+00
	-[ RECORD 2 ]---------------+------------------------------
	id                          | acct_11fs1yjt69rcjx
	name                        | cincy
	account_type                | checking
	active                      | f
	create_dt                   | 2020-12-09 13:27:01.346351+00
	available_balance           | 56.00
	account_num_hint            | 4900
	available_balance_update_dt | 2021-10-22 10:44:02.119291+00
	*/
	private static string displayTableDefinition (
		DataSet dataset,
		string tableName
	) {
		int windowwidth = Console.WindowWidth;

		var columnDefs = new ColumnDefinitions (dataset.Tables[0], windowwidth);

		var sb = new StringBuilder ();

		_ = sb.AppendLine ($"Table \"{tableName}\"");

		string[] names = columnDefs.Columns.ToList<ColumnConfig> ().Select ((el) => { return SafeTruncAndPad (el.Name, el.Width); }).ToArray<string> ();

		_ = sb.AppendLine (string.Join<string> (" | ", names));
		_ = sb.AppendLine ("-".PadRight (windowwidth, '-'));

		foreach (System.Data.DataRow row in dataset.Tables[0].Rows) {
			for (int i = 0; i < row.ItemArray.Length; i++) {
				if (i > 0) {
					_ = sb.Append (" | ");
				}

				_ = sb.Append (SafeTruncAndPad (row.ItemArray[i]?.ToString (), columnDefs.Columns[i].Width));
			}

			_ = sb.Append (Environment.NewLine);
		}

		_ = sb.AppendLine ();

		_ = sb.AppendLine ("Indexes:");

		if (dataset.Tables[1].Rows.Count == 0) {
			_ = sb.AppendLine ("    No indexes");
		} else {
			foreach (System.Data.DataRow row in dataset.Tables[1].Rows) {
				_ = sb.AppendLine ($"    \"{row.ItemArray[0]}\" {row.ItemArray[1]} {(row.ItemArray[3]!.ToString () == "True" ? "PRIMARY KEY" : (row.ItemArray[2]!.ToString () == "True" ? "UNIQUE" : ""))}");
			}
		}

		_ = sb.AppendLine ("Referenced by:");

		if (dataset.Tables[2].Rows.Count == 0) {
			_ = sb.AppendLine ("    No foreign keys");
		} else {
			foreach (System.Data.DataRow row in dataset.Tables[2].Rows) {
				_ = sb.AppendLine ($"    TABLE \"{row.ItemArray[5]}.{row.ItemArray[6]}\" CONSTRAINT \"{row.ItemArray[1]}\" FOREIGN KEY ({row.ItemArray[7]}) REFERENCES {row.ItemArray[2]}.{row.ItemArray[3]}({row.ItemArray[4]})");
			}
		}

		return sb.ToString ();
	}

	// pad to a column width, or truncate to fit it. truncated values end in an ellipsis
	public static string SafeTruncAndPad (
		string? val,
		int width
	) {
		if (width <= 0) {
			return string.Empty;
		}

		if (string.IsNullOrEmpty (val)) {
			return "".PadRight (width);
		}

		if (val.Length <= width) {
			return val.PadRight (width);
		}

		return val.Substring (0, width - 1) + "\u2026";
	}

	private static (string cmd, string? param) parseMacro (
		string input
	) {

		string[] parts = input.Split (new char[0], StringSplitOptions.RemoveEmptyEntries);

		return (parts[0].ToLower (), parts.Length > 1 ? parts[1] : null);
	}

	// builds an " and col = 'x'" / " and col like 'x%'" fragment. '*' is the wildcard. null/empty/'*' means no filter
	private static string likeClause (
		string column,
		string? pattern
	) {
		if (string.IsNullOrEmpty (pattern) || pattern == "*") {
			return string.Empty;
		}

		string escaped = pattern.Replace ("'", "''");

		if (escaped.Contains ('*')) {
			return $" and {column} like '{escaped.Replace ('*', '%')}'";
		}

		return $" and {column} = '{escaped}'";
	}

	// private static string stripPrompt (
	// 	ConfigExtension config,
	// 	string input
	// ) {
	// 	return input.Replace ($"{config.Database}=# ", "");
	// }

	private static string writeHelp () {
		return @"General
	\q                      quit mssqlcli

Help
	\?                      show this help

Informational
	(options: S = show system objects, + = additional detail)
  	\d[S+]                  list tables, views, and sequences
  	\d[S+]  NAME            describe table, view, sequence, or index
	\dt                     describe tables
	\dn     [PATTERN]       list schemas
	\df[S]  [PATTERN]       list functions and stored procedures (PATTERN: name or schema.name, * wildcard)
	\sf     NAME            show the source of a function or stored procedure

Connection
  \conninfo display information about current connection
";
	}

	private static IList<Config>? parseConfig () {
		// in the spirit of .pgpass - formatted as :
		// host:port:database:user:password

		string homedir = Environment.GetFolderPath (Environment.SpecialFolder.UserProfile);
		const string PASS_FILE = ".mssqlpass";

		if (!File.Exists (Path.Combine (homedir, PASS_FILE))) {
			return null;
		}

		string[] configlines = File.ReadAllLines (Path.Combine (homedir, PASS_FILE));

		if (configlines.Length == 0) {
			return null;
		}

		IList<Config> configs = new List<Config> ();

		foreach (string line in configlines) {

			// don't parse comments
			if (line.StartsWith ('#')) {
				continue;
			}

			string[] parts = line.Split (':');

			if (parts.Length != 5) {
				Console.WriteLine ($"line `{line}` is not valid. not enough parts. skipping");
				continue;
			}

			if (!int.TryParse (parts[1], out var port)) {
				Console.WriteLine ($"port `{parts[1]}` does not convert to an integer. skipping");
				continue;
			}

			configs.Add (new Config () {
				Host = parts[0],
				Port = port,
				Database = parts[2],
				User = parts[3],
				Password = parts[4]
			});
		}

		return configs;
	}
}
