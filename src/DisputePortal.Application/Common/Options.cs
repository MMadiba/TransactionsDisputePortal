using System.ComponentModel.DataAnnotations;

namespace DisputePortal.Application.Common;

public sealed class DisputeOptions
{
    public const string SectionName = "Disputes";

    /// <summary>How many days after posting a customer may still dispute a transaction.</summary>
    [Range(1, 540)]
    public int WindowDays { get; set; } = 120;
}

public sealed class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>IANA time zone used to show dates and to interpret date filters.</summary>
    [Required]
    public string TimeZone { get; set; } = "Africa/Johannesburg";
}

public static class Roles
{
    public const string Customer = "Customer";
    public const string Agent = "Agent";

    public static readonly IReadOnlyList<string> All = [Customer, Agent];
}
