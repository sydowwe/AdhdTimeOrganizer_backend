namespace AdhdTimeOrganizer.Command.application.dto.request.extendable;

using System.ComponentModel.DataAnnotations;
public record NameTextColorIconRequest(
    string Name,
    string? Text,
    string Color,
    [ StringLength(255)] string? Icon
) : NameTextColorRequest(Name, Text, Color);