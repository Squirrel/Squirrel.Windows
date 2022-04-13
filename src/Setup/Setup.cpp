#include <windows.h>
#include <versionhelpers.h>
#include <string>
#include <fstream>
#include "bundle_marker.h"
#include "platform_util.h"

using namespace std;
namespace fs = std::filesystem;

// Prints to the provided buffer a nice number of bytes (KB, MB, GB, etc)
wstring pretty_bytes(uint64_t bytes)
{
    wchar_t buf[128];
    const wchar_t* suffixes[7];
    suffixes[0] = L"B";
    suffixes[1] = L"KB";
    suffixes[2] = L"MB";
    suffixes[3] = L"GB";
    suffixes[4] = L"TB";
    suffixes[5] = L"PB";
    suffixes[6] = L"EB";
    uint64_t s = 0; // which suffix to use
    double count = bytes;
    while (count >= 1000 && s < 7) {
        s++;
        count /= 1000;
    }
    if (count - floor(count) == 0.0)
        swprintf(buf, 128, L"%d %s", (int)count, suffixes[s]);
    else
        swprintf(buf, 128, L"%.1f %s", count, suffixes[s]);

    return wstring(buf);
}

int WINAPI wWinMain(_In_ HINSTANCE hInstance, _In_opt_ HINSTANCE hPrevInstance, _In_ PWSTR pCmdLine, _In_ int nCmdShow)
{
    if (!IsWindows7SP1OrGreater()) {
        util::show_error_dialog(L"This application requires Windows 7 SP1 or later and cannot be installed on this computer.");
        return 0;
    }

    wstring updaterPath = util::get_temp_file_path(L"exe");
    wstring packagePath = util::get_temp_file_path(L"nupkg");
    uint8_t* memAddr = 0;

    try {
        // locate bundled package and map to memory
        memAddr = util::mmap_read(util::get_current_process_path(), 0);
        if (!memAddr) {
            throw wstring(L"Unable to memmap current executable. Is there enough available system memory?");
        }

        int64_t packageOffset, packageLength;
        bundle_marker_t::header_offset(&packageOffset, &packageLength);
        uint8_t* pkgStart = memAddr + packageOffset;
        if (packageOffset == 0 || packageLength == 0) {
            throw wstring(L"The embedded package containing the application to install was not found. Please contact the application author.");
        }

        // rough check for sufficient disk space before extracting anything
        // required space is size of compressed nupkg, size of extracted app, 
        // and squirrel overheads (incl temp files). the constant 0.38 is a
        // aggressive estimate on what the compression ratio might be.
        int64_t squirrelOverhead = 50 * 1000 * 1000;
        int64_t requiredSpace = squirrelOverhead + (packageLength * 3) + (packageLength / (double)0.38);
        if (!util::check_diskspace(requiredSpace)) {
            throw wstring(L"Insufficient disk space. This application requires at least " + pretty_bytes(requiredSpace) + L" free space to be installed.");
        }

        // extract Update.exe and embedded nuget package
        util::extractUpdateExe(pkgStart, packageLength, updaterPath);
        std::ofstream(packagePath, std::ios::binary).write((char*)pkgStart, packageLength);

        // run installer and forward our command line arguments
        wstring cmd = L"\"" + updaterPath + L"\" --setup \"" + packagePath + L"\" " + pCmdLine;
        util::wexec(cmd.c_str());
    }
    catch (wstring wsx) {
        util::show_error_dialog(L"An error occurred while running setup. " + wsx);
    }
    catch (...) {
        util::show_error_dialog(L"An unknown error occurred while running setup. Please contact the application author.");
    }

    // clean-up resources
    if (memAddr) util::munmap(memAddr);
    DeleteFile(updaterPath.c_str());
    DeleteFile(packagePath.c_str());
    return 0;
}