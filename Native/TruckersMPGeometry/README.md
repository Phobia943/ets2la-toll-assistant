# Optional TruckersMP vehicle geometry observer

Component version 0.3.15, locally installed alongside Mautassistent 0.3.16 on 15 November 2026. **Not yet validated against a running TruckersMP client.** This module only observes model bounds. It does not control pedals, keep the game focused, identify construction barriers, or authorize a closer stop at a closed gate.

## Purpose and public API

The official [TruckersMP vehicle API](https://github.com/TruckersMP/Docs/blob/master/client-sdk/modules/vehicles-and-trailers.md) exposes local truck/trailer placement and model-space bounding boxes in metres. These measurements can establish the actual reference point and dimensions needed for future toll clearance and payment-stop calculations. Model bounds are not proven to cover cargo, accessories, or the exact collision hull. The managed consumer always reports `ClearanceApproved = false` and `CargoCoverageKnown = false`.

The observer uses the C SDK at commit `bee3b9e2b314c2b47fee9f94635e9945a71924e1`. All getters run from initialization or the main-thread `RENDER_PRE_RENDER` callback, at most ten samples per second. There is no SDK access on a worker thread, no input API, game-memory offset, hook, disk writer, network request, or other-player enumeration. Headers remain external under ignored `References/`; the fetch script verifies both hashes.

## Build and offline checks

Requires Windows x64, PowerShell 7 and Zig 0.15.2. Download Zig from [the official release](https://ziglang.org/download/0.15.2/zig-x86_64-windows-0.15.2.zip); SHA256 `3A0ED1E8799A2F8CE2A6E6290A9FF22E6906F8227865911FB7DDEDC3CC14CB0C`.

From the repository root:

```powershell
./Native/TruckersMPGeometry/Fetch-Sdk.ps1
./Native/TruckersMPGeometry/Build.ps1 -Zig 'path/to/zig.exe' -Test
$env:MAUT_GEOMETRY_FIXTURE=(Resolve-Path './Native/TruckersMPGeometry/bin/native-fixture.bin').Path
dotnet run --project CoreTests -c Release
```

Output: `bin/MautGeometry.dll`, plus the fake-host executable and a binary frame fixture when `-Test` is used. The native fake host checks loading, ABI, truck/trailer reads, unknown/absent trailer distinctions, invalid data, duplicate initialization, despawn, shutdown, reload, and missing APIs. The actual native frame is decoded by the managed tests. No game process is started, attached to, or controlled. CI repeats this path on Windows.

## Protocol

One writer publishes the 256-byte layout in `Protocol.h` to `Local\ETS2LA-MautGeometry-v1`. The odd/even sequence is checked around the complete reader copy. A writer mutex excludes another producer; the frame carries process ID, unique session, UTC milliseconds, explicit truck/trailer validity, world placement, local bounds, and optional velocity. Shutdown publishes invalidity before unmapping. If a reader retains the mapping across game restarts, the next exclusive writer may reclaim only a known protocol frame that is invalidated or more than one second stale. Fresh foreign frames are not erased. Fake-host tests cover these cases.

The consumer opens existing memory read-only and never creates it. It rejects changed, stale (>300 ms), future (>100 ms), malformed or unverified game-process frames. A changed PID/session is revalidated. Reader work runs on the diagnostic worker, away from driving callbacks. Raw truck/trailer bounds and pose are included in `status.vehicleShape`; front reach is only a diagnostic value when SDK origin and orientation agree. Rejection or absence of this optional observer cannot stop the driving assistant.

## First real-client validation remains outstanding

The official getting-started guide requires TruckersMP client 0.7.4.0 or newer and loading game-side DLLs through the game plugins directory after the SDK disclaimer. Do not copy this DLL into ETS2LA's managed plugin directory. Future installation requires closed applications and a backup; no installation is performed by these scripts.

Before using any measurements for driving decisions, verify SDK initialization on the actual client, truck/trailer identity, coordinates and orientation against telemetry, known-absent versus unreadable trailers, model dimensions against the visible vehicle/cargo, freshness while Alt+Tabbing, unload/reload, and unchanged manual brake takeover. Even a successful observer test does not demonstrate working background pedals or general obstacle detection.
