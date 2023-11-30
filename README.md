# MessQL
MSSQL CLI client in the spirit of PostgreSQL's psql CLI client

*This code is a mess, apologies. PR's are welcome!*

I'm in the middle of a job change and I love PostgreSQL and `psql`.  
The new job is very MSSQL-heavy and I haven't used MSSQL in about 10 years.  
Back then their CLI was atrocious, surely it's decent now, right? Right? 

The official MSSQL CLI is really an embarassment. Microsoft should be ashamed.

So I decided to take a stab at the main features I use from `psql`

### The Name
`MessQL` comes from two main things - the code is something i knocked out between jobs and it is a mess.  
The second thing is that in my head i tend to pronounce "MSSQL" as "MessQL"

## CLI Options

The `MessQL` binary accepts a few options - arguably the most important being help (`-h`, `--help`, `-?`);

This should result in :
```
Description:
  MSSQL cli in the spirit of psql

Usage:
  MessQL [options]

Options:
  -d, --dbname <dbname>      database name to connect to
  -h, --host <host>          database server host [default: locahost]
  -p, --port <port>          database server port (default: "1433") [default: 1433]
  -u, --username <username>  database user name
  -W, --password             force password prompt (should happen automatically) [default: False]
  --version                  Show version information
  -?, -h, --help             Show help and usage information
```

`MessQL` will look for `~/.mssqlpass`. For those familiar with PostgereSQL's `.pgpass`, it follows the same format.
You can have any number of lines in the config with each config formatted as 
```
host:port:database:user:password
```
Depending one which values you provide to the `MessQL` binary, it will seek to match values in `.mssqlpass` and will use the first matching line's values.

## Implemented features

This is where i would really love some PRs too - in addition to code structuring fun.

The following have been implemented in some form or fashion: 

- Writing queries and getting results
- `\q` exit
- `\?` help 
- `\x` expanded display
- `\d`, `\d+` basic definitions. handles wildcards to find objects
- `\dt` table definitions. handles wildcards
- `\dn` schemas
- `\conninfo` connection info
- multi-line queries
- still working out up/down arrow query history
- backspace
- still working on keyboard shortcuts for getting around and in-line editing, like left-right arrows

As you can see, there is **much** to implement though this handles my basic daily driver needs

