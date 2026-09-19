# Windows NativeAOT startup layout

`EmptyWindowLayout.props` selects the adjacent method-order file for a `win-x64`
NativeAOT publication. The file contains common framework and runtime symbols;
it contains no MemoryProbe or StartupAllocationProbe application symbols and no
machine-specific paths. `tests/Jalium.UI.MemoryProbe` imports it in the LowMemory
publish profile. Other source-tree applications can import this props file from
their own publish profile after setting RuntimeIdentifier and PublishAot.

The order was derived with .NET 10.0.12 from the default Jalium window startup.
An instrumented executable was used solely to identify layout candidates. The
normal publish does not use reachability-based pruning, instrumentation, runtime
feature removal, or aggressive method-body folding. All unlisted methods stay in
the compiled program. Inlining and generic method sharing mean that a candidate
is not proof that each particular native method body executed.

The compiler matches complete native symbol names, including case. Unknown names
are ignored. Changing the framework or runtime version can therefore reduce the
effectiveness of the list, without removing functionality. Applications with
different startup paths should measure their own working set before adopting it.
The list is a measured layout hint, not a memory-budget guarantee.

An explicitly supplied `IlcOrderFile` takes precedence. Pass
`-p:JaliumUseEmptyWindowLayout=false` to compare the default compiler layout using
otherwise identical inputs. Keep the final `.ilc.rsp`, input and output hashes,
and memory samples for both builds. Check that the response file contains
`--order:` and `--method-layout:explicit` only in the layout-enabled build, and
does not contain `--reachabilityuse` or `--reachabilityinstrument` in either.

The source order has one raw symbol per line and intentionally has no header or
comments, because the compiler reads each line as a symbol name. Do not convert
the entries to C# or CLI method signatures.

## Optional Windows imports

The low-memory probe profiles also enable `JaliumLazyWindowsImports`. The
`LazyWindowsImports.targets` import refines only the installed SDK's automatic
`WindowsAPIs.txt` list. Certificate, networking, automation-string and version
APIs use NativeAOT's normal lazy P/Invoke resolution, while the rest of the SDK
list and any explicit application `DirectPInvoke` items or custom lists remain
unchanged. Function bodies and interop entry points are not removed.

The first actual use pays the DLL loading/resolution cost. An independent AOT
smoke application verified certificate signing and verification, a loopback
socket, IP Helper, BSTR allocation and version queries after those libraries
were initially absent. The libraries appeared on use and every operation
succeeded. No external network traffic was used for this test.

The native intermediate directory retains the original SDK list and both the
eager and deferred partitions. If the SDK's list cannot be recognized, the
original SDK policy is retained and the build emits a warning. Pass
`-p:JaliumLazyWindowsImports=false` to compare that original policy. This setting
does not apply to ordinary managed Debug or Release builds.

Neither setting is a statement that a particular application meets a 20 MB
budget. Use the measured maximum total working set, independently of private
commit and executable size.
