using System.Security.Cryptography;
using System.Text.Json;

namespace Mirage.Shared.Security;

public enum ServerTrust
{
    /// <summary>No certificate on record for this server.</summary>
    FirstContact,
    /// <summary>Matches the one on record.</summary>
    Known,
    /// <summary>Differs from the one on record.</summary>
    Changed,
}

/// <summary>
/// Which certificate each server presented last time, so a different one can be refused.
/// Trust on first use: the servers are self-signed, so there is no chain to validate.
/// </summary>
public sealed class ServerPins
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, string> _pins;

    /// <summary>SHA-256 of a certificate's raw bytes, lower-case hex.</summary>
    public static string FingerprintOf(byte[] rawCertificate) =>
        Convert.ToHexStringLower(SHA256.HashData(rawCertificate));

    public ServerPins(string path)
    {
        _path = path;
        _pins = Load(path);
    }

    private static string KeyFor(string host, int port) => $"{host.Trim().ToLowerInvariant()}:{port}";

    /// <summary>Compares <paramref name="fingerprint"/> against the record. Records nothing.</summary>
    public ServerTrust Check(string host, int port, string fingerprint)
    {
        lock (_gate)
        {
            if (!_pins.TryGetValue(KeyFor(host, port), out string? known)) return ServerTrust.FirstContact;
            return string.Equals(known, fingerprint, StringComparison.OrdinalIgnoreCase)
                ? ServerTrust.Known
                : ServerTrust.Changed;
        }
    }

    /// <summary>The fingerprint on record, or null.</summary>
    public string? PinnedFingerprint(string host, int port)
    {
        lock (_gate) { return _pins.GetValueOrDefault(KeyFor(host, port)); }
    }

    /// <summary>Records the certificate to expect, replacing any previous one.</summary>
    public void Remember(string host, int port, string fingerprint)
    {
        lock (_gate)
        {
            _pins[KeyFor(host, port)] = fingerprint;
            Save();
        }
    }

    /// <summary>Drops the record. True if there was one.</summary>
    public bool Forget(string host, int port)
    {
        lock (_gate)
        {
            if (!_pins.Remove(KeyFor(host, port))) return false;
            Save();
            return true;
        }
    }

    public IReadOnlyDictionary<string, string> All
    {
        get { lock (_gate) { return new Dictionary<string, string>(_pins); } }
    }

    public void Reload()
    {
        lock (_gate) { _pins = Load(_path); }
    }

    // An unreadable store reads as empty, so every server becomes a first contact rather than
    // trusting a value that cannot be verified.
    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var read = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            return read is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(read, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        string? dir = Path.GetDirectoryName(_path);
        if (dir is { Length: > 0 }) Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(_pins, new JsonSerializerOptions { WriteIndented = true }));
    }
}
