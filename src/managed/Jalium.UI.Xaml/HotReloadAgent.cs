using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using Jalium.UI.Threading;

namespace Jalium.UI.Markup;

/// <summary>
/// Hot-reload in-process agent. The IDE injects the environment variable
/// <c>JALIUM_HOTRELOAD_PIPE=&lt;pipe name&gt;</c> when launching the user's app; the agent starts
/// lazily on the first JALXAML component registration (<see cref="HotReloadRuntime.RegisterComponent"/>
/// → <see cref="EnsureStarted"/>), so existing apps get hot reload with zero code changes.
/// Without the variable this type does nothing.
/// </summary>
/// <remarks>
/// Protocol (one frame per connection, then the server loops back to accept):
/// the client writes three <see cref="BinaryWriter"/> length-prefixed UTF-8 strings —
/// xClass, filePath, content — and reads back a single result string
/// <c>"updated|fallback|failed|message"</c>. <see cref="HotReloadRuntime.ApplyPatch"/> mutates the
/// visual tree and therefore always runs marshalled onto the UI thread (8s timeout guard).
/// </remarks>
public static class HotReloadAgent
{
    /// <summary>Forwarded from <see cref="HotReloadProtocol.PipeEnvironmentVariable"/> for existing callers.</summary>
    public const string PipeEnvironmentVariable = HotReloadProtocol.PipeEnvironmentVariable;

    /// <summary>Time budget for reading one complete request frame; guards the accept thread from a stalled client.</summary>
    private static readonly TimeSpan ReadFrameTimeout = TimeSpan.FromSeconds(10);

    private static int _started;

    /// <summary>
    /// Test hook: apply patches inline on the pipe thread instead of marshalling to the UI thread.
    /// Unit-test processes have a captured-but-never-pumping main dispatcher, so a BeginInvoke there
    /// would only ever time out; production never sets this.
    /// </summary>
    internal static bool ApplyInlineForTests;

    /// <summary>Starts the agent once if the pipe environment variable is present. Thread-safe.</summary>
    /// <remarks>
    /// The one-shot latch is only consumed when the variable IS present — a missing variable keeps
    /// the agent eligible for later registrations (matters for in-process tests that set the
    /// variable after other components have already registered; production sets it at launch).
    /// </remarks>
    public static void EnsureStarted()
    {
        if (Volatile.Read(ref _started) != 0)
        {
            return;
        }

        var pipeName = Environment.GetEnvironmentVariable(PipeEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            return;
        }

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        new Thread(() => ListenLoop(pipeName!))
        {
            IsBackground = true,
            Name = "Jalium-HotReload-Agent",
        }.Start();
    }

    /// <summary>
    /// Permanently prevents the in-process agent from starting in THIS process, even if the pipe
    /// environment variable becomes visible later. Called by a host (e.g. the Jalium IDE) that is
    /// itself a Jalium app but must never serve a hot-reload pipe — otherwise, when the host sets
    /// <see cref="PipeEnvironmentVariable"/> so its <em>child</em> app inherits it, the host's own
    /// agent would race to own the pipe and patch the host instead of the child. Idempotent.
    /// </summary>
    public static void SuppressInProcessAgent() => Interlocked.Exchange(ref _started, 1);

    private static void ListenLoop(string pipeName)
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.None);
                server.WaitForConnection();

                // Read the whole request frame under a time budget so a slow / truncated client cannot
                // pin this (single) accept thread forever. HotReloadProtocol already rejects oversized
                // or garbage frames via its length caps + magic/version check.
                var request = ReadRequestWithTimeout(server, ReadFrameTimeout);

                var result = ApplyOnUiThread(request.XClass, request.FilePath, request.Content);
                HotReloadProtocol.WriteResult(server, result);
                // Unix named pipes do not expose the Windows drain operation;
                // disposing the connected stream after the write is sufficient.
                if (OperatingSystem.IsWindows())
                {
                    try { server.WaitForPipeDrain(); } catch { }
                }
            }
            catch (Exception ex)
            {
                // Connection-level failure (IDE disconnected mid-frame, malformed payload, read timeout):
                // drop this connection and keep accepting — the agent must outlive any one client.
                // Trace it so a genuine protocol/transport defect is not indistinguishable from a
                // benign client disconnect when diagnosing in a debugger.
                System.Diagnostics.Debug.WriteLine($"[Jalium.HotReload] connection dropped: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Reads a complete request frame with an overall time budget. The frame read runs on a dedicated worker so a
    /// client that connects then stalls mid-frame can't block the accept loop indefinitely; on timeout we
    /// throw and the caller drops the connection (disposing the pipe unblocks the worker's pending read).
    /// </summary>
    private static (string XClass, string FilePath, string Content) ReadRequestWithTimeout(Stream stream, TimeSpan timeout)
    {
        // The listener already blocks synchronously below. Do not queue the actual pipe read to the
        // shared ThreadPool: a busy UI/test process can starve that work item past the timeout even
        // though the client has already sent a complete frame, causing us to close the Unix pipe and
        // surface ECONNRESET to the client. LongRunning gives this bounded read its own worker.
        var task = Task.Factory.StartNew(
            () => HotReloadProtocol.ReadRequest(stream),
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
        if (!task.Wait(timeout))
        {
            throw new TimeoutException("Timed out reading the hot-reload request frame.");
        }

        return task.Result;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Hot reload is a development-time feature activated only via environment variable; " +
                        "trimmed production builds never set JALIUM_HOTRELOAD_PIPE.")]
    private static HotReloadPatchResult ApplyOnUiThread(string xClass, string filePath, string content)
    {
        var dispatcher = Dispatcher.MainDispatcher;
        if (ApplyInlineForTests || dispatcher == null || dispatcher.CheckAccess())
        {
            try { return HotReloadRuntime.ApplyPatch(xClass, filePath, content); }
            catch (Exception ex) { return new HotReloadPatchResult(0, 0, 1, ex.Message); }
        }

        HotReloadPatchResult? result = null;
        // 0 = pending, 1 = applying (committed on the UI thread), 2 = abandoned (waiter timed out first).
        var state = 0;
        using var done = new ManualResetEventSlim(initialState: false);
        dispatcher.BeginInvoke(() =>
        {
            // Commit to applying only while still pending. If the waiter already abandoned (state == 2),
            // the IDE has moved on to a full restart — do NOT mutate a tree it decided to discard.
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0) { return; }
            try { result = HotReloadRuntime.ApplyPatch(xClass, filePath, content); }
            catch (Exception ex) { result = new HotReloadPatchResult(0, 0, 1, ex.Message); }
            finally { done.Set(); }
        });

        if (!done.Wait(TimeSpan.FromSeconds(8)))
        {
            // Abandon ONLY if the callback hasn't already committed to applying. Winning the CAS means it
            // never starts (safe to report a timeout). Losing it means a mutation is already in flight —
            // returning a "timed out + discard" now would let a late patch mutate a tree the client gave
            // up on (the original TOCTOU). Instead wait out a short grace for the real result.
            if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
            {
                return new HotReloadPatchResult(0, 0, 1, "Timed out applying patch on the UI thread.");
            }

            done.Wait(TimeSpan.FromSeconds(2));
        }

        return result ?? new HotReloadPatchResult(0, 0, 1, "Patch produced no result.");
    }
}
