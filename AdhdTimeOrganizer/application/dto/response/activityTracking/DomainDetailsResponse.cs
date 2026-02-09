namespace AdhdTimeOrganizer.application.dto.response.activityTracking;

public record DomainDetailsResponse
{
    public string Domain { get; set; } = string.Empty;
    public int TotalSeconds { get; set; }
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int Entries { get; set; }  // Number of window records
    public List<PageVisitDto> Pages { get; set; } = new();
}

public record PageVisitDto
{
    public string Url { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;  // URL without domain for display
    public int TotalSeconds { get; set; }
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
}
