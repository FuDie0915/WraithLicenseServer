namespace Aegis.Server.AspNetCore.DTOs.Activation;

public class ActivationRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public string HardwareId { get; set; } = string.Empty;
    public string ClientVersion { get; set; } = string.Empty;
}
