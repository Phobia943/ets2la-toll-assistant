# Read-only static road worker

The optional static-road check reads supported map `Model` instances, their explicitly active `coll` parts, and the selected standard-road corridor. It reports positive overlaps only. Missing files, unsupported variants/layers, source errors, exhausted budgets, and late replies are **unknown**, never a claim that the road is clear and never an independent emergency-brake cause.

The worker references the pinned TruckLib libraries and pure core geometry. It does not reference ETS2LA.Game, controls, pedal bridges, or the plugin assembly. Archive decompression, model parsing, BVH construction and the shared 60,000-check query budget run in this separate process. The plugin only queues a route reference, checks equivalent route identity, and reads bounded results.

## Build and package

On Windows with the reviewed ETS2LA dependencies:

```powershell
./Tools/StaticRoadWorker/Build.ps1 -EtsRoot 'D:\ETS2LA\current'
dotnet build ./Plugin/MautAssistent.csproj -c Release -p:EtsRoot='D:\ETS2LA\current'
```

Build the worker **before** the final plugin build whenever its linked core/parser files change. The script creates `Plugin/StaticRoadWorkerManifest.json`, which the plugin embeds as its expected file identities. It rejects unexpected runtime files in its build directory rather than authorizing stale outputs. Rebuild from a clean output if the dependency list intentionally changes, and review the new identities.

Package the 13 manifest-listed files from `Tools/StaticRoadWorker/bin/Release/net10.0/` in a directory named `StaticRoadWorker` immediately beside the installed plugin DLL. PDB files are optional. Do not copy another installation's manifest or use a helper from PATH. The client checks every pinned file and rejects additional DLL, EXE or JSON runtime files, including `runtimeconfig.dev.json`. Missing/changed helpers disable this optional evidence source; they never fall back to parsing meshes in the driving process.

The supported installer layout is `<application base>/Plugins/MautAssistent.dll` with `<application base>/Plugins/StaticRoadWorker/`. The client resolves from `AppContext.BaseDirectory`, not `Assembly.Location`: the reviewed host copies only the plugin DLL to a temporary shadow directory before loading it. The default helper path also does not depend on the launch working directory. This is the packaged installation contract, not a claim that every host discovery path is application-base-relative.

## Process and evidence contract

- One owned process and one outstanding UTF-8 JSONL request. Requests are at most 128 KiB and replies at most 8 KiB; oversized, pipelined or truncated frames are rejected.
- Protocol version, random process-session ID, request ID, source generation, route ID, manual-override epoch and original host timestamp must match exactly.
- The response lease expires 200 ms after the original host snapshot. Sending, receiving, repeated reads and equivalent route refreshes cannot renew it. A separate 10-second I/O timeout detects a hung child without restarting on every ordinary cold load.
- Results contain only model UID, the actually selected map and absolute `ConflictAlong` on the original route. Distances are recomputed from the truck's current projection on that original route. An equivalent newly constructed route or removal of already passed points is allowed only through `MatchesAhead`; changed lane, topology or geometry revokes outstanding work permanently.
- Source/map changes, F9/manual-override epoch changes and reset revoke old evidence. Reset cancels pending pipe operations without waiting in the driving thread; cleanup kills only the owned process handle. The IPC thread exits on reset or after 60 seconds idle and can be started again. Final disposal prevents further jobs.
- The helper redirects library console output to stderr. The client continuously drains stderr without accumulating an unbounded log. Child crashes or I/O errors revoke the result and use bounded retry delays.

This remains a nominal standard-road check. It does not prove full trailer/cargo clearance, observe dynamically spawned construction objects, or prove the entire scene free. Conditional map layers, unknown flags and unsupported collision variants are excluded. Railway warning lights before arm movement require a separate observed signal source.

## Evidence

Regression groups cover bounded frames, exact stamps, wrong map, invalid/duplicate hits, expired results, equivalent route refresh, pruning, actual route replacement, manual override, source replacement, reset, and thread-start failure. The offline worker probe additionally checks the actual executable bundle, missing/tampered/runtime-injection files, historical road intersections, cold loading, live source recovery and owned-child restart. These offline checks do not replace a supervised drive test.
