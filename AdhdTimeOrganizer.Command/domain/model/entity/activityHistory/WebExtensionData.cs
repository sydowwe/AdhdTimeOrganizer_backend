using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;

public class WebExtensionData : BaseEntityWithActivity
{
    public required string Domain { get; set; }
    public required string Title { get; set; }
    public required int Duration { get; set; }
    public required DateTime StartTimestamp { get; set; }
}