using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.request.toDoList;

public record TimePeriodRequest(string Text, string Color, [ Required] int Length, bool IsHiddenInView = false) : TextColorRequest(Text, Color);