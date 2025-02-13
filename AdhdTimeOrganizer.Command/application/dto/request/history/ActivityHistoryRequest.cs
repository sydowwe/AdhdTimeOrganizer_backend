using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.activity;
using AdhdTimeOrganizer.Common.domain.helper;

namespace AdhdTimeOrganizer.Command.application.dto.request.history;

public record ActivityHistoryRequest(long ActivityId, [ Required] DateTime StartTimestamp, [ Required] MyIntTime Length) : ActivityIdRequest(ActivityId);