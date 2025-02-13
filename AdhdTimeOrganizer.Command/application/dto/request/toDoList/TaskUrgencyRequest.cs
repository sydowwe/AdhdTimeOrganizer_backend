using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.request.toDoList;

public record TaskUrgencyRequest(string Text, string Color, [ Required] short Priority) : TextColorRequest(Text, Color);