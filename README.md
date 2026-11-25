# MessQL

MSSQL CLI client in the spirit of PostgreSQL's psql CLI client

_This code is a mess, apologies. PR's are welcome!_

I'm in the middle of a job change and I love PostgreSQL and `psql`.  
The new job is very MSSQL-heavy and I haven't used MSSQL in about 10 years.  
Back then their CLI was atrocious, surely it's decent now, right? Right?

The official MSSQL CLI is really an embarassment. Microsoft should be ashamed.

So I decided to take a stab at the main features I use from `psql`

### The Name

`MessQL` comes from two main things - the code is something i knocked out between jobs and it is a mess.  
The second thing is that in my head i tend to pronounce "MSSQL" as "MessQL"

## Installing

### Download a release

Download the ZIP for your operating system and CPU from the
[latest release](https://github.com/janoserdelyi/MessQL/releases/latest):

- Windows x64: `MessQL-win-x64.zip`
- Linux x64 or ARM64: `MessQL-linux-x64.zip` or `MessQL-linux-arm64.zip`
- macOS Intel or Apple Silicon: `MessQL-osx-x64.zip` or `MessQL-osx-arm64.zip`

Extract the ZIP and run `MessQL.exe` on Windows or `./MessQL` on Linux or macOS.
These builds include the .NET runtime; you don't need to install .NET or compile
the project. The operating system's native dependencies are still required.

### Build from source

Requires the .NET 10 SDK to build.

Publish a build to a fixed location (re-run this after pulling code changes):

```sh
cd src/
dotnet publish -c Release -o ~/.local/share/messql
```

Then make it runnable from any terminal. Add an alias to your `~/.zshrc` (or `~/.bashrc`):

```sh
alias messql="$HOME/.local/share/messql/MessQL"
```

and run `source ~/.zshrc`, or open a new terminal.

Alternatively, symlink the executable into a directory that is already on your `PATH`. Unlike an alias, this also works from scripts:

```sh
ln -s ~/.local/share/messql/MessQL ~/.local/bin/messql
```

## CLI Options

The `MessQL` binary accepts a few options - arguably the most important being help (`-h`, `--help`, `-?`);

This should result in :

```
Description:
  MSSQL cli in the spirit of psql

Usage:
  MessQL [options]

Options:
  -N, --alias <alias>        connect using an alias from ~/.mssqlpass. -d, -h, -p and -u override its values
  -d, --dbname <dbname>      database name to connect to
  -h, --host <host>          database server host [default: locahost]
  -p, --port <port>          database server port (default: "1433") [default: 1433]
  -u, --username <username>  database user name
  -W, --password             force password prompt (should happen automatically) [default: False]
  --version                  Show version information
  -?, -h, --help             Show help and usage information
```

`MessQL` will look for `~/.mssqlpass`. For those familiar with PostgereSQL's `.pgpass`, it follows the same format, with an alias added as the first field.
You can have any number of lines in the config with each config formatted as

```
alias:host:port:database:user:password
```

A line with no alias leaves the first field empty, so it starts with `:`

```
dev:dev-sql.example.com:1433:appdb:app_user:change-me
:other-sql.example.com:1433:reports:report_user:change-me
```

An empty `user` means integrated (Kerberos) security. An empty `password` means you are prompted for it. Blank lines and `#` comments are ignored. As in `.pgpass`, escape a literal `:` in any field with `\:` and a literal backslash with `\\` (a password of `pa:ss` is written `pa\:ss`). The older 5 field format (no alias) is still read.
[.mssqlpass.example](.mssqlpass.example) is a commented copy you can start from: `cp .mssqlpass.example ~/.mssqlpass && chmod 600 ~/.mssqlpass`

Connect with an alias using `-N` (`-a` is taken by psql's `--echo-all`, so it was avoided):

```
messql -N dev
```

Options you type win over the alias, e.g. `messql -N dev -d otherdb`. The alias's password is only used for the host, port and user it was written for, so overriding `-h`, `-p` or `-u` means it is not sent.

Without an alias, depending on which values you provide to the `MessQL` binary, it will seek to match values in `.mssqlpass` and will use the first matching line's values.

`MessQL` also reads `~/.messqlrc` on startup, in the spirit of psql's `.psqlrc`: one backslash command per line, blank lines and `--` comments ignored. Currently `\pset`, `\timing` and `\set` are supported there. For example, to always display NULL as `∅` and show query timings:

```
\pset null '∅'
\timing on
```

### Command history

Queries are saved to disk so up/down arrow history survives restarts, in the same format psql uses (`_HiStOrY_V2_`, one entry per line, multi-line queries stored on a single line and restored as multi-line). Two variables control it, set with `\set` or persisted in `~/.messqlrc`:

- `HISTFILE` where history is saved. defaults to `~/.messql_history`. end it with `-:DBNAME` to keep a separate history per database, e.g. `\set HISTFILE ~/.messql_history-:DBNAME`. setting it to `/dev/null` turns saving off
- `HISTSIZE` how many entries are kept. defaults to `500`. a negative number means no limit

```
\set HISTFILE ~/.messql_history-:DBNAME
\set HISTSIZE 1000
```

`\set` on its own lists the current values. `\set HISTFILE` or `\set HISTSIZE` without a value goes back to the default.

## Implemented features

This is where i would really love some PRs too - in addition to code structuring fun.

The following have been implemented in some form or fashion:

- Writing queries and getting results
- `\q` exit
- `\?` help
- `\x` expanded display
- `\pset null [STRING]` how NULL is displayed for the current session (the only `\pset` option so far)
- `\timing [on|off]` show how long each query took (`Time: 33.566 ms`). no argument toggles
- `\d`, `\d+` basic definitions of tables and views. accepts `name` (assumes `dbo`) or `schema.name`; `*` wildcards (e.g. `\d dbo.*`, `\d dbo.cust*`) describe every matching table and view, system ones included
- `\l`, `\l+`, `\lx` list databases. accepts a name pattern with `*` wildcards (e.g. `\l prod*`). `+` adds size and created date, `x` uses expanded output for that listing
- `\dt` table definitions. handles wildcards
- `\dn` schemas
- `\dv`, `\dvS` list views. accepts `schema` or `schema.name`, handles wildcards. `S` includes system views; giving a pattern (e.g. `\dv information_schema.*`) includes them too, as in psql
- `\df`, `\dfS` list functions and stored procedures. accepts `name` or `schema.name`, handles wildcards. `S` includes system objects
- `\sf name` show the source of a function or stored procedure
- `\sv name` show the definition of a view. accepts `name` or `schema.name`, works for system views like `information_schema.tables`
- `\c [DBNAME]`, `\connect` connect to another database on the same server, with the same host, port and credentials. on failure the previous connection is kept. with no argument shows the connection info. USER, HOST and PORT are not supported yet. with a `:DBNAME` `HISTFILE` the history switches too
- `\conninfo` connection info
- multi-line queries
- up/down arrow query history, saved to `HISTFILE` (see above)
- backspace
- still working on keyboard shortcuts for getting around and in-line editing, like left-right arrows

As you can see, there is **much** to implement though this handles my basic daily driver needs
