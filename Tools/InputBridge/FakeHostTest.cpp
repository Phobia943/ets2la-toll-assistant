#ifndef MAUT_INPUT_TEST_ID
#error Tests must use unique test-only maps, never live command/status maps.
#endif
#include "Protocol.h"
#include "LegacyInputs.h"
#include <stdio.h>
#include <math.h>
#include <string.h>
#include <stdlib.h>

static scs_input_device_t registered{};
static bool reject_registration;
static unsigned checks;
template<class T> static T load_export(HMODULE dll,const char* name) {
    FARPROC raw=GetProcAddress(dll,name);
    T result{};
    static_assert(sizeof(result)==sizeof(raw),"Win64 function-pointer ABI");
    memcpy(&result,&raw,sizeof(result));
    return result;
}
static void check(bool passed, const char* description) {
    ++checks;
    if(!passed) { fprintf(stderr,"FAIL %s\n",description); exit(1); }
    printf("PASS %s\n",description);
}
static SCSAPI_RESULT register_device(const scs_input_device_t* const device) {
    registered = *device;
    return reject_registration ? SCS_RESULT_generic_error : SCS_RESULT_ok;
}
static SCSAPI_VOID log_message(const scs_log_type_t, const scs_string_t) {}
__attribute__((noinline)) static void overwrite_old_stack() {
    volatile unsigned char bytes[16384];
    for(unsigned i=0;i<sizeof(bytes);++i) bytes[i]=static_cast<unsigned char>(i*17);
}
static MautInputStatus read_status(const MautInputStatus* source) {
    MautInputStatus copy{};
    for(unsigned i=0;i<100;++i) {
        const LONG before=source->sequence;
        if(before&1) continue;
        MemoryBarrier(); memcpy(&copy,source,sizeof(copy)); MemoryBarrier();
        if(source->sequence==before) return copy;
    }
    check(false,"Coherent diagnostic read"); return copy;
}
static scs_input_event_t next(unsigned flags=0) {
    scs_input_event_t event{};
    if(registered.input_event_callback(&event,flags,registered.callback_context)!=SCS_RESULT_ok) {
        fprintf(stderr,"Expected event\n"); exit(1);
    }
    return event;
}
static void until(unsigned index, unsigned first_flags=0) {
    for(unsigned i=0;i<=index;++i) {
        auto event=next(i==0?first_flags:0);
        if(event.input_index!=i) { fprintf(stderr,"Wrong event index\n"); exit(1); }
    }
}
static DWORD WINAPI activity_worker(void*) {
    for(unsigned i=0;i<10000;++i) registered.input_active_callback(static_cast<scs_u8_t>(i%2),registered.callback_context);
    return 0;
}
static DWORD WINAPI events_worker(void*) {
    for(unsigned i=0;i<10000;++i) next(SCS_INPUT_EVENT_CALLBACK_FLAG_first_in_frame);
    return 0;
}
int main(int argc,char** argv) {
    check(argc==3,"Test DLL and offline fixture arguments supplied");
    HMODULE dll=LoadLibraryA(argv[1]); check(dll!=nullptr,"Isolated DLL loads");
    auto marker=load_export<unsigned(*)()>(dll,"MautInputBridgeTestMarker");
    check(marker&&marker()==0x54455354,"Test-only map marker before initialization");
    auto init=load_export<decltype(&scs_input_init)>(dll,"scs_input_init");
    auto shutdown=load_export<decltype(&scs_input_shutdown)>(dll,"scs_input_shutdown");
    check(init&&shutdown,"SCS exports present");
    scs_input_init_params_v100_t params{};
    params.register_device=register_device; params.common.log=log_message;
    check(init(0,&params)==SCS_RESULT_unsupported,"Unsupported SDK is rejected");
    check(init(SCS_INPUT_VERSION_1_00,nullptr)==SCS_RESULT_generic_error,"Null SDK parameters rejected");
    check(init(SCS_INPUT_VERSION_1_00,&params)==SCS_RESULT_ok,"Actual SDK registration succeeds");
    check(init(SCS_INPUT_VERSION_1_00,&params)==SCS_RESULT_generic_error,"Duplicate initialization rejected");
    check(registered.input_count==276&&registered.type==SCS_INPUT_DEVICE_TYPE_semantical&&
        strcmp(registered.name,"laneassist")==0,"Original registration identity and count preserved");
    bool abi=true;
    for(unsigned i=0;i<legacy_input_count;++i) abi &= registered.inputs[i].value_type==legacy_inputs[i].value_type&&
        strcmp(registered.inputs[i].name,legacy_inputs[i].name)==0&&strcmp(registered.inputs[i].display_name,legacy_inputs[i].display_name)==0;
    check(abi,"All 276 names, types and ordering match pinned legacy ABI");
    MEMORY_BASIC_INFORMATION region{};
    check(VirtualQuery(registered.callback_context,&region,sizeof(region))&&region.AllocationBase==dll,
        "Callback context resides in loaded DLL storage, not expired initialization stack");
    overwrite_old_stack();
    HANDLE writer=OpenFileMappingA(FILE_MAP_ALL_ACCESS,FALSE,MAUT_INPUT_COMMAND_MAP);
    auto* commands=static_cast<unsigned char*>(MapViewOfFile(writer,FILE_MAP_ALL_ACCESS,0,0,legacy_bytes));
    HANDLE observer=OpenFileMappingA(FILE_MAP_READ,FALSE,MAUT_INPUT_DIAGNOSTIC_MAP);
    auto* status=static_cast<const MautInputStatus*>(MapViewOfFile(observer,FILE_MAP_READ,0,0,sizeof(MautInputStatus)));
    check(commands&&status,"Test maps opened; observer read-only");
    auto s=read_status(status);
    check(s.magic==0x4d494231&&s.version==1&&s.bytes==216&&s.initialized&&s.qpc_frequency>0,
        "Diagnostic header and clock available");
    check(!s.active_known&&s.frames_started==0&&s.throttle.frame==0,"Unknown activity and no invented acknowledgement at startup");
    registered.input_active_callback(1,registered.callback_context);
    s=read_status(status);
    check(s.active_known&&s.active&&s.active_changes==1&&s.last_active_change_qpc>0,"Actual activation notification recorded");
    auto write_float=[&](unsigned index,float value){memcpy(commands+legacy_offsets[index],&value,sizeof(value));};
    write_float(70,.25f); write_float(71,.4f); write_float(72,.6f); commands[134]=2;
    until(70,3);
    s=read_status(status);
    check(s.frames_started==1&&s.frames_completed==0&&s.events_returned==71&&s.steering.frame==1&&s.steering.value==.25f,
        "Partial SDK enumeration records actual steering event only");
    check(s.throttle.frame==0&&s.brake.frame==0&&s.activate.frame==0,"Unrequested pedals and activate are not acknowledged");
    write_float(71,.7f);
    check(next().value_float.value==.7f,"Each event reads current host value, no queued earlier throttle");
    check(next().value_float.value==.6f,"Brake returned independently");
    next(); check(next().value_bool.value==1,"Legacy nonzero bool is normalized");
    for(unsigned i=75;i<legacy_input_count;++i) next();
    scs_input_event_t event{};
    check(registered.input_event_callback(&event,0,registered.callback_context)==SCS_RESULT_not_found,"Frame drains at exact input count");
    s=read_status(status);
    check(s.frames_completed==1&&s.events_returned==276&&s.throttle.frame==1&&s.throttle.value==.7f&&s.brake.value==.6f&&s.activate.value==1,
        "Completed frame and per-axis callback values recorded");
    registered.input_event_callback(&event,0,registered.callback_context);
    check(read_status(status).frames_completed==1,"Repeated drain does not invent another completed frame");
    registered.input_active_callback(0,registered.callback_context);
    auto inactive=read_status(status);
    check(!inactive.active&&inactive.active_changes==2&&inactive.frames_started==1,"Inactivity notification does not simulate frame reception");
    // Fake host expires its original command / F9 clears it while SDK inactive.
    // The bridge must not resurrect the partial previous frame on reactivation.
    memset(commands,0,legacy_bytes);
    registered.input_active_callback(1,registered.callback_context);
    until(74,3);
    s=read_status(status);
    check(s.frames_started==2&&s.frames_completed==1&&s.throttle.value==0&&s.brake.value==0&&s.activate.value==0,
        "Host-expired/F9-neutral values remain neutral after reactivation, including activate");
    check(s.last_after_activation_qpc>=inactive.last_callback_qpc&&s.throttle.frame==2,"Reactivation diagnostics carry new frame and clock");
    write_float(70,INFINITY); write_float(71,NAN); write_float(72,3);
    until(69,1);
    check(next().value_float.value==0&&next().value_float.value==0&&next().value_float.value==1,
        "Invalid floats neutralize; finite overflow clamps without undefined clamp arguments");
    write_float(70,-2); until(69,1); check(next().value_float.value==-1,"Negative finite overflow clamps");
    // This negative control states the actual protocol boundary. Legacy has no
    // original-issued timestamp: the bridge cannot prove a retained raw value fresh.
    write_float(71,.3f); until(70,1); check(next().value_float.value==.3f,
        "Legacy raw input has no claimed game-side TTL or host-crash acknowledgement");
    auto fixture=read_status(status);
    FILE* file=fopen(argv[2],"wb");
    check(file&&fwrite(&fixture,sizeof(fixture),1,file)==1,"Offline native diagnostic fixture written");
    fclose(file);
    auto before_overlap=read_status(status);
    HANDLE workers[2]={CreateThread(nullptr,0,activity_worker,nullptr,0,nullptr),CreateThread(nullptr,0,events_worker,nullptr,0,nullptr)};
    check(workers[0]&&workers[1],"Concurrent callback workers started on isolated maps");
    check(WaitForMultipleObjects(2,workers,TRUE,10000)==WAIT_OBJECT_0,"Overlapping callback writers complete without deadlock");
    CloseHandle(workers[0]);CloseHandle(workers[1]);
    auto after_overlap=read_status(status);
    check(after_overlap.active_changes==before_overlap.active_changes+10000&&
        after_overlap.events_returned==before_overlap.events_returned+10000&&
        after_overlap.sequence==before_overlap.sequence+40000,
        "Activity and event callbacks share serialized status transactions");
    shutdown();
    check(!read_status(status).initialized,"Retained read-only observer sees shutdown");
    check(registered.input_event_callback(&event,1,registered.callback_context)==SCS_RESULT_not_found,"Late callback after shutdown emits nothing");
    UnmapViewOfFile(status); CloseHandle(observer); UnmapViewOfFile(commands); CloseHandle(writer);
    reject_registration=true;
    check(init(SCS_INPUT_VERSION_1_00,&params)==SCS_RESULT_generic_error,"SDK registration rejection cleans resources");
    HANDLE abandoned=OpenFileMappingA(FILE_MAP_READ,FALSE,MAUT_INPUT_DIAGNOSTIC_MAP);
    check(abandoned==nullptr,"Failed registration leaves no diagnostic mapping");
    reject_registration=false;
    check(init(SCS_INPUT_VERSION_1_00,&params)==SCS_RESULT_ok,"Clean reinitialization succeeds after failed registration");
    shutdown(); shutdown(); FreeLibrary(dll);
    printf("%u isolated input bridge assertions passed. No live maps or game APIs were used.\n",checks);
    return 0;
}
