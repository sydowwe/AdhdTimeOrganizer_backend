using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.user;

public record ConnectGoogleCalendarRequest(
    [Required] string Code
);
