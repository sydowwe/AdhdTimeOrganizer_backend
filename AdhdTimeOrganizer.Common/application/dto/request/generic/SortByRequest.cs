namespace AdhdTimeOrganizer.Common.application.dto.request.generic;

public class SortByRequest
{
    public string Key { get; set; }
    public bool IsDesc { get; set; } = false;
}