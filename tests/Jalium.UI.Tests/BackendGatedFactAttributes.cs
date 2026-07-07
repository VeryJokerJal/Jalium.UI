using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// A <c>[Fact]</c> that runs only when the given native render backend is
/// available on this host. When it is not, the test is reported as SKIPPED
/// (with the reason) instead of silently returning PASS from inside the body.
/// </summary>
/// <remarks>
/// xUnit v2 (this project references xunit 2.9.3) has no <c>Assert.Skip</c> —
/// that is v3-only — and pulling in the Xunit.SkippableFact package would add
/// a dependency. Setting <see cref="FactAttribute.Skip"/> from the attribute
/// constructor is the zero-dependency v2 idiom: the discoverer reads the
/// property after instantiating the attribute, so the availability probe runs
/// at discovery time and unavailable-backend tests surface as first-class
/// skip results in the run summary.
/// </remarks>
internal sealed class RequiresBackendFactAttribute : FactAttribute
{
    public RequiresBackendFactAttribute(RenderBackend backend)
    {
        if (!BackendAvailabilityProbe.IsAvailable(backend, out var reason))
        {
            Skip = reason;
        }
    }
}

/// <summary>
/// Inverse gate of <see cref="RequiresBackendFactAttribute"/>: the test runs
/// only when the given backend is ABSENT, and is reported as SKIPPED when the
/// backend unexpectedly exists. Used by regression tests that need a
/// guaranteed-unavailable probe backend (e.g. Metal on a Windows host).
/// </summary>
internal sealed class RequiresBackendAbsentFactAttribute : FactAttribute
{
    public RequiresBackendAbsentFactAttribute(RenderBackend backend)
    {
        if (BackendAvailabilityProbe.IsAvailable(backend, out _))
        {
            Skip = $"{backend} backend is unexpectedly available on this host; " +
                   "this test requires it to be absent.";
        }
    }
}

/// <summary>
/// Shared availability probe for the backend-gated fact attributes. Any probe
/// failure (e.g. <see cref="DllNotFoundException"/> when the native core DLL
/// is missing from the test output) is treated as "unavailable" so discovery
/// never crashes — the affected tests skip with the exception named in the
/// reason instead.
/// </summary>
internal static class BackendAvailabilityProbe
{
    internal static bool IsAvailable(RenderBackend backend, out string unavailableReason)
    {
        try
        {
            if (NativeMethods.IsBackendAvailable(backend) != 0)
            {
                unavailableReason = string.Empty;
                return true;
            }

            unavailableReason = $"{backend} backend unavailable on this host " +
                                "(NativeMethods.IsBackendAvailable returned 0).";
            return false;
        }
        catch (Exception ex)
        {
            unavailableReason = $"{backend} backend availability probe failed: " +
                                $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }
}
