namespace Aegis.Server.AspNetCore.DTOs.Activation;

public class ActivationResponse
{
    public bool Success { get; set; }
    public string SessionToken { get; set; } = string.Empty;
    public DateTime ExpiryUtc { get; set; }
    public int HeartbeatIntervalSec { get; set; }
    public DateTime? LicenseExpiryUtc { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
