using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Transcope;

internal sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string settingsFilePath;

    public AppSettingsStore()
    {
        string appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Transcope");

        settingsFilePath = Path.Combine(appDataDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(settingsFilePath))
        {
            return AppSettings.Empty;
        }

        try
        {
            string json = File.ReadAllText(settingsFilePath, Encoding.UTF8);
            StoredAppSettings stored = JsonSerializer.Deserialize<StoredAppSettings>(json, JsonOptions)
                ?? new StoredAppSettings();

            return new AppSettings(Decrypt(stored.DeepSeekApiKeyProtected));
        }
        catch
        {
            return AppSettings.Empty;
        }
    }

    public void SaveDeepSeekApiKey(string? apiKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);

        StoredAppSettings stored = new()
        {
            DeepSeekApiKeyProtected = Encrypt(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim())
        };

        string json = JsonSerializer.Serialize(stored, JsonOptions);
        File.WriteAllText(settingsFilePath, json, Encoding.UTF8);
    }

    private static string? Encrypt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        byte[] plaintext = Encoding.UTF8.GetBytes(value);
        byte[] ciphertext = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(ciphertext);
    }

    private static string? Decrypt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            byte[] ciphertext = Convert.FromBase64String(value);
            byte[] plaintext = ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return null;
        }
    }

    internal sealed record AppSettings(string? DeepSeekApiKey)
    {
        public static AppSettings Empty { get; } = new AppSettings(DeepSeekApiKey: null);
    }

    private sealed record StoredAppSettings
    {
        public string? DeepSeekApiKeyProtected { get; init; }
    }
}
