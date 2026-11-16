# Current parked scene observation (local candidate)

This supplements the original ETS2LA SDK's unchanged 40-entry parked map. The
ADDED OBSERVER is read-only and does not issue game controls. The SDK DLL still
contains its existing input/FFB functions; it is not a read-only DLL as a whole.
It uses pinned public ETS2LA plugin
source for **ETS2 1.62.x**, with the original license retained. It is not yet a
road-tested replacement for the installed SDK.

The 0.3.18 local candidate combines this source with managed Host revision
`0.3.18-host-3`. Neither this new source nor that combination has yet been
validated in the actual game. The prepared SDK SHA256 is
`BC87E1E35D87A777BB4D86DBA1F5D18F5FA0937F83FE63C22B71B0DB454FDBA9`.
The first controlled stand test must establish live source/session/clocks,
loaded scope coverage, plausible complete rig observations and manual takeover.

## Reproducible inputs and build

- ETS2LA/plugin commit `31f1cf1406d03f0d647a10c72887eaab5c2a748e`:
  https://github.com/ETS2LA/plugin/archive/31f1cf1406d03f0d647a10c72887eaab5c2a748e.zip
- Its fmt submodule commit `9ff9c695db8aeadf70eb26498a0e1cdceeeb849a`:
  https://github.com/fmtlib/fmt/archive/9ff9c695db8aeadf70eb26498a0e1cdceeeb849a.zip
- Zig 0.15.2 for Windows x86_64.

`Build-SceneSdk.ps1` checks both archive SHA256 hashes and compiler version,
extracts into a new directory, applies only exact pinned upstream rewrites, and
builds the SDK with the additional observer. No game files are included. The
script **does not install** or restart anything. MinGW compatibility and the real
Prism multiple-inheritance base offsets require the native tests; successful
compilation alone is not game validation.

`Test-SceneSdk.ps1` builds and runs the independent real-layout collector tests
and synthetic producer lifecycle/list tests. Supply the pinned extracted SDK,
the same Zig compiler, and a new output directory. These tests do not initialize
the SDK, create game mappings or issue controls.
The recorded local run passed 33 independent collector assertions and 18
producer-contract assertions. These are source/layout/lifecycle tests, not a
measurement of current in-game detection coverage.

## Contract

`Protocol.h` fixes every byte. Map name is
`Local\ETS2LA-MautScene-v1-<game PID>`, length 41120 bytes. The single producer
writes an odd sequence, copies the payload while skipping that sequence, then
commits a nonzero even sequence. A consumer must read sequence / whole payload /
sequence with barriers and reject changed, odd or zero values.
A separate per-PID semaphore holds the single-writer lease. A surviving reader
handle may keep the map object alive after shutdown; a later legitimate writer
can reuse it only after obtaining that lease and publishing a new session. Map
existence alone is neither ownership nor a valid producer lifetime.
Only writers open the semaphore, and every failed attempt closes its handle;
readers must never retain one. A semaphore is not an abandoned-owner mutex: crash
recovery relies on all its writer handles disappearing, so a competing attempt
at owner death can fail closed and need a fresh initialization attempt. Failed
callback registration publishes unavailable, closes its view/map and releases
the writer lease; later drive events cannot publish without lease ownership.

UTC is the producer capture time, not consumer read time. A session is per loaded
SDK instance. Generation changes on pause/start and timer restart or regression.
Render, simulation and paused-simulation clocks are exported separately. A
consumer must verify source build, PID, session, generation, age, clock progress
and game state; copying the same map again does not make it current.
The timestamp is taken before enumeration; a slow collector cannot refresh old
boxes by assigning their timestamp only when its full payload is committed.

The upstream FFB processor already owns paused/started callbacks. The pinned
patch keeps those registrations and their original FFB behavior, fans the same
events out to this observer, and checks their registration results. This
observer registers only frame-start; it never replaces another callback. Failed
event registration leaves the extra scene map explicitly unavailable.

`PoseValid` requires the real world-position callback in the capture frame.
`Complete` means the bounded **parked actor** registries examined by this source
were readable and enumerated, not that every object in the world is known. The
`ActorsOnlyParked` flag is mandatory. All three traffic-object lists are examined,
plus parked actors in loaded nearby prefab semaphore registries. Every parked
slave trailer is followed through its correctly adjusted actor base, with cycle
and depth checks. This does not replace active AI/TruckersMP traffic detection,
static collision clearance or an unknown cargo envelope.

Scopes identify observed prefab UIDs. `Loaded|ActorsEnumerated` is required for
the specific UID being checked. A missing UID or its missing segment is unknown;
an unrelated unloaded UID does not invalidate another complete scope. Object
boxes are current game model-local actor bounds transformed by each body's own
placement, with 5 cm padding. They are not an assertion about undisclosed cargo,
physics shapes, future spawns or a future time.

`ActorsEnumerated` says the present verified lists were fully read; it is **not**
an engine `SpawnFinished` marker. A stable sequence of scopes/bodies can debounce
loading but cannot prove that no further parked hookup will materialize. The
limited active-world consumer must recheck continuously and withdraw clearance
when a new local body appears. It must not persist an earlier absence as a
prefab-wide or future permission. Actual loading/approach recordings are still
needed before a road-tested release claim.

`MotionKnown` is set only when that exact adjusted actor's own double-pointer
physics link is readable and its speed, acceleration, bounds and center pass
finite/range checks. Speed is absolute m/s from `traffic_chassis_physics_t`, not
an assumption that a parked class is stationary. Missing physics leaves speed
unknown. Coordinates outside +/-262144 m are unsupported, limiting float-world
placement rounding within the model-box padding.

The body cap is 512, independent of the original 40. The nearest conservative
body-surface lower bounds determine retained entries. Omitted distance uses the
actual oriented body's enclosing sphere, never root distance or last-entry
center distance. Infinity is emitted only with no omissions. Consumers must add
their own full articulated rig extent, current short stopping horizon, latency
and motion reserve before using this bound. There is no cached permission to
drive through a future scene.

Unknown global lists, malformed bounds/quaternions, unreadable non-null actors,
broken trailer chains and unregistered timing callbacks cannot produce an empty
clear scene. The scope of this observer is currently active loaded parked actor
boxes; it is not a full-scene perception system.

## Managed use and remaining limits

The current consumer rechecks source epochs, real current pose/clock agreement,
the specific directed route item, loaded scope and body geometry. Unknown motion
is not interpreted as a stationary car. It can still cause a conservative normal
stop; physically present but not yet materialized/spawned objects remain outside
this source's claim. There is no general active-traffic or construction detector.

Own-rig clearance additionally needs the separate MautGeometry source, resolved
supported auxiliary cargo, current navigation registration and actual coupling
hooks. No fixed 6-by-3 profile or assumed user dimensions replace missing proof.
The native CargoCoverageKnown flag stays false. Multiple trailers, unknown main
cargo models and a complete future swept path are not automatically approved.

The historical v22 admission used an all-articulation enclosure about 15 metres
in radius and rejected unknown motion globally. The current managed runtime
instead calls separate measured truck/trailer predictions on the final directed
trace, with local static/mover checks, local actor-motion bounds and a model
monitor. Those changes do not enlarge the native observer's source coverage or
turn unknown motion into measured physics. The new acute AEB uses current
unpadded measured truck boxes; a planning-reserve tube is not its collision box.
The observer's 51 standalone checks do not validate the entire managed policy.
Those same 33 collector and 18 producer checks also pass through the clean Windows
CI entry using a freshly extracted, hash-verified unpatched SDK archive.

The coordinated update now has seven files, including MautGeometry 0.3.18 with
high-resolution 16-ms sampling and capture-start UTC. The frozen managed build
35542A74...EB087CE5 has new full offline regression and bounded real-FR-model
performance evidence; this is not a full capture/output or driving validation.
The seven-file transaction is complete and installed hashes are verified. No app
was started and new stand/driving validation remains pending.
The model supports the explicitly reported primary single-trailer configuration,
not a proof that five unobserved telemetry slots are empty. Forward near-flat
fixed-axle motion up to 20 km/h, bounded model error and contact monitoring are
required. A 600-ms contact grace does not silently authorize changed axle geometry.

The French recorded late position had only about 14.44 metres of approach road
left. Earlier positions and absence-of-SDK-gate scenarios are separate offline
questions; static inspection does not prove a real traversal. The whole updated
rig/scene/toll combination still needs an actual stand test and controlled drive.
Focus-dependent pedals in TruckersMP remain unresolved.

Generated SDK/fmt source trees, manufacturer DLLs, extracted SCS assets and
private recordings belong outside this repository. Only the authored patch,
observer, protocol, build/test scripts and required upstream license are kept
here. See [the candidate review](../../REVIEW-0.3.18.md) for pinned managed hashes
and the distinction between offline evidence and missing driving validation.
