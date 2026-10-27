using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Oriel.SecureStorage;

/// <summary>
/// Secrets of the app, such as tokens and passwords, encrypted for the user
/// who runs it: another user of the machine, or the file copied elsewhere,
/// cannot read them. Pages use it as <c>SecureStorage</c>; C# code calls it
/// directly.
/// </summary>
/// <remarks>
/// Windows only, for now: the secrets are one file in the app's data folder,
/// encrypted with DPAPI for the current user, with the app's name as
/// entropy. Every page of the app can read them: keep the secrets pages
/// should not see out of it.
/// </remarks>
/// <example>
/// <code>
/// app.Bind(new SecureStorage(app));
/// </code>
/// <code>
/// await SecureStorage.set("github-token", token);
/// const token = await SecureStorage.get("github-token");
/// </code>
/// </example>
[Service(Name = "SecureStorage")]
public sealed class SecureStorage
{
    private const int MaxKeyLength = 256;

    private readonly string _file;
    private readonly byte[] _entropy;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Keeps the secrets of <paramref name="app"/>, in its <see cref="OrielApp.DataDirectory"/>.</summary>
    public SecureStorage(OrielApp app)
        : this(Path.Combine(app.DataDirectory, "secrets.dat"), app.Options.Name)
    {
    }

    /// <summary>Keeps secrets in <paramref name="file"/>, encrypted with <paramref name="appName"/> as entropy.</summary>
    public SecureStorage(string file, string appName)
    {
        _file = Path.GetFullPath(file);
        _entropy = Encoding.UTF8.GetBytes("Oriel.SecureStorage:" + appName);
    }

    /// <summary>The secret of <paramref name="key"/>, or null when there is none.</summary>
    public async Task<string?> Get(string key)
    {
        Check(key);
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            return Load().GetValueOrDefault(key);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Keeps <paramref name="value"/> as the secret of <paramref name="key"/>, in place of the one before.</summary>
    public Task Set(string key, string value)
    {
        Check(key);
        ArgumentNullException.ThrowIfNull(value);
        return Change(secrets => secrets[key] = value);
    }

    /// <summary>Forgets the secret of <paramref name="key"/>.</summary>
    /// <returns>Whether there was one.</returns>
    public async Task<bool> Delete(string key)
    {
        Check(key);
        var deleted = false;
        await Change(secrets => deleted = secrets.Remove(key)).ConfigureAwait(false);
        return deleted;
    }

    /// <summary>The keys of the secrets kept, in order.</summary>
    public async Task<string[]> Keys()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            return [.. Load().Keys.Order(StringComparer.Ordinal)];
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task Change(Action<Dictionary<string, string>> change)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var secrets = Load();
            change(secrets);
            Save(secrets);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Dictionary<string, string> Load()
    {
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(_file))
            return secrets;
        using var doc = JsonDocument.Parse(Unprotect(File.ReadAllBytes(_file)));
        foreach (var property in doc.RootElement.EnumerateObject())
            secrets[property.Name] = property.Value.GetString() ?? "";
        return secrets;
    }

    /// <summary>Writes the secrets beside the file, then over it: a crash leaves the old file or the new one.</summary>
    private void Save(Dictionary<string, string> secrets)
    {
        using var json = new MemoryStream();
        using (var w = new Utf8JsonWriter(json))
        {
            w.WriteStartObject();
            foreach (var (key, value) in secrets)
                w.WriteString(key, value);
            w.WriteEndObject();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temporary = _file + ".tmp";
        File.WriteAllBytes(temporary, Protect(json.ToArray()));
        File.Move(temporary, _file, overwrite: true);
    }

    private byte[] Protect(byte[] data) =>
        OperatingSystem.IsWindows() ? ProtectWindows(data) : throw NotHere();

    private byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows() ? UnprotectWindows(data) : throw NotHere();

    [SupportedOSPlatform("windows")]
    private byte[] ProtectWindows(byte[] data) => ProtectedData.Protect(data, _entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private byte[] UnprotectWindows(byte[] data) => ProtectedData.Unprotect(data, _entropy, DataProtectionScope.CurrentUser);

    private static PlatformNotSupportedException NotHere() =>
        new("SecureStorage keeps secrets on Windows only, for now.");

    private static void Check(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
            throw new ArgumentException($"A key has 1 to {MaxKeyLength} characters.", nameof(key));
    }
}
