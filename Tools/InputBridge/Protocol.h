#pragma once
#include <windows.h>
#include <stdint.h>
#include <stddef.h>

#define MAUT_STRINGIFY_INNER(x) #x
#define MAUT_STRINGIFY(x) MAUT_STRINGIFY_INNER(x)
#ifdef MAUT_INPUT_TEST_ID
#define MAUT_INPUT_COMMAND_MAP "Local\\MautInputBridgeTest_" MAUT_STRINGIFY(MAUT_INPUT_TEST_ID) "_Commands"
#define MAUT_INPUT_DIAGNOSTIC_MAP "Local\\MautInputBridgeTest_" MAUT_STRINGIFY(MAUT_INPUT_TEST_ID) "_Status"
#else
#define MAUT_INPUT_COMMAND_MAP "Local\\SCSControls"
#define MAUT_INPUT_DIAGNOSTIC_MAP "Local\\MautInputBridgeStatus_v1"
#endif

// Callback writers are serialized. Consumers copy between identical even seq.
// Returned is SDK callback output, NOT logical or effective input acknowledgement.
struct MautReturnedAxis {
    uint64_t frame;
    int64_t qpc;
    float value;
    uint32_t padding;
};
struct MautInputStatus {
    volatile LONG sequence;
    uint32_t magic;                  // 0x4d494231 = MIB1
    uint32_t version;
    uint32_t bytes;
    uint32_t process_id;
    uint32_t initialized;
    int64_t session_qpc;
    int64_t qpc_frequency;
    uint32_t active_known;           // only set by input_active_callback
    uint32_t active;
    uint64_t active_changes;
    int64_t last_active_change_qpc;
    int64_t last_callback_qpc;       // includes end-of-frame not_found
    int64_t last_frame_qpc;
    int64_t last_after_activation_qpc;
    uint64_t frames_started;
    uint64_t frames_completed;
    uint64_t events_returned;
    uint32_t last_input_index;
    uint32_t last_callback_flags;
    MautReturnedAxis steering;
    MautReturnedAxis throttle;
    MautReturnedAxis brake;
    MautReturnedAxis activate;
};
static_assert(sizeof(MautReturnedAxis) == 24, "Axis ABI");
static_assert(offsetof(MautInputStatus, steering) == 120, "Status ABI");
static_assert(sizeof(MautInputStatus) == 216, "Status ABI");
