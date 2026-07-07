using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression tests for the NativeAOT-safe Windows Spell Checking interop
/// (<c>SpellCheckComInterop</c>, consumed by <see cref="SpellChecker"/>).
///
/// <see cref="SpellChecker"/> used to activate the classic <c>SpellCheckerFactory</c> coclass
/// (<c>(ISpellCheckerFactory)new SpellCheckerFactoryClass()</c>), which throws
/// <see cref="NotSupportedException"/> under NativeAOT ("Built-in COM has been disabled"). Because
/// the constructor wraps activation in a broad try/catch, that throw was swallowed and real-time
/// spell-check silently stopped working in every PublishAot build — no crash, no error. These tests
/// drive the rewritten helper through <c>CoCreateInstance</c> + raw vtable dispatch WITHOUT ever
/// showing UI, so they run headless.
///
/// They execute under the JIT test host, so they cannot themselves prove the AOT compiler no longer
/// strips the path; what they DO prove is that the coclass-activation throw is gone (the primary
/// test deliberately drives <c>CreateFactory</c>, which — unlike the <see cref="SpellChecker"/>
/// constructor — does not swallow), that the vtable slot wiring / refcounting is self-consistent, and
/// that a real spell-check surface (IsSupported / CreateSpellChecker / Check + enumerator getters)
/// round-trips. End-to-end AOT behaviour is smoke-tested manually against a PublishAot build.
/// </summary>
[Collection("Application")]
[SupportedOSPlatform("windows")]
public class SpellCheckerComActivationTests
{
    // ISpellCheckerFactory (spellcheck.h).
    private static readonly Guid IID_ISpellCheckerFactory = new("8E018A9D-2415-4677-BF08-794EA61F94BB");

    // REGDB_E_CLASSNOTREG — the spell-check service is not registered on this SKU (e.g. some Server
    // cores). Tolerated as a skip; NOT to be confused with the NotSupportedException being guarded.
    private const int REGDB_E_CLASSNOTREG = unchecked((int)0x80040154);

    [Fact]
    public void Factory_Activates_ViaCoCreateInstance_WithoutBuiltInCom()
    {
        RunSta(() =>
        {
            nint factory;
            try
            {
                // The whole point: under NativeAOT the old coclass activation threw
                // NotSupportedException ("Built-in COM has been disabled"). CoCreateInstance + raw
                // vtable dispatch must not. A NotSupportedException here is intentionally NOT caught
                // — it propagates and fails the test, which is exactly the AOT regression guarded.
                factory = SpellCheckComInterop.CreateFactory();
            }
            catch (COMException ex) when (ex.HResult == REGDB_E_CLASSNOTREG)
            {
                // Spell-check service not registered on this SKU — treat as skip/pass.
                return;
            }

            Assert.NotEqual(0, factory);
            try
            {
                // A freshly activated factory must QI to ISpellCheckerFactory.
                AssertQuerySucceeds(factory, IID_ISpellCheckerFactory);
            }
            finally
            {
                SpellCheckComInterop.Release(factory);
            }
        });
    }

    [RequiresSpellCheckFact]
    public void Factory_RoundTrips_Check_ThroughVtableSlots_WhenLanguageAvailable()
    {
        RunSta(() =>
        {
            // The [RequiresSpellCheckFact] gate already probed factory activation and en-US support
            // as available on this host, so both are asserted here rather than silently skipped
            // mid-body — the old `catch (REGDB_E_CLASSNOTREG) return` / `if (!IsSupported) return`
            // recorded PASS and hid real regressions.
            nint factory = SpellCheckComInterop.CreateFactory();
            Assert.NotEqual(0, factory);

            nint checker = 0;
            try
            {
                // IsSupported (slot 4) must agree with the discovery-time probe.
                Assert.True(SpellCheckComInterop.IsSupported(factory, "en-US"),
                    "en-US must be supported: the [RequiresSpellCheckFact] gate probed it as available.");

                // CreateSpellChecker (slot 5) + Check (slot 4) + the IEnumSpellingError / ISpellingError
                // getters — the exact slots that were silently dead under AOT.
                checker = SpellCheckComInterop.CreateSpellChecker(factory, "en-US");
                Assert.NotEqual(0, checker);

                const string word = "asdfg"; // unambiguous non-word.
                nint enumErrors = SpellCheckComInterop.Check(checker, word);
                Assert.NotEqual(0, enumErrors);
                try
                {
                    int count = 0;
                    while (true)
                    {
                        nint error = SpellCheckComInterop.EnumSpellingErrorNext(enumErrors);
                        if (error == 0)
                        {
                            break;
                        }

                        try
                        {
                            uint start = SpellCheckComInterop.SpellingErrorStartIndex(error);
                            uint length = SpellCheckComInterop.SpellingErrorLength(error);
                            Assert.True(length > 0, "spelling error reported a zero length");
                            Assert.True(start + length <= (uint)word.Length,
                                $"spelling error range [{start},{start + length}) exceeds the input '{word}'");
                            count++;
                        }
                        finally
                        {
                            SpellCheckComInterop.Release(error);
                        }
                    }

                    Assert.True(count >= 1, $"expected at least one spelling error for '{word}'");
                }
                finally
                {
                    SpellCheckComInterop.Release(enumErrors);
                }
            }
            finally
            {
                SpellCheckComInterop.Release(checker);
                SpellCheckComInterop.Release(factory);
            }
        });
    }

    [RequiresSpellCheckFact]
    public void SpellChecker_PublicApi_ReportsMisspellings_WhenAvailable()
    {
        RunSta(() =>
        {
            // Exercises the full production stack the way TextBox consumes it: constructor activation,
            // Check, the nested GetSuggestions (IEnumString path), and SpellingError construction.
            // The [RequiresSpellCheckFact] gate already probed en-US as usable on this host, so
            // IsAvailable is asserted rather than silently returning PASS when it is false.
            using var checker = new SpellChecker("en-US");
            Assert.True(checker.IsAvailable,
                "SpellChecker must be available: the [RequiresSpellCheckFact] gate probed en-US as usable.");

            IReadOnlyList<SpellingError> errors = checker.Check("thiss is a testt sentence");
            Assert.NotNull(errors);
            Assert.All(errors, e =>
            {
                Assert.False(string.IsNullOrEmpty(e.Word));
                Assert.NotNull(e.Suggestions);
            });
            Assert.Contains(errors, e => e.Word == "thiss" || e.Word == "testt");
        });
    }

    #region Helpers

    private static void AssertQuerySucceeds(nint pUnk, Guid iid)
    {
        int hr = Marshal.QueryInterface(pUnk, in iid, out nint p);
        Assert.True(hr >= 0, $"QueryInterface({iid}) failed: 0x{hr:X8}");
        Assert.NotEqual(0, p);
        Marshal.Release(p);
    }

    /// <summary>
    /// Runs <paramref name="body"/> on a dedicated STA thread with COM initialized, so
    /// CoCreateInstance of the spell-check factory works and any assertion failure is re-thrown on the
    /// calling thread with its original stack.
    /// </summary>
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            int hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
            try
            {
                body();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
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

        captured?.Throw();
    }

    private const uint COINIT_APARTMENTTHREADED = 0x2;

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    #endregion
}
