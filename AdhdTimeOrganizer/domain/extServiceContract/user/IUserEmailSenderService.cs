using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.serviceContract;

namespace AdhdTimeOrganizer.domain.extServiceContract.user;

public interface IUserEmailSenderService : IEmailSenderService
{
    Task SendConfirmationLinkAsync(User user, string token);
    Task SendEmailChangeConfirmationAsync(User user, string newEmail, string token);
    Task SendPasswordResetLinkAsync(User user, string resetLink);
    Task SendPasswordResetCodeAsync(User user, string resetCode);
}