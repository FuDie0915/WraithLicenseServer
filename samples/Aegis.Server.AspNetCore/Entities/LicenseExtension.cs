using Aegis.Server.Entities;

namespace Aegis.Server.AspNetCore.Entities;

public class LicenseExtension
{
    public Guid LicenseId { get; set; }
    public License License { get; set; } = null!;

    public int ValidityDays { get; set; }
    public DateTime? FirstActivatedAt { get; set; }
}
