using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.activity;


public record ActivityIdRequest([ Required] long ActivityId) : IActivityIdRequest;