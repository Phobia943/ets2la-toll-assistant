#pragma once
#include <cstdint>
#include <cstddef>
namespace maut_pedal_probe {
constexpr std::uint32_t Magic=0x4d505031,Version=1;
#pragma pack(push,8)
struct Snapshot {
 std::uint32_t sequence,magic,version,size,pid,initialized;
 std::int64_t sessionQpc,qpcFrequency;
 std::uint64_t samples;
 std::int64_t utcMillis,beginQpc,endQpc;
 double originalAccelerationTimestamp;
 float requested,beforeThrottle,beforeBrake,afterThrottle,afterBrake;
 std::uint32_t flags;
 std::uint64_t reserved[3];
};
#pragma pack(pop)
static_assert(sizeof(Snapshot)==128);
static_assert(offsetof(Snapshot,originalAccelerationTimestamp)==72);
static_assert(offsetof(Snapshot,requested)==80);
static_assert(offsetof(Snapshot,flags)==100);
constexpr std::uint32_t FiniteValues=1;
// Flags/observations acknowledge only the existing input_mix writes. They do
// not prove a logical game input, brake force, or physics-frame consumption.
}
