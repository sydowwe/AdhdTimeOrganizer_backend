using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.serviceContract;

namespace AdhdTimeOrganizer.domain.extServiceContract.user;

public interface IUserEmailSenderService : IEmailSenderService
{
    Task SendPasswordResetLinkAsync(User user, string email, string resetLink);
}