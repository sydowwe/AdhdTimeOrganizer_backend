using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.activity;

namespace AdhdTimeOrganizer.application.dto.request;

public record WebExtensionHeartbeatRequest
{
    public required DateTime HeartbeatAt { get; set; }
    public required bool IsIdle { get; set; }
    public required WebExtensionWindowDto Window { get; set; }
}