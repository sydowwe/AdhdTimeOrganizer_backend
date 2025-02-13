using AdhdTimeOrganizer.Command.domain.model.entity.user;
using AdhdTimeOrganizer.Common.domain.serviceContract;

namespace AdhdTimeOrganizer.Command.infrastructure.extService;

public interface IUserEmailSenderService : IEmailSenderService
{
    Task SendConfirmationLinkAsync(UserEntity user, string email, string confirmationLink);
    Task SendPasswordResetLinkAsync(UserEntity user, string email, string resetLink);
    Task SendPasswordResetCodeAsync(UserEntity user, string email, string resetCode);
}