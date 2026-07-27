using Syncfusion.Licensing;

namespace AdhdTimeOrganizer.Scheduler.infrastructure;

/// <summary>
/// Registers the Syncfusion license once, on first document render.
/// Reads the key from the <c>SYNCFUSION_LICENSE_KEY</c> environment variable
/// (Community License key while under the free-tier thresholds).
/// If no key is present (e.g. local dev / tests) registration is skipped so the
/// host still boots; rendered output then carries Syncfusion's trial notice.
/// </summary>
public static class SyncfusionLicense
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
            return;

        var key = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY");
        if (!string.IsNullOrWhiteSpace(key))
            SyncfusionLicenseProvider.RegisterLicense(key);
    }
}