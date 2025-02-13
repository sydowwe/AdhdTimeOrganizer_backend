using AdhdTimeOrganizer.Common.application.dto.response.@base;

namespace AdhdTimeOrganizer.Command.application.dto.response.activity;

public class ActivityResponse : NameTextResponse
{
    public bool IsOnToDoList { get; set; }
    public bool IsUnavoidable { get; set; }
    public NameTextColorIconResponse Role { get; set; }
    public NameTextColorIconResponse Category { get; set; }
}