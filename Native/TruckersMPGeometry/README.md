# TruckersMP vehicle geometry source

The locally installed component is 0.3.18, SHA256 `A91E2C5354D03DA0E07E79E9AB732443EE277B995297293FB65D93D3A96E5262`. Its installed hash was verified with all SEVEN files in the coordinated update. No apps were started and no new live validation has occurred. It only observes model bounds; it does not control pedals, keep the game focused, identify construction barriers or authorize a closer stop at a closed gate.

Historical 0.3.16 fixed retention of the SDK entry host. The installed 0.3.15 initialized in a real game process but emitted no valid body bounds while the user confirmed a server connection. After installation and user restart on 15 November, coherent live truck and trailer observations were confirmed for that older correction; this is not validation of the new 0.3.18 runtime combination.

The host descriptor passed to `truckersmp_init` is now copied into module-owned storage. Keeping the caller's pointer across callbacks was unsafe; the official C++ session wrapper also retains the context and API function values instead of the entry descriptor's address. A regression clears the fake entry host after initialization: the old implementation loses vehicle data, the corrected implementation keeps it. The regression and subsequent live restart confirm this correction for the observed failure: fresh flags 7, diagnostic code 0, and known/present truck and trailer data. At standstill the model reference position matches SCS telemetry within one centimetre and heading dot is above 0.9999. Moving samples are asynchronous and must not be interpreted as a measured bumper gap or proof of cargo coverage.

## Purpose and public API

The official [TruckersMP vehicle API](https://github.com/TruckersMP/Docs/blob/master/client-sdk/modules/vehicles-and-trailers.md) exposes local truck/trailer placement and model-space bounding boxes in metres. The 0.3.18 candidate uses these observations as an input to a SEPARATE derived enclosure proof. The raw measurements alone do not approve toll clearance: model bounds are not proven to cover cargo, accessories, or the exact collision hull. The native/diagnostic consumer continues to report `ClearanceApproved = false` and `CargoCoverageKnown = false`. Those flags are never changed to imply cargo evidence.

The observer uses the C SDK at commit `bee3b9e2b314c2b47fee9f94635e9945a71924e1`. All getters run from initialization or the main-thread `RENDER_PRE_RENDER` callback. A validated high-resolution QPC clock enforces at least 16 ms between samples and at most one sample per callback, without a catch-up loop. UTC comes from `GetSystemTimePreciseAsFileTime` and records the capture START before getters; a slow getter cannot restamp old data as new. There is no SDK access on a worker thread, no input API, game-memory offset, hook, disk writer, network request, or other-player enumeration. Headers remain external under ignored `References/`; the fetch script verifies both hashes.

## Build and offline checks

Requires Windows x64, PowerShell 7 and Zig 0.15.2. Download Zig from [the official release](https://ziglang.org/download/0.15.2/zig-x86_64-windows-0.15.2.zip); SHA256 `3A0ED1E8799A2F8CE2A6E6290A9FF22E6906F8227865911FB7DDEDC3CC14CB0C`.

From the repository root:

```powershell
./Native/TruckersMPGeometry/Fetch-Sdk.ps1
./Native/TruckersMPGeometry/Build.ps1 -Zig 'path/to/zig.exe' -Test
$env:MAUT_GEOMETRY_FIXTURE=(Resolve-Path './Native/TruckersMPGeometry/bin/native-fixture.bin').Path
dotnet run --project CoreTests -c Release
```

Output: `bin/MautGeometry.dll`, plus `MautGeometry.Test.dll`, the fake-host executable and a binary frame fixture when `-Test` is used. `-OutputDirectory` can place all products/caches in a separate private workspace directory. Only the production DLL is an installable artifact. The test pair uses a unique test-only mapping and writer mutex for every build: it cannot open or replace the live game's production measurement channel. The 31 native checks include descriptor reuse, missing getters, loading/ABI, truck/trailer reads, unknown/absent distinctions, invalid data, duplicate initialization, despawn, shutdown, reload, API allowlist, 16-ms throttle, duplicate callbacks, jittered high-resolution 60-Hz timing and capture-start UTC under slow getters. Fake timing exists only under test defines through the existing callback; production retains exactly its two original exports. No game process is started, attached to or controlled.

## Protocol

One writer publishes the 256-byte layout in `Protocol.h` to `Local\ETS2LA-MautGeometry-v1`. The odd/even sequence is checked around the complete reader copy. A writer mutex excludes another producer; the frame carries process ID, unique session, UTC milliseconds, explicit truck/trailer validity, world placement, local bounds, and optional velocity. Shutdown publishes invalidity before unmapping. If a reader retains the mapping across game restarts, the next exclusive writer may reclaim only a known protocol frame that is invalidated or more than one second stale. Fresh foreign frames are not erased. Fake-host tests cover these cases.

Component 0.3.16 uses four previously reserved bytes at offset 180 for an optional diagnostic code: 0 success, 1 local player unavailable, 2 vehicle unavailable, 3 placement unavailable, 4 bounds unavailable, 5 invalid body data, 6 shutdown. Frame version, size, offsets and validity rules are unchanged; existing readers ignore the optional field. Invalid body values may remain in the diagnostic payload, always with truck validity cleared. They are never returned as valid geometry. No player identity is collected.

The consumer opens existing memory read-only and never creates it. It rejects changed, stale (>300 ms), future (>100 ms), malformed or unverified game-process frames. A changed PID/session is revalidated. Raw truck/trailer bounds and pose remain available in `status.vehicleShape`. The 0.3.18 enclosure path also reads this fixed-size source during its current proof checks; substantial asset inspection uses a separate bounded worker. Its absence or rejection can now deny automatic toll clearance and cause a normal precautionary stop. This source is therefore not an optional diagnostic dependency for that candidate. The observer itself still does not issue driving controls.

## Real-client validation and remaining checks

Use the matching TruckersMP client/SDK combination for the pinned headers and load game-side DLLs through the game plugins directory after the SDK disclaimer. This project does not establish compatibility with arbitrary later client builds. Do not copy this DLL into ETS2LA's managed plugin directory. Future installation requires closed applications and a backup; no installation is performed by these scripts.

Before using any measurements for driving decisions, verify SDK initialization on the actual client, truck/trailer identity, coordinates and orientation against telemetry, known-absent versus unreadable trailers, model dimensions against the visible vehicle/cargo, freshness while Alt+Tabbing, unload/reload, and unchanged manual brake takeover. Even a successful observer test does not demonstrate working background pedals or general obstacle detection.

## Derived enclosure in the 0.3.18 candidate

The seven-file combination is installed and hash-verified, but not road validated. Previous confirmation
of raw 0.3.16 measurements does not validate this new use of them.

The derived consumer separately requires coherent truck/trailer identity, each
body's own current pose, matching SCS origins and current directed navigation
height/lateral registration. Supported auxiliary cargo/accessory definitions are
resolved and placed through their actual model locators. No user dimensions and
no fixed 6-by-3-metre default grant clearance. A local model proof may be reused
only with its source/rig/cargo binding; its number is not a cached current-pose
permission.

For the articulated scene enclosure, the SCS telemetry must additionally provide
actual model-local truck and trailer coupling hooks whose transformed positions
agree. This native observer does not supply or invent those hooks. Missing hooks,
unknown main cargo models, special unsupported loads and multiple observed trailers
do not receive automatic enclosure permission. The runtime now predicts separate
truck/trailer boxes on the final directed trace under an explicit forward,
near-flat, fixed-axle no-slip model up to 20 km/h. It uses the reported primary
SCS chain_type=single configuration; native TrailerKnown only describes observed
presence and is not proof that unseen telemetry slots contain no extra units.
This finite monitored model is not an unrestricted physical swept-path guarantee.
Source phase translation is retained directionally, per body, without turning
ordinary longitudinal sampling lag into extra lateral width. Raw native capture
times and SCS observation times are not relabelled as current.

Real low-frame-rate timing, correlated source delivery and the combined runtime
still need validation in the actual client. No background pedal support or
general active-traffic/construction perception follows from these observations.

The parked-scene source is a separate component in the modified game SDK; see
[its contract](../ParkedScene/README.md). Both sources and the final managed
candidate must still be checked together in the actual client, first at standstill.
