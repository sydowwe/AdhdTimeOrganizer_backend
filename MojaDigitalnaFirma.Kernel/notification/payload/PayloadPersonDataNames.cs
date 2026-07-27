using System.Text.Json.Nodes;

namespace MojaDigitalnaFirma.Kernel.notification.payload;

/// <summary>
/// The person-data field names the payload PII contract forbids (see <see cref="INotificationPayload"/>) —
/// Slovak and English, since payload keys in this codebase are written in both.
/// <para>
/// One list, two enforcement points, deliberately: the reflection guard test walks payload <i>types</i> at
/// build time, and <see cref="ContainsPersonData"/> walks a posted JSON <i>document</i> at runtime. The
/// runtime check exists because the compile-time marker cannot constrain JSON arriving over the wire — the
/// manual-ops register endpoint accepts a free-form payload from an admin caller, which is the one path where
/// the type system gives no guarantee.
/// </para>
/// </summary>
public static class PayloadPersonDataNames
{
    /// <summary>
    /// Matched as a case-insensitive substring of the property name. Every token is unambiguous on its own —
    /// a bare <c>name</c> is deliberately <b>not</b> here, because non-person labels legitimately end in it
    /// (<c>itemName</c>, <c>leaveTypeName</c>, <c>ownerModule</c>). An unqualified <c>name</c> is still
    /// rejected, by the exact-match rule in <see cref="IsForbidden"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> ForbiddenFragments =
    [
        "employeename", "personname", "fullname", "firstname", "lastname", "givenname", "familyname",
        "surname", "meno", "priezvisko",
        "address", "adresa", "phone", "telefon", "mobil",
        "email", "birthnumber", "rodnecislo", "iban"
    ];

    /// <summary>True when a payload property by this name would carry person data.</summary>
    public static bool IsForbidden(string propertyName)
    {
        var lowered = propertyName.ToLowerInvariant();

        // An unqualified "name" on a payload is ambiguous by construction — qualify it (itemName, leaveTypeName)
        // or drop it. This is what stops the laziest way to reintroduce the leak.
        if (lowered is "name")
            return true;

        return ForbiddenFragments.Any(fragment => lowered.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// Walks a payload document (objects and arrays, to any depth) and returns the first offending property
    /// path, or null when the document is clean. Used to reject a hand-posted payload at the API boundary.
    /// </summary>
    public static string? ContainsPersonData(JsonNode? node, string path = "")
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    var childPath = path.Length == 0 ? key : $"{path}.{key}";
                    if (IsForbidden(key))
                        return childPath;
                    if (ContainsPersonData(value, childPath) is { } nested)
                        return nested;
                }

                return null;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    if (ContainsPersonData(array[i], $"{path}[{i}]") is { } nested)
                        return nested;

                return null;

            default:
                return null;
        }
    }
}