using AdhdTimeOrganizer.Command.application.dto.response.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.response.activityHistory;

public class WebExtensionDataResponse : WithActivityResponse
{
    public string Domain { get; set; }
    public string Title { get; set; }
    public int Duration { get; set; }
    public DateTime StartTimestamp { get; set; }
}