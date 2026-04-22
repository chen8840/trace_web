using SQLite;

namespace TraceWeb.Models;

public class HistoryRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string RootDomain { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateTime VisitTime { get; set; }
}
