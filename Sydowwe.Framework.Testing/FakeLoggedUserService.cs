using System.Security.Claims;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;

namespace Sydowwe.Framework.Testing;

public class FakeLoggedUserService : ILoggedUserService
{
    public const long TestUserId = 999L;
    public const string TestUserEmail = "test@test.com";

    private readonly long _userId;

    public FakeLoggedUserService(long? userId = null)
    {
        _userId = userId ?? TestUserId;
    }

    public ClaimsPrincipal? LoggedUserPrincipal => null;
    public bool IsAuthenticated => true;
    public IEnumerable<string> GetRoles => [nameof(UserRoleEnum.Admin)];
    public string GetEmail => TestUserEmail;
    public long GetUserId => _userId;
    public long? GetUserIdOrNull => _userId;
}