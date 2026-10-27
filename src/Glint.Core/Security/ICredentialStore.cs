namespace Glint.Core.Security;

public interface ICredentialStore
{
    void SaveApiKey(string provider, string apiKey);
    string? GetApiKey(string provider);
    void RemoveApiKey(string provider);
}
