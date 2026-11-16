// Synthetic producer contract tests. Includes the producer implementation only
// in this standalone executable, with null world getters. Never initializes an
// SDK, opens a game, creates a mapping, or issues controls.
#include "SceneSnapshot.cpp"
#include <cstdio>
#include <stdexcept>

namespace ets2la_plugin::prism {
void traffic_object_t::destructor(){}
ETrafficObjectType traffic_object_t::get_type()const{return traffic_parked_vehicle;}
void traffic_obstacle_t::destructor(){}
void traffic_model_object_t::destructor(){}
void traffic_physics_object_t::destructor(){}
void traffic_physics_t::destructor(){}
void kdop_item_t::destructor(){}
void driveable_item_t::destructor(){}
void segment_t::destructor(){}
template<class T>void array_t<T>::destructor(){}
base_ctrl_u* base_ctrl_u::get(){return nullptr;}
array_dyn_t<kdop_item_t*>* base_ctrl_u::get_nearby_kdop_items()const{return nullptr;}
game_traffic_u* game_traffic_u::get(){return nullptr;}
}
namespace {
unsigned count=0,registrations=0;
scs_result_t result=SCS_RESULT_ok;
scs_event_t registeredEvent=0;
void Check(bool okay,const char* message){++count;if(!okay)throw std::runtime_error(message);}
SCSAPI_RESULT Register(const scs_event_t event,scs_telemetry_event_callback_t,scs_context_t){++registrations;registeredEvent=event;return result;}
}
int main(){try{
 using namespace maut_scene;
 scs_telemetry_init_params_v101_t params{};params.register_for_event=Register;
 DriveCallbackRegistration(false);Check(!RegisterClock(&params)&&registrations==0,"Missing either existing drive callback prevents clock registration");
 DriveCallbackRegistration(true);result=SCS_RESULT_generic_error;
 Check(!RegisterClock(&params)&&registrations==1,"Failed frame-start registration cannot enable observer");
 result=SCS_RESULT_ok;Check(RegisterClock(&params)&&registrations==2&&registeredEvent==SCS_TELEMETRY_EVENT_frame_start,"Observer registers only frame-start; no replacement pause/started callback");

 alignas(8) Snapshot wire{};shared=&wire;writerOwned=true;session=123;sequence=0;generation=10;
 Snapshot input{};input.header.flags=Complete|PoseValid;input.header.omittedMinSurfaceDistance=std::numeric_limits<double>::infinity();
 started=true;paused=false;clockSeen=true;clockFrame={};clockFrame.simulation_time=100;clockFrame.paused_simulation_time=90;clockFrame.render_time=110;
 Publish(input);
 Check(wire.header.sequence&&!(wire.header.sequence&1)&&wire.header.magic==Magic&&wire.header.size==41120,"Single producer commits an even, nonzero wire sequence with pinned shape");
 Check(wire.header.session==123&&wire.header.generation==10&&wire.header.simulationTime==100&&wire.header.pausedSimulationTime==90&&wire.header.renderTime==110,"Producer exports its current instance, generation and all three clocks");
 Check((wire.header.flags&(Complete|PoseValid|ClockValid|Started|ActorsOnlyParked))==(Complete|PoseValid|ClockValid|Started|ActorsOnlyParked),"Bounded parked-source semantics remain explicit");
 Check(std::memcmp(wire.header.buildId,BuildId,32)==0,"Producer build identity matches consumer pin");
 input.header.utcMillis=123456;Publish(input);Check(wire.header.utcMillis==123456,"Commit cannot refresh a scene's earlier capture-start timestamp");
 auto committed=wire.header.sequence;DriveEvent(SCS_TELEMETRY_EVENT_paused);
 Check(wire.header.sequence!=committed&&!(wire.header.flags&Complete)&&(wire.header.flags&Paused)&&!(wire.header.flags&Started)&&wire.header.generation==11,"Pause immediately withdraws prior completeness and advances generation");
 DriveEvent(SCS_TELEMETRY_EVENT_started);
 Check(!clockSeen&&!(wire.header.flags&Complete)&&wire.header.generation==12,"Resume requires a new frame clock before availability");
 scs_telemetry_frame_start_t frame{};frame.simulation_time=200;frame.paused_simulation_time=190;frame.render_time=210;
 poseSeenThisFrame=true;Event(SCS_TELEMETRY_EVENT_frame_start,&frame,nullptr);
 Check(clockSeen&&!poseSeenThisFrame,"New frame never inherits an earlier frame's world-position callback");
 WorldPlacementSeen();Check(poseSeenThisFrame,"Real world-position callback marks this frame only");
 frame.flags=SCS_TELEMETRY_FRAME_START_FLAG_timer_restart;frame.render_time=1;frame.simulation_time=1;frame.paused_simulation_time=0;
 Event(SCS_TELEMETRY_EVENT_frame_start,&frame,nullptr);
 Check(wire.header.generation==13&&!(wire.header.flags&Complete)&&!poseSeenThisFrame,"Timer restart invalidates earlier scene and body epoch");
 registered=true;lastWrite={};scs_value_dplacement_t own{};WorldPlacementSeen();Tick(own);
 Check(!(wire.header.flags&Complete)&&wire.header.diagnostic==MissingWorld,"Missing global world sources never become empty clear scene");

 prism::prefab_item_t prefab{};prefab.kdop_item_t::item_type=4;prefab.uid=777;
 Collection unloaded;unloaded.Prefab(&prefab);unloaded.Finish();
 Check(unloaded.scopes.at(777)==0&&unloaded.error==Good,"Missing segment blocks its UID without inventing a global source error");
 prism::segment_t segment{};prefab.segment=&segment;
 prism::semaphore_instance_t instance{};segment.semaphore_instances.value=&instance;segment.semaphore_instances.size=1;segment.semaphore_instances.capacity=0;
 Collection malformed;malformed.Prefab(&prefab);malformed.Finish();
 Check(malformed.scopes.at(777)==Loaded&&malformed.error==Good,"Malformed local registry remains scope-unknown without masking global Actor errors");
 segment.semaphore_instances.capacity=1;prism::traffic_parked_actor_t actor{};
 actor.placement.rot={1,0,0,0};actor.aabox.start={-1,0,-2};actor.aabox.end={1,4,2};instance.actor=&actor;
 Collection overflow;
 for(std::uintptr_t i=0;i<32768;++i)overflow.seen.insert(reinterpret_cast<const prism::traffic_parked_actor_t*>(0x100000+i*0x1000));
 overflow.Prefab(&prefab);overflow.Finish();
 Check(overflow.error==InvalidRegistry&&!(overflow.value.header.flags&Complete)&&overflow.scopes.at(777)==Loaded,"Actor-limit loss inside a valid local list cannot be reset back to Complete");
 committed=wire.header.sequence;writerOwned=false;Publish(input);
 Check(wire.header.sequence==committed,"A producer that lost its writer lease cannot commit a later callback result");
 shared=nullptr;writerOwned=false;registered=false;
 std::printf("PASS %u producer contract assertions; synthetic callbacks/lists, no SDK initialization or mappings.\n",count);return 0;
}catch(const std::exception& e){std::fprintf(stderr,"FAIL: %s\n",e.what());return 1;}}
