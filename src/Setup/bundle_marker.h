#ifndef __BUNDLE_MARKER_H__
#define __BUNDLE_MARKER_H__

#include <cstdint>

#pragma pack(push, 1)
union bundle_marker_t
{
public:
    uint8_t placeholder[48];
    struct
    {
        int64_t bundle_header_offset;
        int64_t bundle_header_length;
        uint8_t signature[32];
    } locator;

    static void header_offset(int64_t* pOffset, int64_t* pLength);
    static bool is_bundle()
    {
        int64_t offset, length;
        header_offset(&offset, &length);
        return offset != 0;
    }
};
#pragma pack(pop)

#endif // __BUNDLE_MARKER_H__