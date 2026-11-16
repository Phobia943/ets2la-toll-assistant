#pragma once
#include <cstdint>
#include <cstddef>

namespace maut_scene {
constexpr std::uint32_t Magic = 0x3143534d;
constexpr std::uint32_t Version = 1;
constexpr unsigned Capacity = 512;
constexpr std::uint8_t BuildId[32] = {
 0xc6,0x5e,0xfa,0x73,0x08,0x37,0xa8,0xc0,0x5f,0xe3,0x82,0xc3,0x30,0xc2,0xf4,0xdf,
 0xfe,0xee,0xc6,0x9f,0xbd,0xa8,0x1b,0xb0,0xf8,0x90,0x72,0xc2,0xc4,0xab,0xa6,0xd5};
enum Flags : std::uint32_t { Complete=1, Started=2, Paused=4, PoseValid=8, ClockValid=16, ActorsOnlyParked=32 };
enum BodyFlags : std::uint32_t { Trailer=1, MotionKnown=2 };
enum ScopeFlags : std::uint32_t { Loaded=1, ActorsEnumerated=2 };
enum Diagnostic : std::uint32_t { Good=0, MissingWorld=1, InvalidRegistry=2, InvalidActor=3, BrokenChain=4, ClockUnavailable=5, CallbackUnavailable=6, MemoryUnreadable=7 };
#pragma pack(push, 1)
struct Header {
 std::uint32_t magic, version, size, sequence;
 std::int64_t utcMillis;
 std::uint32_t pid, flags;
 std::uint64_t session, simulationTime, pausedSimulationTime;
 double ownPosition[3];
 std::uint32_t bodyCount, bodyTotal, scopeCount, scopeTotal;
 double omittedMinSurfaceDistance;
 std::uint32_t diagnostic, reserved;
 std::uint8_t buildId[32];
 std::uint64_t generation, renderTime;
};
struct Body {
 std::uint64_t id;
 std::uint32_t flags;
 float speed, center[3], rotationXYZW[4], halfExtents[3];
 std::uint64_t group;
};
struct Scope { std::uint64_t uid; std::uint32_t flags, reserved; };
struct Snapshot { Header header; Body bodies[Capacity]; Scope scopes[Capacity]; };
#pragma pack(pop)
static_assert(sizeof(Header)==160 && sizeof(Body)==64 && sizeof(Scope)==16 && sizeof(Snapshot)==41120);
static_assert(offsetof(Header,sequence)==12 && offsetof(Header,utcMillis)==16 && offsetof(Header,buildId)==112 && offsetof(Header,generation)==144);
}
