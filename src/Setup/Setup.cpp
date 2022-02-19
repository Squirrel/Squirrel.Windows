#include <windows.h>
#include <versionhelpers.h>
#include <string>
#include <functional>
#include "unzip.h"
#include "bundle_marker.h"
#include "platform_util.h"

using namespace std;

// https://stackoverflow.com/a/874160/184746
bool hasEnding(std::wstring const& fullString, std::wstring const& ending)
{
    if (fullString.length() >= ending.length()) {
        return (0 == fullString.compare(fullString.length() - ending.length(), ending.length(), ending));
    }
    return false;
}

void unzipSingleFile(BYTE* zipBuf, DWORD cZipBuf, wstring fileLocation, std::function<bool(ZIPENTRY&)>& predicate)
{
    HZIP zipFile = OpenZip(zipBuf, cZipBuf, NULL);

    bool unzipSuccess = false;

    ZRESULT zr;
    int index = 0;
    do {
        ZIPENTRY zentry;
        zr = GetZipItem(zipFile, index, &zentry);
        if (zr != ZR_OK && zr != ZR_MORE) {
            break;
        }

        if (predicate(zentry)) {
            auto zaunz = UnzipItem(zipFile, index, fileLocation.c_str());
            if (zaunz == ZR_OK) {
                unzipSuccess = true;
            }
            break;
        }

        index++;
    } while (zr == ZR_MORE || zr == ZR_OK);

    CloseZip(zipFile);

    if (!unzipSuccess) throw wstring(L"Unable to extract embedded package (predicate not found).");
}

int WINAPI wWinMain(_In_ HINSTANCE hInstance, _In_opt_ HINSTANCE hPrevInstance, _In_ PWSTR pCmdLine, _In_ int nCmdShow)
{
    if (!IsWindows7SP1OrGreater()) {
        util::show_error_dialog(L"This application requires Windows 7 SP1 or later and cannot be installed on this computer.");
        return 0;
    }

    wstring myPath = util::get_current_process_path();
    wstring updaterPath = util::get_temp_file_path(L"exe");

    try {
        // locate bundled package
        BYTE* memAddr = util::mmap_read(myPath, 0);
        int64_t packageOffset, packageLength;
        bundle_marker_t::header_offset(&packageOffset, &packageLength);
        BYTE* pkgStart = memAddr + packageOffset;
        if (packageOffset == 0 || packageLength == 0) {
            util::munmap(memAddr);
            util::show_error_dialog(L"An error occurred while running setup. The embedded package was not found. Please contact the application author.");
            return 0;
        }

        // extract Squirrel installer
        std::function<bool(ZIPENTRY&)> endsWithSquirrel([](ZIPENTRY& z) {
            return hasEnding(std::wstring(z.name), L"Squirrel.exe");
        });
        unzipSingleFile(pkgStart, packageLength, updaterPath, endsWithSquirrel);
        util::munmap(memAddr);

        // run installer and forward our command line arguments
        wstring cmd = L"\"" + updaterPath + L"\" --setup \"" + myPath + L"\" " + pCmdLine;
        util::wexec(cmd.c_str());
    }
    catch (wstring wsx) {
        util::show_error_dialog(L"An error occurred while running setup. " + wsx + L". Please contact the application author.");
    }
    catch (std::exception ex) {
        // nasty shit to convert from ascii to wide-char. this will fail if there are multi-byte characters.
        // hopefully we remember to throw 'wstring' everywhere instead of 'exception' and it doesn't matter.
        string msg = ex.what();
        wstring wsTmp(msg.begin(), msg.end());
        util::show_error_dialog(L"An error occurred while running setup. " + wsTmp + L". Please contact the application author.");
    }
    catch (...) {
        util::show_error_dialog(L"An unknown error occurred while running setup. Please contact the application author.");
    }

    // clean-up after ourselves
    DeleteFile(updaterPath.c_str());
    return 0;
}