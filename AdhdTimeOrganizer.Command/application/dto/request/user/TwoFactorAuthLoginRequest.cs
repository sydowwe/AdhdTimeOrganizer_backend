using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.Command.application.dto.request.user;

public record TwoFactorAuthLoginRequest(string TwoFactorAuthToken, [ Required] bool StayLoggedIn) : TwoFactorAuthRequest(TwoFactorAuthToken);