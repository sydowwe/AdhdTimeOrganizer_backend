using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record TwoFactorAuthRequest([ Required] string Token) : IMyRequest;