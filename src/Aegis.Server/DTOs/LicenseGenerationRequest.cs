using Aegis.Enums;
using Aegis.Models;

namespace Aegis.Server.DTOs;

public class LicenseGenerationRequest
{
    public LicenseType LicenseType { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public Guid ProductId { get; init; }
    public string IssuedTo { get; init; } = string.Empty;
    public int? MaxActiveUsersCount { get; init; }
    public string? HardwareId { get; init; }
    public TimeSpan? SubscriptionDuration { get; init; }
    public Dictionary<Guid, Feature> Features { get; init; } = [];

}