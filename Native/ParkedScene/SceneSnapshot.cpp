#include "SceneSnapshot.hpp"
#include "SceneCollector.hpp"
#include "prism/controllers/base_ctrl.hpp"
#include "prism/traffic/game_traffic.hpp"
#include "prism/management/item/prefab_item.hpp"
#include "prism/management/item/segment.hpp"
#include "prism/management/item/semaphore_instance.hpp"
#include <windows.h>
#include <chrono>
#include <cstring>
#include <string>
#include <unordered_map>

namespace maut_scene {
namespace {
HANDLE mapping=nullptr;
HANDLE writerLease=nullptr;
bool writerOwned=false;
Snapshot* shared=nullptr;
std::uint32_t sequence=0;
std::uint64_t session=0,generation=1;
bool registered=false,started=false,paused=true,clockSeen=false,poseSeenThisFrame=false;
bool driveCallbacksAvailable=false;
scs_telemetry_frame_start_t clockFrame{};
std::chrono::steady_clock::time_point lastWrite{};

std::int64_t Utc() {return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::system_clock::now().time_since_epoch()).count();}
void ReleaseWriterLease() {
 if(writerLease){if(writerOwned)ReleaseSemaphore(writerLease,1,nullptr);CloseHandle(writerLease);writerLease=nullptr;}
 writerOwned=false;
}
bool Readable(const void* ptr,std::size_t bytes) {
 if(!ptr || !bytes) return false;
 auto pos=reinterpret_cast<std::uintptr_t>(ptr);
 if(pos+bytes<pos) return false;
 const auto end=pos+bytes;
 while(pos<end) {
  MEMORY_BASIC_INFORMATION memory{};
  if(!VirtualQuery(reinterpret_cast<const void*>(pos),&memory,sizeof(memory)) || memory.State!=MEM_COMMIT || (memory.Protect&(PAGE_GUARD|PAGE_NOACCESS))) return false;
  if(!(memory.Protect&(PAGE_READONLY|PAGE_READWRITE|PAGE_WRITECOPY|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY))) return false;
  auto next=reinterpret_cast<std::uintptr_t>(memory.BaseAddress)+memory.RegionSize;
  if(next<=pos) return false;
  pos=next;
 }
 return true;
}
bool Executable(const void* ptr) {
 MEMORY_BASIC_INFORMATION memory{};
 return ptr&&VirtualQuery(ptr,&memory,sizeof(memory))&&memory.State==MEM_COMMIT&&
  !(memory.Protect&(PAGE_GUARD|PAGE_NOACCESS))&&
  (memory.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY));
}

void Publish(Snapshot& value) {
 if(!shared||!writerOwned) return;
 value.header.magic=Magic;value.header.version=Version;value.header.size=sizeof(Snapshot);
 // A scene uses capture-start time, not commit time. A slow enumeration cannot
 // make old body observations fresh by stamping them after its work finishes.
 if(!value.header.utcMillis)value.header.utcMillis=Utc();
 value.header.pid=GetCurrentProcessId();
 value.header.session=session;value.header.generation=generation;
 value.header.simulationTime=clockFrame.simulation_time;
 value.header.pausedSimulationTime=clockFrame.paused_simulation_time;
 value.header.renderTime=clockFrame.render_time;
 std::memcpy(value.header.buildId,BuildId,32);
 value.header.flags|=ActorsOnlyParked;
 if(started)value.header.flags|=Started;
 if(paused)value.header.flags|=Paused;
 if(clockSeen)value.header.flags|=ClockValid;
 // Never copy the sequence field: it stays odd for the entire payload copy.
 sequence+=2;if(sequence==0)sequence=2;
 auto sequenceAddress=reinterpret_cast<volatile LONG*>(reinterpret_cast<std::uintptr_t>(shared)+offsetof(Header,sequence));
 InterlockedExchange(sequenceAddress,sequence-1);
 MemoryBarrier();
 std::memcpy(shared,&value,offsetof(Header,sequence));
 auto begin=offsetof(Header,sequence)+sizeof(std::uint32_t);
 std::memcpy(reinterpret_cast<char*>(shared)+begin,reinterpret_cast<const char*>(&value)+begin,sizeof(Snapshot)-begin);
 MemoryBarrier();
 InterlockedExchange(sequenceAddress,sequence);
 lastWrite=std::chrono::steady_clock::now();
}
void Invalidate(std::uint32_t reason) {
 Snapshot value{};value.header.diagnostic=reason;
 value.header.omittedMinSurfaceDistance=0;
 Publish(value);
}
bool RegisterClock(const scs_telemetry_init_params_v101_t* params);
SCSAPI_VOID Event(const scs_event_t event,const void* const info,scs_context_t) {
 if(event==SCS_TELEMETRY_EVENT_paused) {
  paused=true;started=false;++generation;Invalidate(ClockUnavailable);
 } else if(event==SCS_TELEMETRY_EVENT_started) {
  paused=false;started=true;++generation;clockSeen=false;Invalidate(ClockUnavailable);
 } else if(event==SCS_TELEMETRY_EVENT_frame_start) {
  if(!info){clockSeen=false;Invalidate(ClockUnavailable);return;}
  const auto next=*static_cast<const scs_telemetry_frame_start_t*>(info);
  const bool reset=(next.flags&SCS_TELEMETRY_FRAME_START_FLAG_timer_restart)||
   (clockSeen&&(next.render_time<clockFrame.render_time||next.simulation_time<clockFrame.simulation_time||next.paused_simulation_time<clockFrame.paused_simulation_time));
  clockFrame=next;clockSeen=true;
  poseSeenThisFrame=false;
  if(reset){++generation;Invalidate(ClockUnavailable);}
 }
}

struct Collection {
 Snapshot value{};
 std::vector<Body> bodies;
 std::unordered_set<const prism::traffic_parked_actor_t*> seen;
 std::unordered_map<std::uint64_t,std::uint32_t> scopes;
 std::uint32_t error=Good;
 void Fail(std::uint32_t reason){if(!error)error=reason;}

 template<class T> bool ValidArray(const prism::array_dyn_t<T>& list) {
  return list.size<=32768 && list.capacity>=list.size && list.capacity<=1048576 &&
   (!list.size||Readable(list.value,list.size*sizeof(T)));
 }
 template<class T,class F> bool Array(const prism::array_dyn_t<T>& list,F consume) {
  if(!ValidArray(list)){Fail(InvalidRegistry);return false;}
  for(std::uint64_t i=0;i<list.size;++i)consume(list.value[i]);
  return true;
 }
 bool Actor(const prism::traffic_object_t* object) {
  if(!Readable(object,sizeof(*object))){Fail(MemoryUnreadable);return false;}
  auto vtable=*reinterpret_cast<void* const* const*>(object);
  if(!Readable(vtable,2*sizeof(void*))||!Executable(vtable[1])){Fail(MemoryUnreadable);return false;}
  const auto type=object->get_type();
  if(type!=prism::ETrafficObjectType::traffic_parked_vehicle && type!=prism::ETrafficObjectType::traffic_parked_trailer) return true;
  const auto* parked=static_cast<const prism::traffic_parked_actor_t*>(object);
  if(seen.count(parked))return true;
  const auto group=static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(parked));
  bool okay=WalkParkedChain(parked,Readable,[&](const prism::traffic_parked_actor_t* p,bool child) {
   if(!seen.insert(p).second)return true;
   if(seen.size()>32768){Fail(InvalidRegistry);return false;}
   Body body{};
   if(!MakeBody(p,child||type==prism::ETrafficObjectType::traffic_parked_trailer,
       static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(p)),group,body)){Fail(InvalidActor);return false;}
   ObserveMotion(p,Readable,body);
   bodies.push_back(body);return true;
  });
  if(!okay)Fail(BrokenChain);
  return okay;
 }
 void Prefab(const prism::kdop_item_t* item) {
  if(!Readable(item,sizeof(*item))){Fail(MemoryUnreadable);return;}
  if(item->item_type!=4)return;
  auto* prefab=static_cast<const prism::prefab_item_t*>(item);
  if(!Readable(prefab,sizeof(*prefab))||!prefab->uid){Fail(InvalidRegistry);return;}
  std::uint32_t flags=0;
  if(prefab->segment) {
   flags=Loaded;
   if(Readable(prefab->segment,sizeof(*prefab->segment))) {
    bool okay=true;
    const auto& list=prefab->segment->semaphore_instances;
    // Null actor is legitimate for an inactive semaphore instance; the registry
    // still was enumerated. Unknown non-null actor is never silently discarded.
    // Validate the local list header before touching global Actor errors. A
    // malformed local list only makes this scope unknown; any Actor/chain loss
    // during a valid list remains a global failure. Never reset an Actor error.
    if(ValidArray(list) && Array(list,[&](const auto& instance){if(instance.actor&&!Actor(instance.actor))okay=false;}) && okay)flags|=ActorsEnumerated;
   }
  }
  auto found=scopes.find(prefab->uid);
  if(found==scopes.end())scopes.emplace(prefab->uid,flags);
  else found->second&=flags; // Conflicting duplicate observations cannot upgrade.
 }
 void Finish() {
  StoreBodies(value,bodies);
  value.header.scopeTotal=static_cast<std::uint32_t>(scopes.size());
  std::vector<std::pair<std::uint64_t,std::uint32_t>> ordered(scopes.begin(),scopes.end());
  std::sort(ordered.begin(),ordered.end());
  value.header.scopeCount=std::min<unsigned>(Capacity,ordered.size());
  for(unsigned i=0;i<value.header.scopeCount;++i)value.scopes[i]={ordered[i].first,ordered[i].second,0};
  value.header.diagnostic=error;
  if(!error)value.header.flags|=Complete;
 }
};
bool RegisterClock(const scs_telemetry_init_params_v101_t* params) {
 return driveCallbacksAvailable&&params&&params->register_for_event&&
  params->register_for_event(SCS_TELEMETRY_EVENT_frame_start,Event,nullptr)==SCS_RESULT_ok;
}
}

bool Initialize(const scs_telemetry_init_params_v101_t* params) {
 if(mapping)return registered;
 auto name=L"Local\\ETS2LA-MautScene-v1-"+std::to_wstring(GetCurrentProcessId());
 // A reader can keep an old map alive after SDK shutdown/PID reuse. Exclusivity
 // belongs to this separate writer lease, not map-object existence. Semaphores
 // are not recursively owned by a second SDK instance on the same game thread.
 writerLease=CreateSemaphoreW(nullptr,1,1,(name+L"-writer").c_str());
 if(!writerLease)return false;
 if(WaitForSingleObject(writerLease,0)!=WAIT_OBJECT_0){CloseHandle(writerLease);writerLease=nullptr;return false;}
 writerOwned=true;
 mapping=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Snapshot),name.c_str());
 if(!mapping){ReleaseWriterLease();return false;}
 shared=static_cast<Snapshot*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Snapshot)));
 if(!shared){CloseHandle(mapping);mapping=nullptr;ReleaseWriterLease();return false;}
 LARGE_INTEGER counter{};QueryPerformanceCounter(&counter);
 session=static_cast<std::uint64_t>(counter.QuadPart)^(static_cast<std::uint64_t>(Utc())<<16)^GetCurrentProcessId();
 if(!session)session=1;
 // Pause/started are already owned by the upstream FFB processor. Its original
 // callback fans out to DriveEvent; registering a second callback is invalid.
 if(!RegisterClock(params)) {
  Invalidate(CallbackUnavailable);
  UnmapViewOfFile(shared);shared=nullptr;CloseHandle(mapping);mapping=nullptr;
  ReleaseWriterLease();return false;
 }
 registered=true;Invalidate(ClockUnavailable);return true;
}

void WorldPlacementSeen(){poseSeenThisFrame=true;}
void DriveCallbackRegistration(bool available){driveCallbacksAvailable=available;}
void DriveEvent(scs_event_t event){Event(event,nullptr,nullptr);}

void Tick(const scs_value_dplacement_t& own) {
 if(!registered||!shared)return;
 if(!started||paused||!clockSeen)return;
 if(std::chrono::steady_clock::now()-lastWrite<std::chrono::milliseconds(40))return;
 if(!poseSeenThisFrame){Invalidate(InvalidActor);return;}
 Collection c;
 c.value.header.utcMillis=Utc();
 c.value.header.ownPosition[0]=own.position.x;c.value.header.ownPosition[1]=own.position.y;c.value.header.ownPosition[2]=own.position.z;
 for(auto coordinate:c.value.header.ownPosition)if(!std::isfinite(coordinate)||std::abs(coordinate)>262144){Invalidate(InvalidActor);return;}
 c.value.header.flags=PoseValid;
 auto* base=prism::base_ctrl_u::get();auto* traffic=prism::game_traffic_u::get();
 if(!base||!traffic||!Readable(traffic,sizeof(*traffic))){Invalidate(MissingWorld);return;}
 auto* nearby=base->get_nearby_kdop_items();
 if(!Readable(nearby,sizeof(*nearby))){Invalidate(MissingWorld);return;}
 c.Array(*nearby,[&](const auto* p){c.Prefab(p);});
 // All three source registries are enumerated, not just the first 40 objects.
 c.Array(traffic->traffic_objects_1,[&](const auto* p){c.Actor(p);});
 c.Array(traffic->traffic_objects_2,[&](const auto* p){c.Actor(p);});
 c.Array(traffic->traffic_objects_3,[&](const auto* p){c.Actor(p);});
 c.Finish();Publish(c.value);
}

void Shutdown() {
 if(shared){++generation;started=false;paused=true;Invalidate(MissingWorld);UnmapViewOfFile(shared);shared=nullptr;}
 if(mapping){CloseHandle(mapping);mapping=nullptr;}
 ReleaseWriterLease();
 registered=false;driveCallbacksAvailable=false;
}
}
