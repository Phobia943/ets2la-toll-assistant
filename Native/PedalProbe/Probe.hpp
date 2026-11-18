#pragma once
#include <cstdint>
namespace maut_pedal_probe {
struct Observation {
 std::int64_t beginQpc,utcMillis;
 double originalTimestamp;
 float requested,beforeThrottle,beforeBrake;
};
bool Initialize();
void Shutdown();
Observation Begin(float requested,double originalTimestamp,float throttle,float brake);
void Finish(const Observation& before,float throttle,float brake);
}
