using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// A <c>[Fact]</c> that runs only when the Windows Spell Checking service is
/// registered AND an <c>en-US</c> dictionary is installed on this host. When
/// either is missing the test surfaces as SKIPPED (with the reason) instead of
/// silently returning PASS from inside the body.
/// </summary>
/// <remarks>
/// Same zero-dependency xUnit v2 idiom as <see cref="RequiresBackendFactAttribute"/>:
/// the discoverer reads <see cref="FactAttribute.Skip"/> after instantiating the
/// attribute, so the availability probe runs at discovery time and unavailable
/// hosts surface as first-class skip results in the run summary. Unlike the
/// render-backend probe, activating the spell-check factory needs an STA thread
/// with COM initialized, so the probe spins up a dedicated STA thread (mirroring
/// the tests' own <c>RunSta</c> helper) and caches its one-shot result so the two
/// gated tests do not each re-activate COM during discovery.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class RequiresSpellCheckFactAttribute : FactAttribute
{
    public RequiresSpellCheckFactAttribute()
    {
        if (!SpellCheckAvailabilityProbe.IsAvailable(out string reason))
        {
            Skip = reason;
        }
    }
}

/// <summary>
/// One-shot probe for the exact Windows Spell Checking surface the gated tests
/// need: factory activation (via <c>CoCreateInstance</c>, not the AOT-hostile
/// coclass), <c>en-US</c> language support, and successful <c>ISpellChecker</c>
/// creation — the same chain <see cref="SpellChecker.IsAvailable"/> depends on.
/// Any failure is reported (never thrown) so test discovery cannot crash, and the
/// reason distinguishes an unregistered service from a missing dictionary so a
/// skip on CI is self-explanatory.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SpellCheckAvailabilityProbe
{
    // Every gated test builds its checker for en-US, so that is what we probe.
    private const string ProbeLanguage = "en-US";

    // REGDB_E_CLASSNOTREG — the spell-check service is not registered on this SKU
    // (e.g. some Server cores). A tolerated "unavailable" reason, NOT the
    // NotSupportedException the AOT rewrite guards against.
    private const int REGDB_E_CLASSNOTREG = unchecked((int)0x80040154);

    private const uint COINIT_APARTMENTTHREADED = 0x2;

    private static readonly Lazy<(bool Available, string Reason)> Cached =
        new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static bool IsAvailable(out string unavailableReason)
    {
        (bool available, string reason) = Cached.Value;
        unavailableReason = reason;
        return available;
    }

    private static (bool, string) Probe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, "Windows Spell Checking is only available on Windows.");
        }

        (bool, string) result = (false, "Windows Spell Checking probe did not run.");
        var thread = new Thread(() =>
        {
            int hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
            try
            {
                result = ProbeOnSta();
            }
            catch (Exception ex)
            {
                result = (false, $"Windows Spell Checking probe failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (hr >= 0)
                {
                    CoUninitialize();
                }
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static (bool, string) ProbeOnSta()
    {
        nint factory;
        try
        {
            factory = SpellCheckComInterop.CreateFactory();
        }
        catch (COMException ex) when (ex.HResult == REGDB_E_CLASSNOTREG)
        {
            return (false, "Windows Spell Checking service is not registered on this host (REGDB_E_CLASSNOTREG).");
        }

        nint checker = 0;
        try
        {
            if (!SpellCheckComInterop.IsSupported(factory, ProbeLanguage))
            {
                return (false, $"Windows Spell Checking has no '{ProbeLanguage}' dictionary installed on this host.");
            }

            checker = SpellCheckComInterop.CreateSpellChecker(factory, ProbeLanguage);
            if (checker == 0)
            {
                return (false, $"Windows Spell Checking could not create an '{ProbeLanguage}' checker on this host.");
            }

            return (true, string.Empty);
        }
        finally
        {
            SpellCheckComInterop.Release(checker);
            SpellCheckComInterop.Release(factory);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
