namespace mssqlcli;

public class Config
{
	// optional name for the line, used with -N/--alias. null when the line has none
	public string? Alias { get; set; }
	public string? Host { get; set; }
	public int? Port { get; set; }
	public string? Database { get; set; }
	public string? User { get; set; }
	public string? Password { get; set; }

	// the password is deliberately left out so this can't leak it into output
	public override string ToString () {
		return $"{Alias}:{Host}:{Port}:{Database}:{User}";
	}
}
