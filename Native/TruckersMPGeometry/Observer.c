#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <math.h>
#include <string.h>
#include <TruckersMP/TruckersMP.h>
#include "Protocol.h"

// Only public, read-only vehicle getters. No input API, hooks, game offsets,
// network requests, player identities, or disk writes.
static const TruckersMP_Host *host;
static TruckersMP_Host owned_host;
static const TruckersMP_Core *core;
static const TruckersMP_Players *players;
static const TruckersMP_PlayerHandle *player_api;
static const TruckersMP_Vehicle *vehicles;
static const TruckersMP_Trailer *trailers;
static HANDLE mapping,writer_mutex;
static GeometryFrame *shared;
static uint64_t session, last_sample, sample_frequency;
static bool listening;
#define GEOMETRY_SAMPLE_PERIOD_MS 16ULL

static uint64_t utc_ms(void) {
    FILETIME ft; GetSystemTimePreciseAsFileTime(&ft);
    ULARGE_INTEGER value; value.LowPart=ft.dwLowDateTime;value.HighPart=ft.dwHighDateTime;
    return (value.QuadPart-116444736000000000ULL)/10000ULL;
}
static void publish(GeometryFrame *frame) {
    if(!shared)return;
    if(!frame->utc_ms)frame->utc_ms=utc_ms();
    frame->process_id=GetCurrentProcessId();frame->session=session;
    InterlockedIncrement((volatile LONG*)&shared->sequence);
    MemoryBarrier();
    memcpy((char*)shared+16,(char*)frame+16,sizeof(*frame)-16);
    MemoryBarrier();
    InterlockedIncrement((volatile LONG*)&shared->sequence);
}
static bool body(GeometryBody *out,TruckersMP_Placement placement,TruckersMP_Bounds bounds) {
    double pos[3]={placement.position.x,placement.position.y,placement.position.z};
    float rot[4]={placement.rotation.x,placement.rotation.y,placement.rotation.z,placement.rotation.w};
    float low[3]={bounds.min.x,bounds.min.y,bounds.min.z};
    float high[3]={bounds.max.x,bounds.max.y,bounds.max.z};
    // Raw values remain diagnostic-only when validation fails: validity flags
    // stay clear, so the managed reader cannot use them as vehicle geometry.
    memcpy(out->position,pos,sizeof(pos));memcpy(out->rotation_xyzw,rot,sizeof(rot));
    memcpy(out->minimum,low,sizeof(low));memcpy(out->maximum,high,sizeof(high));
    double norm=0;
    for(int i=0;i<4;i++){if(!isfinite(rot[i]))return false;norm+=rot[i]*rot[i];}
    if(fabs(norm-1)>.01)return false;
    for(int i=0;i<3;i++)if(!isfinite(pos[i])||!isfinite(low[i])||!isfinite(high[i])||high[i]<=low[i]||high[i]-low[i]>50)return false;
    return true;
}
static void sample(void) {
    GeometryFrame frame={0};
    frame.utc_ms=utc_ms(); // Capture origin, never restamped after slow getters.
    TruckersMP_Player_Handle player={0};TruckersMP_Vehicle_Handle vehicle={0};
    TruckersMP_Placement placement={0};TruckersMP_Bounds bounds={0};
    if(!players->GetLocalPlayer(host->m_ctx,&player)){frame.diagnostic_code=GEOMETRY_NO_LOCAL_PLAYER;publish(&frame);return;}
    if(!player_api->GetVehicle(host->m_ctx,player,&vehicle)){frame.diagnostic_code=GEOMETRY_NO_VEHICLE;publish(&frame);return;}
    if(!vehicles->GetPlacement(host->m_ctx,vehicle,&placement)){frame.diagnostic_code=GEOMETRY_NO_PLACEMENT;publish(&frame);return;}
    if(!vehicles->GetBoundingBox(host->m_ctx,vehicle,&bounds)){frame.diagnostic_code=GEOMETRY_NO_BOUNDS;publish(&frame);return;}
    if(!body(&frame.truck,placement,bounds)){frame.diagnostic_code=GEOMETRY_INVALID_BODY;publish(&frame);return;}
    frame.flags=GEOMETRY_TRUCK;
    TruckersMP_Float3 velocity={0};
    if(vehicles->GetLinearVelocity&&vehicles->GetLinearVelocity(host->m_ctx,vehicle,&velocity)&&
        isfinite(velocity.x)&&isfinite(velocity.y)&&isfinite(velocity.z)) {
        frame.velocity[0]=velocity.x;frame.velocity[1]=velocity.y;frame.velocity[2]=velocity.z;
    }
    TruckersMP_Trailer_Handle trailer={0};bool exists=false;
    if(trailers&&trailers->Exists&&vehicles->GetTrailer&&vehicles->GetTrailer(host->m_ctx,vehicle,&trailer)&&
        trailers->Exists(host->m_ctx,trailer,&exists)) {
        frame.flags|=GEOMETRY_TRAILER_KNOWN;
        if(exists&&trailers->GetPlacement&&trailers->GetBoundingBox&&
            trailers->GetPlacement(host->m_ctx,trailer,&placement)&&trailers->GetBoundingBox(host->m_ctx,trailer,&bounds)&&
            body(&frame.trailer,placement,bounds))frame.flags|=GEOMETRY_TRAILER;
        else if(exists)frame.flags&=~GEOMETRY_TRAILER_KNOWN;
    }
    publish(&frame);
}
static void TMP_API on_frame(void *context,uint64_t event,void *payload,uint32_t payload_size) {
    (void)context;(void)event;(void)payload;(void)payload_size;
    LARGE_INTEGER counter;if(!QueryPerformanceCounter(&counter)||!sample_frequency)return;
    uint64_t now=(uint64_t)counter.QuadPart,frequency=sample_frequency;
#if defined(MAUT_GEOMETRY_TEST_ID) && defined(MAUT_GEOMETRY_TEST_CLOCK)
    // Fake-host-only clock injection uses the existing callback, never a new
    // production export or control API. Test payload ticks are microseconds.
    if(payload&&payload_size==sizeof(now)){memcpy(&now,payload,sizeof(now));frequency=1000000;}
#endif
    if(last_sample&&now>=last_sample&&((double)(now-last_sample)*1000/frequency)<GEOMETRY_SAMPLE_PERIOD_MS)return;
    last_sample=now;sample();
}
static void cleanup(void) {
    if(listening&&core&&host)core->RemoveListener(host->m_ctx,TRUCKERSMP_EVT_RENDER_PRE_RENDER,on_frame,NULL);
    listening=false;
    if(shared){GeometryFrame empty={0};empty.diagnostic_code=GEOMETRY_SHUTDOWN;publish(&empty);UnmapViewOfFile(shared);shared=NULL;}
    if(mapping){CloseHandle(mapping);mapping=NULL;}
    if(writer_mutex){ReleaseMutex(writer_mutex);CloseHandle(writer_mutex);writer_mutex=NULL;}
    host=NULL;memset(&owned_host,0,sizeof(owned_host));core=NULL;players=NULL;player_api=NULL;vehicles=NULL;trailers=NULL;last_sample=0;sample_frequency=0;
}
TMP_EXPORT bool TMP_API truckersmp_init(const TruckersMP_Host *h,TruckersMP_PluginDesc *desc) {
    if(host||!h||h->m_size<sizeof(*h)||!h->DeclareVersion||!h->GetApi||!desc)return false;
    if(h->DeclareVersion(h->m_ctx,TRUCKERSMP_SDK_VERSION)!=TruckersMP_Result_Ok)return false;
    // The entry descriptor itself may be temporary. Retain values, as the
    // official C++ Session wrapper does, never a pointer to the caller's host.
    owned_host=*h;host=&owned_host;
    core=host->GetApi(host->m_ctx,TRUCKERSMP_API_CORE);players=host->GetApi(host->m_ctx,TRUCKERSMP_API_PLAYERS);
    player_api=host->GetApi(host->m_ctx,TRUCKERSMP_API_PLAYER_HANDLE);vehicles=host->GetApi(host->m_ctx,TRUCKERSMP_API_VEHICLE);
    trailers=host->GetApi(host->m_ctx,TRUCKERSMP_API_TRAILER);
    if(!core||!core->AddListener||!core->RemoveListener||!players||!players->GetLocalPlayer||!player_api||!player_api->GetVehicle||
        !vehicles||!vehicles->GetPlacement||!vehicles->GetBoundingBox){cleanup();return false;}
    LARGE_INTEGER frequency;
    if(!QueryPerformanceFrequency(&frequency)||frequency.QuadPart<=0){cleanup();return false;}
    sample_frequency=(uint64_t)frequency.QuadPart;
    SetLastError(ERROR_SUCCESS);
    writer_mutex=CreateMutexW(NULL,TRUE,GEOMETRY_WRITER_MUTEX);
    if(!writer_mutex){cleanup();return false;}
    if(GetLastError()==ERROR_ALREADY_EXISTS) {
        DWORD acquired=WaitForSingleObject(writer_mutex,0);
        if(acquired!=WAIT_OBJECT_0&&acquired!=WAIT_ABANDONED){CloseHandle(writer_mutex);writer_mutex=NULL;cleanup();return false;}
    }
    SetLastError(ERROR_SUCCESS);
    mapping=CreateFileMappingW(INVALID_HANDLE_VALUE,NULL,PAGE_READWRITE,0,sizeof(GeometryFrame),GEOMETRY_MAP);
    if(!mapping){cleanup();return false;}
    bool existed=GetLastError()==ERROR_ALREADY_EXISTS;
    shared=MapViewOfFile(mapping,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(GeometryFrame));
    if(!shared){cleanup();return false;}
    if(existed&&shared->process_id&&shared->process_id!=GetCurrentProcessId()) {
        // A managed reader can retain the mapping across a game restart. With
        // the writer mutex owned, reclaim only our known protocol after clean
        // invalidation or a stale heartbeat; never erase another fresh source.
        uint64_t now=utc_ms();
        bool ours=shared->magic==GEOMETRY_MAGIC&&shared->version==GEOMETRY_VERSION&&shared->size==sizeof(*shared);
        bool expired=now>=shared->utc_ms&&now-shared->utc_ms>1000;
        if(!ours||(shared->flags!=0&&!expired)) {
            UnmapViewOfFile(shared);shared=NULL;cleanup();return false;
        }
    }
    LARGE_INTEGER counter;if(!QueryPerformanceCounter(&counter)){cleanup();return false;}session=(uint64_t)counter.QuadPart;
    if(!session)session=1;
    memset(shared,0,sizeof(*shared));shared->magic=GEOMETRY_MAGIC;shared->version=GEOMETRY_VERSION;shared->size=sizeof(*shared);
    if(core->AddListener(host->m_ctx,TRUCKERSMP_EVT_RENDER_PRE_RENDER,on_frame,NULL)!=TruckersMP_Result_Ok){cleanup();return false;}
    listening=true;
    memset(desc,0,sizeof(*desc));desc->m_size=sizeof(*desc);
    strcpy(desc->m_name,"Mautassistent Vehicle Geometry");strcpy(desc->m_author,"Mautassistent");
    strcpy(desc->m_version,"0.3.18");strcpy(desc->m_description,"Read-only local truck and trailer model bounds. No input control.");
    sample();return true;
}
TMP_EXPORT void TMP_API truckersmp_shutdown(void){cleanup();}
