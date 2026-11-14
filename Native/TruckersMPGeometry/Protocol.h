#pragma once
#include <stdint.h>
#include <stddef.h>

#define GEOMETRY_MAP L"Local\\ETS2LA-MautGeometry-v1"
#define GEOMETRY_MAGIC 0x3147544dU
#define GEOMETRY_VERSION 1U
#define GEOMETRY_TRUCK 1U
#define GEOMETRY_TRAILER 2U
#define GEOMETRY_TRAILER_KNOWN 4U

typedef struct GeometryBody {
    double position[3];
    float rotation_xyzw[4];
    float minimum[3];
    float maximum[3];
} GeometryBody;

typedef struct GeometryFrame {
    uint32_t magic, version, size;
    volatile int32_t sequence;
    uint64_t utc_ms;
    uint32_t process_id, flags;
    uint64_t session;
    GeometryBody truck, trailer;
    float velocity[3];
    uint8_t reserved[76];
} GeometryFrame;

_Static_assert(sizeof(GeometryBody)==64,"Body ABI");
_Static_assert(sizeof(GeometryFrame)==256,"Frame ABI");
_Static_assert(offsetof(GeometryFrame,truck)==40,"Truck ABI");
_Static_assert(offsetof(GeometryFrame,trailer)==104,"Trailer ABI");
_Static_assert(offsetof(GeometryFrame,velocity)==168,"Velocity ABI");
