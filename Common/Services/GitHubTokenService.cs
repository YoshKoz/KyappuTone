using Windows.Security.Credentials;

namespace KyappuTone.Common.Services;

/// <summary>
/// Stores the optional fine-grained GitHub token used to read releases from the private
/// KyappuTone repository. The token never enters LocalSettings or the database.
/// </summary>
public static class GitHubTokenService
{
    private const string VaultResource = "KyappuTone.GitHub";
    private const string UserName = "token";

    public static bool HasToken => !string.IsNullOrWhiteSpace(Load());

    public static string? Load()
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(VaultResource, UserName);
            credential.RetrievePassword();
            return string.IsNullOrWhiteSpace(credential.Password) ? null : credential.Password;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Save(string token)
    {
        var value = token.Trim();
        if (value.Length == 0)
        {
            Remove();
            return;
        }

        var vault = new PasswordVault();
        Remove();
        vault.Add(new PasswordCredential(VaultResource, UserName, value));
    }

    public static void Remove()
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(VaultResource, UserName));
        }
        catch (Exception)
        {
            // No stored token.
        }
    }
}
