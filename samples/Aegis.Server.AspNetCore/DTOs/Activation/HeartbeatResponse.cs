namespace Aegis.Server.AspNetCore.DTOs.Activation;

public class HeartbeatResponse
{
    public bool Valid { get; set; }
    public DateTime NewExpiryUtc { get; set; }
    public DateTime? LicenseExpiryUtc { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
