using System.CommandLine;
using System.ComponentModel.Design;
using System.Data;
using System.Linq;
using System.Net;
using System.Text;
using System.Timers;
using Azure.Core;
using Azure.Core.Extensions;
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
			Console.ReadLine ();
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

		var dbnameOption = new Option<string> (
			name: "--dbname",
			description: "database name to connect to"
		);
		dbnameOption.AddAlias ("-d");

		var hostOption = new Option<string> (
			name: "--host",
			description: "database server host",
			getDefaultValue: () => "locahost"
		);
		hostOption.AddAlias ("-h");

		var portOption = new Option<int> (
			name: "--port",
			description: "database server port (default: \"1433\")",
			getDefaultValue: () => 1433
		);
		portOption.AddAlias ("-p");

		var usernameOption = new Option<string> (
			name: "--username",
			description: "database user name"
		);
		usernameOption.AddAlias ("-u");

		var passwordpromptOption = new Option<bool> (
			name: "--password",
			description: "force password prompt (should happen automatically)",
			getDefaultValue: () => false
		);
		passwordpromptOption.AddAlias ("-W");

		rootCommand.AddOption (dbnameOption);
		rootCommand.AddOption (hostOption);
		rootCommand.AddOption (portOption);
		rootCommand.AddOption (usernameOption);
		rootCommand.AddOption (passwordpromptOption);

		var optionsConfig = new ConfigExtension ();

		rootCommand.SetHandler ((dbnameValue, hostValue, portValue, usernameValue, passwordpromptValue) => {
			optionsConfig.Database = dbnameValue?.Trim ();
			optionsConfig.Host = hostValue?.Trim ();
			optionsConfig.Port = portValue;
			optionsConfig.User = usernameValue?.Trim ();
			optionsConfig.PasswordPrompt = passwordpromptValue;
		}, dbnameOption, hostOption, portOption, usernameOption, passwordpromptOption);

		int commandParseResult = rootCommand.InvokeAsync (args).Result;

		// show the help provided from messql, not from when connected to a database
		if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h" || args[0] == "-?")) {
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
			if (string.IsNullOrEmpty (matchingConfig.Password)) {
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

		// try to connect. yes this is one big procedural mess
		ConnectionPropertyBag mssqlConnection = new ConnectionPropertyBag () {
			DatabaseType = DatabaseType.MSSQL,
			Name = "mssql",
			Server = optionsConfig.Host,
			Database = optionsConfig.Database,
			Username = optionsConfig.User,
			Password = optionsConfig.Password,
			Port = optionsConfig.Port.ToString ()
		};
		ConnectionManager.Instance.AddConnection (mssqlConnection);

		// test the connection. output some version info
		string? versionstring = new Connect ("mssql").Query ("select @@version as version;").Go<string?> ((cmd) => {
			using (IDataReader dr = cmd.ExecuteReader ()) {
				if (dr.Read ()) {
					return cmd.DRH.GetString ("version");
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

		builder.Clear ();
		executingQuery = null;
		char? lastNonWhiteSpaceCharacter = null;

		try {
			while (true) {

				bool tableDefinition = false;

				var input = Console.ReadKey (intercept: true);

				if (!Char.IsWhiteSpace (input.KeyChar)) {
					lastNonWhiteSpaceCharacter = input.KeyChar;
				}

				var currentInput = builder.ToString ();

				if (input.Key == ConsoleKey.Backspace) {
					// reduce the current stringbuilder length by one
					if (builder.Length > 0) {
						builder.Length--;

						Console.Write ("\b \b");

						// if the cursor position is less than the prompt length and the builder still has length,
						// we need to move the cursor position up
						if (Console.CursorLeft < prompt.Length) {
							var bufw = Console.BufferWidth;
							var bufh = Console.BufferHeight;

							// delete the current line
							//char[] blankline = new char[80];
							//Console.Write (blankline, 0, prompt.Length);

							string[] bufparts = builder.ToString ().Split (new char[] { '\n', '\r' });
							Console.CursorLeft = prompt.Length + bufparts[bufparts.Length - 1].Length;
							Console.CursorTop--;
						}
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

					builder.Append (input.KeyChar);

					// check if the first character in the buffer is a \
					// if so it's a special command
					if (builder[0] == '\\') {

						(string cmd, string? param) res = parseMacro (builder.ToString ());

						switch (res.cmd) {
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
								if (string.IsNullOrEmpty (res.param) || res.param == "*") {
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
									if (res.param.Contains ('.') == false) {
										// all tables in a schema
										executingQuery = $"select s.name as [schema_name], t.name as [table_name], t.type_desc from sys.schemas s join sys.tables t on t.schema_id = s.schema_id where s.name = '{res.param}' order by t.name;";
									} else {
										string[] dtparts = res.param.Split ('.');
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
								if (string.IsNullOrEmpty (res.param)) {
									Console.WriteLine ();
									Console.WriteLine ("\\d commands require schema.tablename");
									break;
								}

								if (res.param.EndsWith (";")) {
									res.param = res.param.TrimEnd (';');
								}

								tableDefinition = true;

								if (res.param.IndexOf ('.') == -1) {
									res.param = "dbo." + res.param;
								}
								string[] dparts = res.param.Split ('.');

								string columndefs = "ORDINAL_POSITION as [Ord], COLUMN_NAME as [Column], DATA_TYPE as [Type], CHARACTER_MAXIMUM_LENGTH as [Maxlen], IS_NULLABLE as [Nullable], COLUMN_DEFAULT as [Default]";

								if (res.cmd.EndsWith ("+")) {
									columndefs = columndefs + ", CHARACTER_SET_NAME as [Charset], CCOLLATION_NAME as [Collation]";
								}

								executingQuery = $"select {columndefs} FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = '{dparts[0]}' and TABLE_NAME = '{dparts[1]}';";

								executingQuery += $"select name, type_desc, is_unique, is_primary_key from sys.indexes where [object_id] = OBJECT_ID('{res.param}');";
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
	fkc.parent_object_id = OBJECT_ID('{res.param}');";

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
								if (string.IsNullOrEmpty (res.param) || res.param == "*") {
									executingQuery = "select * from sys.schemas order by principal_id, name;";
								} else {
									if (res.param.Contains ('*')) {
										// wildcard the table name
										res.param = res.param.Replace ('*', '%');
										executingQuery = $"select * from sys.schemas where name like '{res.param}' order by principal_id, name;";
									} else {
										executingQuery = $"select * from sys.schemas where name = '{res.param}';";
									}
								}
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
						builder.Clear ();
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

							using var adapter = new SqlDataAdapter (executingQuery, (Microsoft.Data.SqlClient.SqlConnection)ConnectionManager.Instance.GetConnection ("mssql").BaseConnection);
							using var dataset = new DataSet ();
							adapter.Fill (dataset);

							if (dataset.Tables.Count == 0) {
								Console.WriteLine ("no results found");
								Console.Write (prompt);
								goto start;
							}

							if (tableDefinition == true) {
								Console.WriteLine (displayTableDefinition (dataset));
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
						builder.Clear ();
						builder.Append (previousCommand);
						// set the last non-whitespace character
						lastNonWhiteSpaceCharacter = previousCommand.Last ();

						do { Console.Write ("\b \b"); } while (Console.CursorLeft > 0);
						Console.Write (prompt);

						// i need to handle multi-line commands
						string[] cmdparts = builder.ToString ().Split (new char[] { '\n', '\r' });

						Console.CursorLeft = 0;
						for (int i = 0; i < cmdparts.Length; i++) {
							Console.Write (prompt);
							//Console.CursorLeft = prompt.Length;
							Console.Write (cmdparts[i]);
							if (i < cmdparts.Length - 1) {
								Console.Write (System.Environment.NewLine);
							}
						}
					}

					continue;
				}
				if (input.Key == ConsoleKey.DownArrow) {
					string? nextCommand = History.GetNext ();

					if (!string.IsNullOrEmpty (nextCommand)) {
						builder.Clear ();
						builder.Append (nextCommand);
						// set the last non-whitespace character
						lastNonWhiteSpaceCharacter = nextCommand.TrimEnd ().Last ();

						// clear anything that's there?
						do { Console.Write ("\b \b"); } while (Console.CursorLeft > 0);
						Console.Write (prompt);

						// i need to handle multi-line commands
						string[] cmdparts = builder.ToString ().Split (new char[] { '\n', '\r' });

						Console.CursorLeft = 0;
						for (int i = 0; i < cmdparts.Length; i++) {
							Console.Write (prompt);
							//Console.CursorLeft = prompt.Length;
							Console.Write (cmdparts[i]);
							if (i < cmdparts.Length - 1) {
								Console.Write (System.Environment.NewLine);
							}
						}
					}

					continue;
				}
				if (input.Key == ConsoleKey.LeftArrow) {
					// this can get prety hairy. i need to track the position in the builder too
					// and with multi-line it gets even more wonky

					//if (Console.CursorLeft <= prompt.Length) {
					//	continue;
					//}
					//Console.CursorLeft--;
					continue;
				}
				if (input.Key == ConsoleKey.RightArrow) {
					//input = Console.ReadKey (intercept: true);
					continue;
				}

				builder.Append (input.KeyChar);
				Console.Write (input.KeyChar);

			}
		} catch (Exception oops) {
			Console.WriteLine (oops.Message);
			goto returnAfterExit;
		}
		// finally? some cleanup/disposal/console reset?



		return commandParseResult;
	}

	private static string displayResults (
		DataTable table,
		bool expandedDisplay = false
	) {

		int windowwidth = Console.WindowWidth;
		int rowcount = table.Rows.Count;
		int columncount = table.Columns.Count;

		var columnDefs = new ColumnDefinitions (table, windowwidth);

		StringBuilder sb = new StringBuilder ();

		if (expandedDisplay) {
			// get widest name
			int widestnamewidth = columnDefs.Columns.ToList<ColumnConfig> ().Max (el => el.Name.Length);
			int datawidth = windowwidth - widestnamewidth - 3;

			int rownum = 1;
			foreach (System.Data.DataRow row in table.Rows) {
				sb.AppendLine ($"-[ RECORD {rownum} ]".PadRight (windowwidth, '-'));

				string[] names = columnDefs.Columns.ToList<ColumnConfig> ().Select ((el) => { return el.Name.PadRight (widestnamewidth); }).ToArray<string> ();

				for (int i = 0; i < row.ItemArray.Length; i++) {
					sb.Append (names[i]).Append (" | ").Append (safeTruncAndPad (row.ItemArray[i]?.ToString (), datawidth)).Append (System.Environment.NewLine);
				}
				rownum++;
			}
		} else {
			string[] names = columnDefs.Columns.ToList<ColumnConfig> ().Select ((el) => { return el.Name.PadRight (el.Width); }).ToArray<string> ();

			sb.AppendLine (string.Join<string> (" | ", names));
			sb.AppendLine ("-".PadRight (windowwidth, '-'));

			foreach (System.Data.DataRow row in table.Rows) {
				for (int i = 0; i < row.ItemArray.Length; i++) {
					if (i > 0) {
						sb.Append (" | ");
					}
					sb.Append (safeTruncAndPad (row.ItemArray[i]?.ToString (), columnDefs.Columns[i].Width));
				}
				sb.Append (System.Environment.NewLine);
			}
		}

		sb.AppendLine ();
		sb.AppendLine ($"rows ({rowcount})");

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
		DataSet dataset
	) {
		int windowwidth = Console.WindowWidth;

		var columnDefs = new ColumnDefinitions (dataset.Tables[0], windowwidth);

		StringBuilder sb = new StringBuilder ();

		sb.AppendLine ($"Table '{dataset.Tables[0].TableName}'");

		string[] names = columnDefs.Columns.ToList<ColumnConfig> ().Select ((el) => { return el.Name.PadRight (el.Width); }).ToArray<string> ();

		sb.AppendLine (string.Join<string> (" | ", names));
		sb.AppendLine ("-".PadRight (windowwidth, '-'));

		foreach (System.Data.DataRow row in dataset.Tables[0].Rows) {
			for (int i = 0; i < row.ItemArray.Length; i++) {
				if (i > 0) {
					sb.Append (" | ");
				}
				sb.Append (safeTruncAndPad (row.ItemArray[i]?.ToString (), columnDefs.Columns[i].Width));
			}
			sb.Append (System.Environment.NewLine);
		}

		sb.AppendLine ();

		sb.AppendLine ("Indexes:");

		if (dataset.Tables[1].Rows.Count == 0) {
			sb.AppendLine ("    No indexes");
		} else {
			foreach (System.Data.DataRow row in dataset.Tables[1].Rows) {
				sb.AppendLine ($"    \"{row.ItemArray[0]}\" {row.ItemArray[1]} {(row.ItemArray[3]!.ToString () == "True" ? "PRIMARY KEY" : (row.ItemArray[2]!.ToString () == "True" ? "UNIQUE" : ""))}");
			}
		}

		sb.AppendLine ("Referenced by:");

		if (dataset.Tables[2].Rows.Count == 0) {
			sb.AppendLine ("    No foreign keys");
		} else {
			foreach (System.Data.DataRow row in dataset.Tables[2].Rows) {
				sb.AppendLine ($"    TABLE \"{row.ItemArray[5]}.{row.ItemArray[6]}\" CONSTRAINT \"{row.ItemArray[1]}\" FOREIGN KEY ({row.ItemArray[7]}) REFERENCES {row.ItemArray[2]}.{row.ItemArray[3]}({row.ItemArray[4]})");
			}
		}

		return sb.ToString ();
	}

	// truncate to fit a column width
	public static string safeTruncAndPad (
		string? val,
		int width
	) {
		if (string.IsNullOrEmpty (val)) {
			return "".PadRight (width);
		}

		if (val.Length < width) {
			return val.PadRight (width);
		}

		if (val.Length == width) {
			return val;
		}

		return val.Substring (0, width);
	}

	private static (string cmd, string? param) parseMacro (
		string input
	) {

		string[] parts = input.Split (new char[0], StringSplitOptions.RemoveEmptyEntries);

		return (parts[0].ToLower (), parts.Length > 1 ? parts[1] : null);
	}

	private static string stripPrompt (
		ConfigExtension config,
		string input
	) {
		return input.Replace ($"{config.Database}=# ", "");
	}

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
			if (line.StartsWith('#')) {
				continue;
			}

			string[] parts = line.Split (':');

			if (parts.Length != 5) {
				Console.WriteLine ($"line `{line}` is not valid. not enough parts. skipping");
				continue;
			}

			int port;
			if (!int.TryParse (parts[1], out port)) {
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
