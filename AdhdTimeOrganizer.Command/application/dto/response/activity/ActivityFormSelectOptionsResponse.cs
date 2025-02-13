using AdhdTimeOrganizer.Common.application.dto.response.generic;

namespace AdhdTimeOrganizer.Command.application.dto.response.activity;

public class ActivityFormSelectOptionsResponse : SelectOptionResponse
{
    public SelectOptionResponse RoleOption { get; set; }
    public SelectOptionResponse? CategoryOption { get; set; }
}