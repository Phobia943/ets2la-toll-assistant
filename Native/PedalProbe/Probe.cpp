#include "Probe.hpp"
#include "Protocol.h"
#include <windows.h>
#include <cstring>
#include <cmath>
#define MAUT_STRING_INNER(x) L##x
#define MAUT_WIDE(x) MAUT_STRING_INNER(x)
#define MAUT_TEXT_INNER(x) #x
#define MAUT_TEXT(x) MAUT_TEXT_INNER(x)
namespace maut_pedal_probe {
namespace {
#ifdef MAUT_PEDAL_PROBE_TEST_ID
constexpr const wchar_t* Map=L"Local\\MautPedalProbe_TEST_" MAUT_WIDE(MAUT_TEXT(MAUT_PEDAL_PROBE_TEST_ID));
constexpr const wchar_t* Lease=L"Local\\MautPedalProbeLease_TEST_" MAUT_WIDE(MAUT_TEXT(MAUT_PEDAL_PROBE_TEST_ID));
#else
constexpr const wchar_t* Map=L"Local\\MautPedalProbe_v1";
constexpr const wchar_t* Lease=L"Local\\MautPedalProbeLease_v1";
#endif
SRWLOCK gate=SRWLOCK_INIT;
HANDLE mapping=nullptr,lease=nullptr;
Snapshot* shared=nullptr;
Snapshot value{};
bool owned=false;
std::int64_t Qpc(){LARGE_INTEGER n{};QueryPerformanceCounter(&n);return n.QuadPart;}
std::int64_t Utc(){FILETIME time{};GetSystemTimePreciseAsFileTime(&time);ULARGE_INTEGER n{};n.LowPart=time.dwLowDateTime;n.HighPart=time.dwHighDateTime;return static_cast<std::int64_t>(n.QuadPart/10000-11644473600000ULL);}
void Publish(){
 if(!shared||!owned)return;
 value.sequence+=2;if(value.sequence==0)value.sequence=2;
 InterlockedExchange(reinterpret_cast<volatile LONG*>(&shared->sequence),value.sequence-1);
 MemoryBarrier();
 std::memcpy(reinterpret_cast<char*>(shared)+4,reinterpret_cast<const char*>(&value)+4,sizeof(value)-4);
 MemoryBarrier();
 InterlockedExchange(reinterpret_cast<volatile LONG*>(&shared->sequence),value.sequence);
}
void Close(){
 if(shared){UnmapViewOfFile(shared);shared=nullptr;}
 if(mapping){CloseHandle(mapping);mapping=nullptr;}
 if(lease){if(owned)ReleaseSemaphore(lease,1,nullptr);CloseHandle(lease);lease=nullptr;}
 owned=false;
}
}
bool Initialize(){
 AcquireSRWLockExclusive(&gate);
 if(shared){ReleaseSRWLockExclusive(&gate);return true;}
 LARGE_INTEGER frequency{};
 if(!QueryPerformanceFrequency(&frequency)||frequency.QuadPart<=0){ReleaseSRWLockExclusive(&gate);return false;}
 lease=CreateSemaphoreW(nullptr,1,1,Lease);
 if(!lease||WaitForSingleObject(lease,0)!=WAIT_OBJECT_0){Close();ReleaseSRWLockExclusive(&gate);return false;}
 owned=true;
 mapping=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Snapshot),Map);
 const bool existing=mapping&&GetLastError()==ERROR_ALREADY_EXISTS;
 if(mapping)shared=static_cast<Snapshot*>(MapViewOfFile(mapping,FILE_MAP_WRITE,0,0,sizeof(Snapshot)));
 if(!shared){Close();ReleaseSRWLockExclusive(&gate);return false;}
 if(existing&&(shared->magic!=Magic||shared->version!=Version||shared->size!=sizeof(Snapshot))){Close();ReleaseSRWLockExclusive(&gate);return false;}
 // A reader can keep the old mapping alive across SDK reloads. Continue its
 // publication counter so a copy cannot mistake two different sessions for
 // the same sequence. Keep sequence odd throughout the new payload copy.
 const auto previousSequence=existing?shared->sequence&~std::uint32_t(1):0;
 value={};value.sequence=previousSequence;value.magic=Magic;value.version=Version;value.size=sizeof(Snapshot);value.pid=GetCurrentProcessId();
 value.initialized=1;value.sessionQpc=Qpc();value.qpcFrequency=frequency.QuadPart;value.utcMillis=Utc();Publish();
 ReleaseSRWLockExclusive(&gate);return true;
}
void Shutdown(){
 AcquireSRWLockExclusive(&gate);
 value.initialized=0;value.utcMillis=Utc();Publish();Close();
 ReleaseSRWLockExclusive(&gate);
}
Observation Begin(float requested,double timestamp,float throttle,float brake){
 return {Qpc(),Utc(),timestamp,requested,throttle,brake};
}
void Finish(const Observation& before,float throttle,float brake){
 const auto end=Qpc();
 AcquireSRWLockExclusive(&gate);
 if(shared&&value.initialized&&before.beginQpc>=value.sessionQpc&&before.beginQpc<=end){
  ++value.samples;value.utcMillis=before.utcMillis;value.beginQpc=before.beginQpc;value.endQpc=end;
  value.originalAccelerationTimestamp=before.originalTimestamp;
  value.requested=before.requested;value.beforeThrottle=before.beforeThrottle;value.beforeBrake=before.beforeBrake;
  value.afterThrottle=throttle;value.afterBrake=brake;
  value.flags=std::isfinite(before.originalTimestamp)&&std::isfinite(before.requested)&&
   std::isfinite(before.beforeThrottle)&&std::isfinite(before.beforeBrake)&&std::isfinite(throttle)&&std::isfinite(brake)?FiniteValues:0;
  Publish();
 }
 ReleaseSRWLockExclusive(&gate);
}
}
