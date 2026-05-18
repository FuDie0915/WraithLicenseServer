using Microsoft.Extensions.Caching.Memory;

namespace Aegis.Server.AspNetCore.Services;

/// <summary>
/// Stateless-ish session token issuer backed by IMemoryCache.
/// Token is a random GUID; lookup returns the (licenseKey, hardwareId) tuple.
/// Cache eviction = session expiry. Server restart wipes all sessions (clients re-activate).
/// </summary>
public class ClientSessionTokenService(IMemoryCache cache)
{
    public record SessionData(string LicenseKey, string HardwareId);

    private const string KeyPrefix = "ClientSession:";

    public (string Token, DateTime ExpiryUtc) Issue(string licenseKey, string hardwareId, TimeSpan validity)
    {
        var token = Guid.NewGuid().ToString("N");
        var expiry = DateTime.UtcNow.Add(validity);
        cache.Set(KeyPrefix + token, new SessionData(licenseKey, hardwareId), expiry);
        return (token, expiry);
    }

    public SessionData? Resolve(string token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        return cache.TryGetValue(KeyPrefix + token, out SessionData? data) ? data : null;
    }

    public DateTime Refresh(string token, TimeSpan validity)
    {
        var data = Resolve(token);
        if (data == null) throw new InvalidOperationException("Session token not found.");
        var newExpiry = DateTime.UtcNow.Add(validity);
        cache.Set(KeyPrefix + token, data, newExpiry);
        return newExpiry;
    }

    public void Revoke(string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        cache.Remove(KeyPrefix + token);
    }
}
