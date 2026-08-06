using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.serviceContract;

namespace Sydowwe.Framework.domain.extServiceContract.user;

public interface IUserEmailSenderService<in TUser> : IEmailSenderService where TUser : BaseUser
{
    Task SendConfirmationLinkAsync(TUser user, string token);
    Task SendEmailChangeConfirmationAsync(TUser user, string newEmail, string token);
    Task SendPasswordResetLinkAsync(TUser user, string email, string resetLink);
    Task SendPasswordResetCodeAsync(TUser user, string resetCode);
}