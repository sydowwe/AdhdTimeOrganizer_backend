using System.Security.Cryptography;
using System.Text;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.infrastructure.persistence.encryption;

/// <summary>
/// AES-256-GCM column encryptor. The 256-bit key is read from the <c>FIELD_ENCRYPTION_KEY</c>
/// environment variable (base64, 32 bytes) — it lives only in the deployment's <c>.env</c>
/// (git-ignored), never in the repo or appsettings.
/// <para>
/// Encryption is randomized (a fresh 96-bit nonce per value), so the same plaintext yields
/// different ciphertext every time. That gives the strongest at-rest protection but means
/// encrypted columns cannot be queried by value or made unique — acceptable here because
/// <c>BirthNumber</c> / <c>Iban</c> are only ever read by row id, never filtered on.
/// </para>
/// <para>
/// Stored token: <c>"enc:v1:" + base64(nonce(12) ‖ tag(16) ‖ ciphertext)</c>. The literal
/// prefix makes detection unambiguous (legacy plaintext is passed through untouched) and
/// leaves room to rotate to a future <c>v2</c> key without a big-bang re-encrypt.
/// </para>
/// Registered as a DI singleton (<see cref="ISingletonService"/>) for app code; the EF value
/// converter reaches the same logic statically via <see cref="Shared"/> because model building
/// runs without DI.
/// </summary>
public sealed class AesGcmFieldEncryptor : IFieldEncryptor, ISingletonService
{
    public const string KeyEnvVar = "FIELD_ENCRYPTION_KEY";
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12; // AES-GCM standard nonce
    private const int TagSize = 16; // AES-GCM standard tag

    // Shared instance for the EF value converter, which is built during OnModelCreating with no
    // access to the DI container. Lazy so the key is read only once, after Env.Load() has run.
    private static readonly Lazy<AesGcmFieldEncryptor> LazyShared = new(() => new AesGcmFieldEncryptor());
    public static AesGcmFieldEncryptor Shared => LazyShared.Value;

    private readonly byte[] _key;

    public AesGcmFieldEncryptor()
    {
        var base64Key = Helper.GetEnvVar(KeyEnvVar);
        try
        {
            _key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{KeyEnvVar} must be a base64-encoded value.");
        }

        if (_key.Length != 32)
            throw new InvalidOperationException(
                $"{KeyEnvVar} must decode to 32 bytes (256-bit AES key); got {_key.Length}.");
    }

    public bool IsEncrypted(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);

    public string Encrypt(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipherBytes = new byte[plainBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Layout: nonce ‖ tag ‖ ciphertext
        var payload = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, payload, NonceSize + TagSize, cipherBytes.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    public string Decrypt(string stored)
    {
        // Legacy plaintext (written before the column was encrypted) flows through unchanged.
        if (!IsEncrypted(stored))
            return stored;

        var payload = Convert.FromBase64String(stored[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted field payload is truncated.");

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipherBytes = payload.AsSpan(NonceSize + TagSize);
        var plainBytes = new byte[cipherBytes.Length];

        using var aes = new AesGcm(_key, TagSize);
        // Throws CryptographicException if the key is wrong or the data was tampered with.
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}