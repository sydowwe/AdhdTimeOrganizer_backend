using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Common.application.dto.request.@base;

public class NameTextRequest : IMyRequest
{
    [Required]
    [StringLength(50)] // Adjust length as needed
    public required string Name { get; set; }

    [StringLength(200)] // Adjust length as needed
    public string? Text { get; set; }
}