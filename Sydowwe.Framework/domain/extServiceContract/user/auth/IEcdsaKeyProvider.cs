using Microsoft.IdentityModel.Tokens;

namespace Sydowwe.Framework.domain.extServiceContract.user.auth;

public interface IEcdsaKeyProvider
{
    string SecurityAlgorithm { get; }
    ECDsaSecurityKey GetSigningKey();
    SigningCredentials GetSigningCredentials();
}