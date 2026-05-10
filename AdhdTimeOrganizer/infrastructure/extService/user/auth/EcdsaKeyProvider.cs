using System.Security.Cryptography;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using Microsoft.IdentityModel.Tokens;

namespace AdhdTimeOrganizer.infrastructure.extService.user.auth;

public class EcdsaKeyProvider : IEcdsaKeyProvider, ISingletonService, IDisposable
{
    private readonly ECDsa _ecdsa;
    private readonly ECDsaSecurityKey _signingKey;

    public string SecurityAlgorithm => SecurityAlgorithms.EcdsaSha256;

    public EcdsaKeyProvider(IConfiguration configuration)
    {
        var keyPath = configuration["ECDSA_PRIVATE_KEY_PATH"] ?? "secrets/ec_private.pem";
        var ecdsaPrivatePem = File.ReadAllText(keyPath);
        _ecdsa = ECDsa.Create();
        _ecdsa.ImportFromPem(ecdsaPrivatePem);
        _signingKey = new ECDsaSecurityKey(_ecdsa)
        {
            KeyId = "EcdsaKey"
        };
    }

    public ECDsaSecurityKey GetSigningKey() => _signingKey;
    public SigningCredentials GetSigningCredentials() => new(_signingKey, SecurityAlgorithm);

    public void Dispose()
    {
        _ecdsa?.Dispose();
        GC.SuppressFinalize(this);
    }
}