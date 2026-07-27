namespace Sydowwe.Framework.infrastructure.persistence.encryption;

/// <summary>
/// Symmetric, authenticated encryption for individual high-sensitivity database columns
/// (GDPR Art. 32 — encryption at rest). Protects data against a compromised DB / stolen
/// backup / SQL-level read; it does NOT protect against a compromised app process, which
/// must hold the key to function. See <see cref="AesGcmFieldEncryptor"/>.
/// </summary>
public interface IFieldEncryptor
{
    /// <summary>Encrypts <paramref name="plaintext"/> into a self-describing, versioned token.</summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts a token produced by <see cref="Encrypt"/>. Values that are not encryption
    /// tokens (legacy plaintext written before this column was encrypted) are returned
    /// unchanged so reads keep working during/after the one-time backfill.
    /// </summary>
    string Decrypt(string stored);

    /// <summary>True when <paramref name="stored"/> is an encryption token (vs. legacy plaintext).</summary>
    bool IsEncrypted(string stored);
}