#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <math.h>
#include <string.h>
#include <TruckersMP/TruckersMP.h>
#include "Protocol.h"

static TruckersMP_EventFn callback;
static int failures,reads,registered,removed;
static bool has_vehicle=true,has_trailer=true,invalid_bounds=false,deny_api=false,deny_trailer_pose=false;
static TruckersMP_EResult declare_version(TruckersMP_PluginContext *c,uint32_t v){(void)c;return v==TRUCKERSMP_SDK_VERSION?0:1;}
static TruckersMP_EResult add_listener(TruckersMP_PluginContext *c,uint64_t e,TruckersMP_EventFn fn,void *u){(void)c;(void)u;if(e!=TRUCKERSMP_EVT_RENDER_PRE_RENDER)return 1;callback=fn;registered++;return 0;}
static TruckersMP_EResult remove_listener(TruckersMP_PluginContext *c,uint64_t e,TruckersMP_EventFn fn,void *u){(void)c;(void)e;(void)u;if(callback!=fn)return 1;callback=NULL;removed++;return 0;}
static bool local_player(TruckersMP_PluginContext *c,TruckersMP_Player_Handle *p){(void)c;p->m_value=1;reads++;return has_vehicle;}
static bool vehicle(TruckersMP_PluginContext *c,TruckersMP_Player_Handle p,TruckersMP_Vehicle_Handle *v){(void)c;(void)p;v->m_value=2;reads++;return true;}
static bool placement(TruckersMP_PluginContext *c,TruckersMP_Vehicle_Handle v,TruckersMP_Placement *p){(void)c;(void)v;memset(p,0,sizeof(*p));p->position.x=10;p->position.y=2;p->position.z=20;p->rotation.w=1;reads++;return true;}
static bool bounds(TruckersMP_PluginContext *c,TruckersMP_Vehicle_Handle v,TruckersMP_Bounds *b){(void)c;(void)v;b->min=(TruckersMP_Float3){-1,0,-5};b->max=(TruckersMP_Float3){1,4,3};if(invalid_bounds)b->max.y=NAN;reads++;return true;}
static bool trailer(TruckersMP_PluginContext *c,TruckersMP_Vehicle_Handle v,TruckersMP_Trailer_Handle *t){(void)c;(void)v;t->m_value=3;reads++;return true;}
static bool exists(TruckersMP_PluginContext *c,TruckersMP_Trailer_Handle t,bool *value){(void)c;(void)t;*value=has_trailer;reads++;return true;}
static bool trailer_pose(TruckersMP_PluginContext *c,TruckersMP_Trailer_Handle t,TruckersMP_Placement *p){(void)t;return !deny_trailer_pose&&placement(c,(TruckersMP_Vehicle_Handle){2},p);}
static bool trailer_bounds(TruckersMP_PluginContext *c,TruckersMP_Trailer_Handle t,TruckersMP_Bounds *b){(void)t;return bounds(c,(TruckersMP_Vehicle_Handle){2},b);}
static TruckersMP_Core core={.AddListener=add_listener,.RemoveListener=remove_listener};
static TruckersMP_Players players={.GetLocalPlayer=local_player};
static TruckersMP_PlayerHandle player_api={.GetVehicle=vehicle};
static TruckersMP_Vehicle vehicles={.GetTrailer=trailer,.GetPlacement=placement,.GetBoundingBox=bounds};
static TruckersMP_Trailer trailers={.Exists=exists,.GetPlacement=trailer_pose,.GetBoundingBox=trailer_bounds};
static const void *get_api(TruckersMP_PluginContext *c,uint64_t id) {
    (void)c;if(deny_api)return NULL;
    if(id==TRUCKERSMP_API_CORE)return &core;if(id==TRUCKERSMP_API_PLAYERS)return &players;
    if(id==TRUCKERSMP_API_PLAYER_HANDLE)return &player_api;if(id==TRUCKERSMP_API_VEHICLE)return &vehicles;
    if(id==TRUCKERSMP_API_TRAILER)return &trailers;return NULL;
}
static void check(bool valid,const char *name){printf("%s %s\n",valid?"PASS":"FAIL",name);if(!valid)failures++;}
int main(int argc,char **argv) {
    if(argc<2||argc>3)return 2;
    HMODULE dll=LoadLibraryA(argv[1]);if(!dll){printf("LoadLibrary failed %lu\n",GetLastError());return 2;}
    TruckersMP_PluginEntryFn init=NULL;TruckersMP_PluginShutdownFn shutdown=NULL;
    FARPROC symbol=GetProcAddress(dll,"truckersmp_init");memcpy(&init,&symbol,sizeof(init));
    symbol=GetProcAddress(dll,"truckersmp_shutdown");memcpy(&shutdown,&symbol,sizeof(shutdown));
    check(init&&shutdown,"exact SDK exports present");if(!init||!shutdown)return 2;
    TruckersMP_Host host={.m_size=sizeof(host),.m_ctx=(TruckersMP_PluginContext*)1,.DeclareVersion=declare_version,.GetApi=get_api};
    TruckersMP_PluginDesc desc={0};
    check(init(&host,&desc),"initialize against fake SDK host");
    HANDLE map=OpenFileMappingW(FILE_MAP_READ,FALSE,GEOMETRY_MAP);
    const GeometryFrame *frame=map?MapViewOfFile(map,FILE_MAP_READ,0,0,sizeof(GeometryFrame)):NULL;
    check(frame&&frame->magic==GEOMETRY_MAGIC&&frame->size==256&&!(frame->sequence&1),"readable coherent protocol frame");
    if(!frame)return 2;
    check((frame->flags&7)==7&&frame->truck.minimum[2]==-5&&frame->truck.maximum[1]==4,"local truck and real trailer bounds observed");
    if(argc==3) {
        FILE *fixture=fopen(argv[2],"wb");
        check(fixture&&fwrite(frame,1,sizeof(*frame),fixture)==sizeof(*frame),"export native ABI fixture for managed decoder");
        if(fixture)fclose(fixture);
    }
    check(registered==1&&callback&&reads>0,"only registered SDK frame path supplies samples");
    check(!init(&host,&desc),"double initialization cannot create a second writer");
    deny_trailer_pose=true;Sleep(110);callback(NULL,TRUCKERSMP_EVT_RENDER_PRE_RENDER,NULL,0);
    check(frame->flags==GEOMETRY_TRUCK,"failed trailer measurement cannot be declared absent");deny_trailer_pose=false;
    has_trailer=false;Sleep(110);callback(NULL,TRUCKERSMP_EVT_RENDER_PRE_RENDER,NULL,0);
    check((frame->flags&7)==5,"absent trailer is explicit rather than an unverified default");
    invalid_bounds=true;Sleep(110);callback(NULL,TRUCKERSMP_EVT_RENDER_PRE_RENDER,NULL,0);
    check(frame->flags==0,"invalid bounds never become a valid profile");
    invalid_bounds=false;has_vehicle=false;Sleep(110);callback(NULL,TRUCKERSMP_EVT_RENDER_PRE_RENDER,NULL,0);
    check(frame->flags==0,"despawn clears validity while retaining writer heartbeat");
    shutdown();check(frame->flags==0&&removed==1&&!callback,"shutdown unregisters and invalidates the last frame");
    has_vehicle=true;has_trailer=true;Sleep(2);
    check(init(&host,&desc),"reload with a retained read handle");
    shutdown();
    HANDLE writable_map=OpenFileMappingW(FILE_MAP_WRITE,FALSE,GEOMETRY_MAP);
    GeometryFrame *retained=writable_map?MapViewOfFile(writable_map,FILE_MAP_WRITE,0,0,sizeof(*retained)):NULL;
    check(retained!=NULL,"prepare a retained mapping across producer process changes");
    if(retained) {
        retained->process_id=0xffffffffU;
        check(init(&host,&desc),"new producer reclaims a cleanly invalidated known mapping");shutdown();
        retained->process_id=0xffffffffU;retained->flags=GEOMETRY_TRUCK;
        check(!init(&host,&desc)&&retained->flags==GEOMETRY_TRUCK,"fresh foreign producer data is not erased");
        retained->utc_ms=1;
        check(init(&host,&desc),"new producer can recover a stale abandoned known mapping");shutdown();
        UnmapViewOfFile(retained);CloseHandle(writable_map);
    }
    deny_api=true;
    check(!init(&host,&desc),"missing API tables refuse initialization");
    UnmapViewOfFile(frame);CloseHandle(map);FreeLibrary(dll);
    printf("%s Native observer checks; no game process or game inputs used.\n",failures?"FAIL":"PASS");
    return failures?1:0;
}
