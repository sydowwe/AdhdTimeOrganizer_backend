using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.infrastructure.extService.user.auth;

public class EcdsaKeyProvider : IEcdsaKeyProvider, ISingletonService, IDisposable
{
    private readonly ECDsa _ecdsa;
    private readonly ECDsaSecurityKey _signingKey;

    public string SecurityAlgorithm => SecurityAlgorithms.EcdsaSha256;

    public EcdsaKeyProvider()
    {
        var ecdsaPrivatePem = File.ReadAllText(Helper.GetEnvVar("ECDSA_PRIVATE_KEY_PATH"));
        _ecdsa = ECDsa.Create();
        _ecdsa.ImportFromPem(ecdsaPrivatePem);
        _signingKey = new ECDsaSecurityKey(_ecdsa)
        {
            KeyId = "EcdsaKey",
            // Use a private, non-caching CryptoProviderFactory instead of the process-global
            // CryptoProviderFactory.Default cache. That cache keys signature providers by the key's
            // (type + KeyId + algorithm); because every provider instance shares the constant KeyId
            // above, a SignatureProvider cached by one instance can be handed to another. When this
            // provider is disposed (it owns _ecdsa), the cache would still reference the now-disposed
            // ECDsa and the next sign throws ObjectDisposedException. Opting out keeps each provider's
            // signing self-contained for its own lifetime. (Negligible per-sign cost.)
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
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