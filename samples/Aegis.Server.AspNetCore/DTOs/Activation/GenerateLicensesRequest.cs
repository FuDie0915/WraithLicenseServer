namespace Aegis.Server.AspNetCore.DTOs.Activation;

public class GenerateLicensesRequest
{
    public int Count { get; set; } = 1;
    public int ValidityDays { get; set; } = 30;
    public int MaxConcurrentUsers { get; set; } = 1;
    public string IssuedTo { get; set; } = "Wraith User";
    public string Issuer { get; set; } = "Wraith";
}
