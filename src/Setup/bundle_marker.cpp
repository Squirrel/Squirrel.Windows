#include "bundle_marker.h"
#include <string>

using namespace std;

void bundle_marker_t::header_offset(int64_t* pOffset, int64_t* pLength)
{
    // Contains the bundle_placeholder default value at compile time.
    // the first 8 bytes are replaced by squirrel with the offset 
    // where the package is located.
    static volatile uint8_t placeholder[] =
    {
        // 8 bytes represent the package offset 
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        // 8 bytes represent the package length 
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        // 64 bytes represent the bundle signature: SHA-256 for "squirrel bundle"
        0x94, 0xf0, 0xb1, 0x7b, 0x68, 0x93, 0xe0, 0x29,
        0x37, 0xeb, 0x34, 0xef, 0x53, 0xaa, 0xe7, 0xd4,
        0x2b, 0x54, 0xf5, 0x70, 0x7e, 0xf5, 0xd6, 0xf5,
        0x78, 0x54, 0x98, 0x3e, 0x5e, 0x94, 0xed, 0x7d
    };

    volatile bundle_marker_t* marker = reinterpret_cast<volatile bundle_marker_t*>(placeholder);
    *pOffset = marker->locator.bundle_header_offset;
    *pLength = marker->locator.bundle_header_length;
}

