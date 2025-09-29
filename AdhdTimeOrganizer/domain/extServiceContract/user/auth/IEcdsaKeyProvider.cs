using Microsoft.IdentityModel.Tokens;

namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public interface IEcdsaKeyProvider
{
    string SecurityAlgorithm { get; }
    ECDsaSecurityKey GetSigningKey();
    SigningCredentials GetSigningCredentials();
}