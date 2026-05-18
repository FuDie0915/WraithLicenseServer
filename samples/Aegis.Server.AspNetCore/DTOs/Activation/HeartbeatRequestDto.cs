namespace Aegis.Server.AspNetCore.DTOs.Activation;

public class HeartbeatRequestDto
{
    public string SessionToken { get; set; } = string.Empty;
}
