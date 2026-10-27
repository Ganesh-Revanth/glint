using System;
using System.Linq;
using Windows.Security.Credentials;
using Glint.Core.Security;

namespace Glint.Windows.Security;

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const string ResourceName = "Glint.DesktopAssistant";

    public void SaveApiKey(string provider, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider name cannot be empty.", nameof(provider));

        var vault = new PasswordVault();
        try
        {
            var existing = vault.Retrieve(ResourceName, provider);
            if (existing != null)
            {
                vault.Remove(existing);
            }
        }
        catch
        {
            // Ignore not found error
        }

        var credential = new PasswordCredential(ResourceName, provider, apiKey.Trim());
        vault.Add(credential);
    }

    public string? GetApiKey(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return null;

        var vault = new PasswordVault();
        try
        {
            var credential = vault.Retrieve(ResourceName, provider);
            if (credential == null)
                return null;

            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            return null;
        }
    }

    public void RemoveApiKey(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return;

        var vault = new PasswordVault();
        try
        {
            var credential = vault.Retrieve(ResourceName, provider);
            if (credential != null)
            {
                vault.Remove(credential);
            }
        }
        catch
        {
            // Ignore if already absent
        }
    }
}
