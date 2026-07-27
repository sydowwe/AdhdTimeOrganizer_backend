using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sydowwe.Framework.infrastructure.persistence.encryption;

namespace Sydowwe.Framework.infrastructure.persistence.converter;

/// <summary>
/// Encrypts a string property at rest via <see cref="IFieldEncryptor"/>: the model value is
/// plaintext, the stored column value is a versioned encryption token. Apply with
/// <c>EntityBuilderExtensions.EncryptedColumn</c> rather than constructing directly.
/// </summary>
public class EncryptedStringConverter(IFieldEncryptor encryptor) : ValueConverter<string, string>(
    plaintext => encryptor.Encrypt(plaintext),
    stored => encryptor.Decrypt(stored)
);