namespace Aegis.Server.AspNetCore.DTOs.Activation;

public class LogoutRequestDto
{
    public string SessionToken { get; set; } = string.Empty;
}
