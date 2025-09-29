using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.serviceContract;

namespace AdhdTimeOrganizer.domain.extServiceContract.user;

public interface IUserEmailSenderService : IEmailSenderService
{
    Task SendConfirmationLinkAsync(User user, string email, string token);
    Task SendPasswordResetLinkAsync(User user, string email, string resetLink);
    Task SendPasswordResetCodeAsync(User user, string email, string resetCode);
}