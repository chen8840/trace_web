namespace TraceWeb.Models;

public class DomainGroup
{
    public string RootDomain { get; set; } = string.Empty;

    public string LastUrl { get; set; } = string.Empty;

    public DateTime LastVisit { get; set; }

    public int VisitCount { get; set; }
}
