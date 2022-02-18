#include <windows.h>
#include <versionhelpers.h>
#include <string>
#include <functional>
#include <tchar.h>
#include "unzip.h"

using namespace std;

wstring getTempExePath()
{
    wchar_t tempFolderBuf[MAX_PATH];
    DWORD cTempFolder = GetTempPath(MAX_PATH, tempFolderBuf);
    wchar_t tempFileBuf[MAX_PATH];
    GetTempFileName(tempFolderBuf, L"squirrel", 0, tempFileBuf);
    DeleteFile(tempFileBuf);
    wstring tempFile(tempFileBuf);
    tempFile += L".exe";
    return tempFile;
}

BYTE* getByteResource(int idx, DWORD* cBuf)
{
    auto f = FindResource(NULL, MAKEINTRESOURCE(idx), L"DATA");
    if (!f) throw wstring(L"Unable to find resource " + to_wstring(idx));

    auto r = LoadResource(NULL, f);
    if (!r) throw wstring(L"Unable to load resource " + to_wstring(idx));

    *cBuf = SizeofResource(NULL, f);
    return (BYTE*)LockResource(r);
}

wstring getCurrentExecutablePath()
{
    wchar_t ourFile[MAX_PATH];
    HMODULE hMod = GetModuleHandle(NULL);
    GetModuleFileName(hMod, ourFile, _countof(ourFile));
    return wstring(ourFile);
}

wstring getNameFromPath(wstring path)
{
    auto idx = path.find_last_of('\\');

    // if we can't find last \ or the name is too short, default to 'Setup'
    if (idx == wstring::npos || path.length() < idx + 3)
        return L"Setup";

    return path.substr(idx + 1);
}

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

// https://stackoverflow.com/a/17387176/184746
void throwLastWin32Error()
{
    DWORD errorMessageID = ::GetLastError();
    if (errorMessageID == 0) {
        return;
    }

    LPWSTR messageBuffer = nullptr;
    size_t size = FormatMessage(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
                                NULL, errorMessageID, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (LPWSTR)&messageBuffer, 0, NULL);

    std::wstring message(messageBuffer, size);
    throw message;
}

void wexec(const wchar_t* cmd)
{
    LPTSTR szCmdline = _tcsdup(cmd); // https://stackoverflow.com/a/10044348/184746

    STARTUPINFO si = { 0 };
    si.cb = sizeof(STARTUPINFO);
    si.wShowWindow = SW_SHOW;
    si.dwFlags = STARTF_USESHOWWINDOW;

    PROCESS_INFORMATION pi = { 0 };
    if (!CreateProcess(NULL, szCmdline, NULL, NULL, false, 0, NULL, NULL, &si, &pi)) {
        throwLastWin32Error();
    }

    WaitForSingleObject(pi.hProcess, INFINITE);

    DWORD dwExitCode = 0;
    if (!GetExitCodeProcess(pi.hProcess, &dwExitCode)) {
        dwExitCode = (DWORD)-9;
    }

    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);

    if (dwExitCode != 0) {
        throw wstring(L"Process exited with error code: " + to_wstring(dwExitCode));
    }
}

int showErrorDialog(wstring msg)
{
    wstring myPath = getCurrentExecutablePath();
    wstring myName = getNameFromPath(myPath);
    wstring errorTitle = myName + L" Error";
    MessageBox(0, msg.c_str(), errorTitle.c_str(), MB_OK | MB_ICONERROR);
    return -1;
}

int WINAPI wWinMain(_In_ HINSTANCE hInstance, _In_opt_ HINSTANCE hPrevInstance, _In_ PWSTR pCmdLine, _In_ int nCmdShow)
{
    if (!IsWindows7SP1OrGreater()) {
        return showErrorDialog(L"This application requires Windows 7 SP1 or later and cannot be installed on this computer.");
    }

    wstring myPath = getCurrentExecutablePath();
    wstring updaterPath = getTempExePath();

    try {
        // locate bundled package
        DWORD cZipBuf;
        BYTE* zipBuf = getByteResource(205, &cZipBuf); // 205 = BundledPackageBytes

        // extract Squirrel installer
        std::function<bool(ZIPENTRY&)> endsWithSquirrel([](ZIPENTRY& z) {
            return hasEnding(std::wstring(z.name), L"Squirrel.exe");
        });
        unzipSingleFile(zipBuf, cZipBuf, updaterPath, endsWithSquirrel);

        // run installer and forward our command line arguments
        wstring cmd = L"\"" + updaterPath + L"\" --setup \"" + myPath + L"\" " + pCmdLine;
        wexec(cmd.c_str());
    }
    catch (wstring wsx) {
        return showErrorDialog(L"An error occurred while running setup: " + wsx + L". Please contact the application author.");
    }
    catch (std::exception ex) {
        // nasty shit to convert from ascii to wide-char. this will fail if there are multi-byte characters.
        // hopefully we remember to throw 'wstring' everywhere instead of 'exception' and it doesn't matter.
        string msg = ex.what();
        wstring wsTmp(msg.begin(), msg.end());
        return showErrorDialog(L"An error occurred while running setup: " + wsTmp + L". Please contact the application author.");
    }
    catch (...) {
        return showErrorDialog(L"An unknown error occurred while running setup. Please contact the application author.");
    }

    // clean-up after ourselves
    DeleteFile(updaterPath.c_str());
    return 0;
}