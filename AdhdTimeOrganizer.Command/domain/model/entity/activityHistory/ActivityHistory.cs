using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Common.domain.helper;
using AdhdTimeOrganizer.Common.infrastructure.persistence.converter;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activityHistory;

public class ActivityHistory : BaseEntityWithActivity
{
    public DateTime StartTimestamp { get; set; }
    public MyIntTime Length { get; set; }

    public DateTime EndTimestamp { get; set; }
}