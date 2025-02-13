using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;

public class WebExtensionData : BaseEntityWithActivity
{
    public string Domain { get; set; }
    public string Title { get; set; }
    public int Duration { get; set; }
    public DateTime StartTimestamp { get; set; }
}