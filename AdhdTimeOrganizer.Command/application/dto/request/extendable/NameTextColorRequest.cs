using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Command.application.dto.request.extendable;

public record NameTextColorRequest(
    string Name,
    string? Text,
    [ Required, StringLength(7)] string Color
) : NameTextRequest(Name, Text);