# Performance measurements

[Documentation](README.md) · [Original October 4 record (中文)](round-2026-10-04.md)

This English overview explains the figures used in the launch film and links their historical measurement records. It does not represent a new benchmark run.

## v0.4.1 optimization comparison

The October 4 work first measured a real Avalonia desktop window and Windows ConPTY under nine combinations: 1/5/10 sessions, each idle, one outputting, or all outputting. It then changed screen writes to avoid per-character cluster string allocation and throttled preview redraws to 250 ms.

| Workload | Machine CPU before | After | Relative change |
| --- | --- | --- | --- |
| 1 session, one outputting | 14.28% | 11.39% | −20% |
| 5 sessions, all outputting | 24.18% | 17.72% | −27% |
| 10 sessions, all outputting | 17.48% | 5.06% | −71% |

For the 10-session all-output workload, reported allocations changed from 110.8 to 26.9 MiB (−76%). This is **allocation volume**, not a claim of a 76% reduction in total resident memory. Idle differences were within measurement noise, under 1.1 absolute CPU percentage points.

These are same-machine, before/after results for the recorded workload. They do not predict every CLI, desktop, session count, or machine. The original report names `desktop-opt4-2026-10-04.json`; raw artifact files from that environment are not all tracked in this repository. See [October 4 record (Chinese)](round-2026-10-04.md) and [v0.4.1 acceptance (Chinese)](acceptance-v0.4.1-2026-10-04.md) for recorded conditions and limits.

## Reproduce desktop measurements

Use the native measurement tool with a working .NET 8/Avalonia environment. Run from the repository root:

```powershell
dotnet run --project tools/TerminalHub.DesktopMeasurements -c Release -- artifacts/performance/desktop-current.json
dotnet run --project tools/TerminalHub.DesktopMeasurements -c Release -- artifacts/performance/workspace-switch-current.json --workspace-switch
```

Compare the same machine, session count, output load, and build settings. A successful build or a Headless regression is not a desktop performance measurement.

Earlier [desktop measurement](performance-2026-10-02.md) and [workspace-switch](performance-workspace-switch-2026-10-02.md) records remain in Chinese as historical evidence, with their own conditions and numbers.
