namespace mssqlcli;

// there are more options than belong in the .mssqlpass config object
public class ConfigExtension : Config
{
	public bool PasswordPrompt { get; set; } = false;
}
