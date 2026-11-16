#pragma once
#include <scssdk_telemetry.h>
namespace maut_scene {
 bool Initialize(const scs_telemetry_init_params_v101_t* params);
 void WorldPlacementSeen();
 void DriveCallbackRegistration(bool available);
 void DriveEvent(scs_event_t event);
 void Tick(const scs_value_dplacement_t& ownPlacement);
 void Shutdown();
}
