// Isolated compatible legacy bridge candidate. No focus manipulation or hooks.
#include "Protocol.h"
#include "LegacyInputs.h"
#include <math.h>
#include <string.h>

struct InputContext {
    HANDLE commands_handle;
    unsigned char* commands;
    HANDLE status_handle;
    MautInputStatus* status;
    unsigned input_index;
    bool frame_finished;
    bool initialized;
};
// SDK callbacks outlive scs_input_init. This storage lasts until DLL unload.
static InputContext input_context{};
// The event callback is on the game thread, but the activity callback has no
// matching SDK thread guarantee. Serialize writers and map lifetime together.
static SRWLOCK callback_lock = SRWLOCK_INIT;
struct CallbackLock {
    CallbackLock() { AcquireSRWLockExclusive(&callback_lock); }
    ~CallbackLock() { ReleaseSRWLockExclusive(&callback_lock); }
};
static int64_t now_qpc() { LARGE_INTEGER q{}; QueryPerformanceCounter(&q); return q.QuadPart; }
static void begin_status(MautInputStatus* s) { InterlockedIncrement(&s->sequence); }
static void end_status(MautInputStatus* s) { InterlockedIncrement(&s->sequence); }

static void close_maps() {
    CallbackLock lock;
    auto& c = input_context;
    if(c.status) {
        begin_status(c.status); c.status->initialized = 0; end_status(c.status);
        UnmapViewOfFile(c.status); c.status = nullptr;
    }
    if(c.status_handle) { CloseHandle(c.status_handle); c.status_handle = nullptr; }
    if(c.commands) { UnmapViewOfFile(c.commands); c.commands = nullptr; }
    if(c.commands_handle) { CloseHandle(c.commands_handle); c.commands_handle = nullptr; }
    c.initialized = false;
    c.input_index = legacy_input_count;
}

static SCSAPI_VOID active_callback(const scs_u8_t active, const scs_context_t context) {
    CallbackLock lock;
    auto& c = *static_cast<InputContext*>(context);
    if(!c.initialized || !c.status) return;
    begin_status(c.status);
    c.status->active_known = 1;
    c.status->active = active != 0;
    c.status->active_changes++;
    c.status->last_active_change_qpc = now_qpc();
    end_status(c.status);
    // Notification only. It grants no permission to force device activation.
}

static SCSAPI_RESULT event_callback(scs_input_event_t* const event, const scs_u32_t flags, const scs_context_t context) {
    CallbackLock lock;
    auto& c = *static_cast<InputContext*>(context);
    if(!c.initialized || !c.commands || !c.status || !event) return SCS_RESULT_not_found;
    auto* s = c.status;
    const auto qpc = now_qpc();
    begin_status(s);
    s->last_callback_qpc = qpc;
    s->last_callback_flags = flags;
    if(flags & (SCS_INPUT_EVENT_CALLBACK_FLAG_first_in_frame | SCS_INPUT_EVENT_CALLBACK_FLAG_first_after_activation)) {
        c.input_index = 0;
        c.frame_finished = false;
        s->frames_started++;
        s->last_frame_qpc = qpc;
        if(flags & SCS_INPUT_EVENT_CALLBACK_FLAG_first_after_activation) s->last_after_activation_qpc = qpc;
    }
    if(c.input_index >= legacy_input_count) {
        if(!c.frame_finished) { s->frames_completed++; c.frame_finished = true; }
        end_status(s);
        return SCS_RESULT_not_found;
    }
    const unsigned index = c.input_index++;
    event->input_index = index;
    float value;
    // Read when the SDK asks for this event: no persistent queue, lease renewal,
    // synthetic origin time, or replay of an earlier frame after reactivation.
    if(legacy_inputs[index].value_type == SCS_VALUE_TYPE_float) {
        memcpy(&value, c.commands + legacy_offsets[index], sizeof(value));
        if(!isfinite(value)) value = 0;
        else if(value < -1) value = -1;
        else if(value > 1) value = 1;
        event->value_float.value = value;
    } else {
        value = c.commands[legacy_offsets[index]] != 0 ? 1.0f : 0.0f;
        event->value_bool.value = static_cast<scs_u8_t>(value);
    }
    MautReturnedAxis* axis = index == 70 ? &s->steering : index == 71 ? &s->throttle :
        index == 72 ? &s->brake : index == 74 ? &s->activate : nullptr;
    if(axis) *axis = {s->frames_started, qpc, value, 0};
    s->last_input_index = index;
    s->events_returned++;
    end_status(s);
    return SCS_RESULT_ok;
}

extern "C" SCSAPI_RESULT scs_input_init(const scs_u32_t version, const scs_input_init_params_t* const params) {
    if(version != SCS_INPUT_VERSION_1_00) return SCS_RESULT_unsupported;
    if(!params || input_context.initialized) return SCS_RESULT_generic_error;
    auto* api = static_cast<const scs_input_init_params_v100_t*>(params);
    if(!api->register_device) return SCS_RESULT_generic_error;
    auto& c = input_context;
    c.status_handle = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(MautInputStatus), MAUT_INPUT_DIAGNOSTIC_MAP);
    // Refuse a second owner; do not reset another adapter's diagnostics.
    if(!c.status_handle || GetLastError() == ERROR_ALREADY_EXISTS) { close_maps(); return SCS_RESULT_generic_error; }
    c.status = static_cast<MautInputStatus*>(MapViewOfFile(c.status_handle, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(MautInputStatus)));
    c.commands_handle = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, legacy_bytes, MAUT_INPUT_COMMAND_MAP);
    if(c.commands_handle) c.commands = static_cast<unsigned char*>(MapViewOfFile(c.commands_handle, FILE_MAP_ALL_ACCESS, 0, 0, legacy_bytes));
    if(!c.status || !c.commands) { close_maps(); return SCS_RESULT_generic_error; }
    // Same startup neutralization as the original bridge; no subsequent writes.
    memset(c.commands, 0, legacy_bytes);
    memset(c.status, 0, sizeof(*c.status));
    LARGE_INTEGER frequency{}; QueryPerformanceFrequency(&frequency);
    c.status->magic = 0x4d494231;
    c.status->version = 1;
    c.status->bytes = sizeof(*c.status);
    c.status->process_id = GetCurrentProcessId();
    c.status->session_qpc = now_qpc();
    c.status->qpc_frequency = frequency.QuadPart;
    c.status->last_input_index = UINT32_MAX;
    c.initialized = true;
    c.input_index = legacy_input_count;
    c.frame_finished = true;
    scs_input_device_t device{};
    device.name = "laneassist";
    device.display_name = "ETS2 Lane Assist";
    device.type = SCS_INPUT_DEVICE_TYPE_semantical;
    device.input_count = legacy_input_count;
    device.inputs = legacy_inputs;
    device.callback_context = &input_context;
    device.input_active_callback = active_callback;
    device.input_event_callback = event_callback;
    if(api->register_device(&device) != SCS_RESULT_ok) {
        close_maps(); return SCS_RESULT_generic_error;
    }
    // register_device may invoke a callback synchronously; never hold its
    // callback lock across registration itself.
    { CallbackLock lock; begin_status(c.status); c.status->initialized = 1; end_status(c.status); }
    return SCS_RESULT_ok;
}
extern "C" SCSAPI_VOID scs_input_shutdown() { close_maps(); }
#ifdef MAUT_INPUT_TEST_ID
extern "C" __declspec(dllexport) unsigned MautInputBridgeTestMarker() { return 0x54455354; }
#endif
// SDK shutdown owns cleanup. Never acquire a callback lock under loader lock;
// process termination reclaims handles even if the SDK cannot call shutdown.
BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
