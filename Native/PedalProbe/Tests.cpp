#include "Probe.hpp"
#include "Protocol.h"
#include <windows.h>
#include <cstdio>
#include <cstdlib>
#include <limits>
#include <cstring>
#ifndef MAUT_PEDAL_PROBE_TEST_ID
#error Tests require an isolated mapping name.
#endif
#define STR_INNER(x) #x
#define STR(x) STR_INNER(x)
#define WIDE_INNER(x) L##x
#define WIDE(x) WIDE_INNER(x)
using namespace maut_pedal_probe;
int tests=0;
void Check(bool okay,const char* label){if(!okay){std::fprintf(stderr,"FAIL %s\n",label);std::exit(1);}++tests;}
Snapshot Read(Snapshot* p){
 auto deadline=GetTickCount64()+5000;
 while(GetTickCount64()<deadline){auto a=*reinterpret_cast<volatile LONG*>(&p->sequence);if(a&1){SwitchToThread();continue;}MemoryBarrier();Snapshot v{};std::memcpy(&v,p,sizeof v);MemoryBarrier();auto b=*reinterpret_cast<volatile LONG*>(&p->sequence);if(a==b&&!(b&1)&&v.sequence==static_cast<unsigned>(a))return v;}
 Check(false,"coherent read");return {};
}
DWORD WINAPI Writer(void* marker) {const float f=marker?.6f:.3f;for(int i=0;i<5000;++i){auto x=Begin(f,123.0+f,f/2,f/4);Finish(x,f,0);}return 0;}
int main(int argc,char**argv){
 Check(argc==2,"fixture path");
 auto old=Begin(1,111,0,0);
 Finish(old,1,0);Shutdown();
 Check(Initialize(),"initialize");
 HANDLE h=OpenFileMappingW(FILE_MAP_READ,FALSE,L"Local\\MautPedalProbe_TEST_" WIDE(STR(MAUT_PEDAL_PROBE_TEST_ID)));
 Check(h!=nullptr,"test namespace exists");
 auto p=static_cast<Snapshot*>(MapViewOfFile(h,FILE_MAP_READ,0,0,sizeof(Snapshot)));
 Check(p!=nullptr,"read-only view");
 auto first=Read(p);Check(first.magic==Magic&&first.version==1&&first.size==128,"protocol identity");
 Check(first.pid==GetCurrentProcessId()&&first.initialized&&first.qpcFrequency>0&&first.samples==0,"origin identity");
 Check(Initialize()&&Read(p).sessionQpc==first.sessionQpc,"duplicate initialization preserves session");
 Finish(old,1,0);Check(Read(p).samples==0,"pre-session observation ignored");
 auto sample=Begin(.7f,123.25,.1f,.3f);Finish(sample,.7f,0);
 auto one=Read(p);Check(one.samples==1&&one.flags==FiniteValues,"one finite observation");
 Check(one.originalAccelerationTimestamp==123.25&&one.requested==.7f,"original source time unchanged");
 Check(one.beforeThrottle==.1f&&one.beforeBrake==.3f&&one.afterThrottle==.7f&&one.afterBrake==0,"before/after retained");
 Check(one.beginQpc==sample.beginQpc&&one.endQpc>=one.beginQpc&&one.utcMillis==sample.utcMillis,"capture origin is before output");
 auto nan=Begin(std::numeric_limits<float>::quiet_NaN(),123.25,0,0);Finish(nan,0,0);
 Check(Read(p).flags==0,"nonfinite observation is not valid");
 HANDLE a=CreateThread(nullptr,0,Writer,reinterpret_cast<void*>(1),0,nullptr),b=CreateThread(nullptr,0,Writer,nullptr,0,nullptr);
 Check(a&&b,"two writers started");
 bool coherent=true;while(WaitForSingleObject(a,0)==WAIT_TIMEOUT||WaitForSingleObject(b,0)==WAIT_TIMEOUT){auto v=Read(p);if(v.flags==FiniteValues&&v.samples>2)coherent&=(v.requested==.6f||v.requested==.3f)&&v.afterThrottle==v.requested&&v.afterBrake==0&&v.beforeThrottle==v.requested/2&&v.beforeBrake==v.requested/4&&v.originalAccelerationTimestamp==123.0+v.requested;}
 WaitForSingleObject(a,INFINITE);WaitForSingleObject(b,INFINITE);CloseHandle(a);CloseHandle(b);
 auto complete=Read(p);Check(coherent&&complete.samples==10002,"serialized writers and coherent copies");
 FILE* file=std::fopen(argv[1],"wb");Check(file!=nullptr,"fixture file");Check(std::fwrite(&complete,1,sizeof complete,file)==sizeof complete,"fixture written");std::fclose(file);
 Shutdown();auto stopped=Read(p);Check(!stopped.initialized&&stopped.samples==complete.samples,"shutdown invalidates retained view");
 Finish(sample,1,1);Check(Read(p).samples==complete.samples,"late finish cannot publish after shutdown");
 Check(Initialize(),"restart while old reader retained");auto restarted=Read(p);Check(restarted.sessionQpc>first.sessionQpc&&restarted.samples==0,"new session replaces old capture");
 Check(restarted.sequence==stopped.sequence+2,"retained reader cannot observe session reload with reused sequence");
 Finish(sample,1,1);Check(Read(p).samples==0,"old-session observation cannot enter new session");
 Shutdown();UnmapViewOfFile(p);CloseHandle(h);
 std::printf("%d pedal observer assertions passed; isolated test namespace only.\n",tests);
}
