using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.activity;

namespace AdhdTimeOrganizer.Command.application.dto.request;

public record WebExtensionDataRequest(
    long ActivityId,
    [ Required, StringLength(255)]    string Domain,
    [ Required, StringLength(255)]    string Title,
    [ Required] int Duration,
    [ Required] DateTime StartTimestamp
) : ActivityIdRequest(ActivityId);