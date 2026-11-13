namespace mssqlcli;

public class Config
{
	public string? Host { get; set; }
	public int? Port { get; set; }
	public string? Database { get; set; }
	public string? User { get; set; }
	public string? Password { get; set; }

	public override string ToString () {
		return $"{Host}:{Port}:{Database}:{User}:{Password}";
	}
}
