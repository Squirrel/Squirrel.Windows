#include "platform_util.h"
#include <windows.h>
#include <shlobj_core.h>
#include <tchar.h>
#include <string>

using namespace std;

wstring get_filename_from_path(wstring& path)
{
    auto idx = path.find_last_of('\\');

    // if we can't find last \ or the name is too short, default to 'Setup'
    if (idx == wstring::npos || path.length() < idx + 3)
        return L"Setup";

    return path.substr(idx + 1);
}

void throwWin32Error(HRESULT hr, wstring addedInfo)
{
    if (hr == 0) {
        return;
    }

    // https://stackoverflow.com/a/17387176/184746
    // https://stackoverflow.com/a/455533/184746
    LPWSTR messageBuffer = nullptr;
    size_t size = FormatMessage(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
                                NULL, hr, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (LPWSTR)&messageBuffer, 0, NULL);

    wstring message(messageBuffer, size);

    if (messageBuffer) {
        LocalFree(messageBuffer);
        messageBuffer = nullptr;
    }

    if (addedInfo.empty()) {
        throw message;
    }
    else {
        throw wstring(addedInfo + L" \n" + message);
    }
}

void throwLastWin32Error(wstring addedInfo)
{
    throwWin32Error(::GetLastError(), addedInfo);
}

std::wstring util::get_temp_file_path(wstring extension)
{
    wchar_t tempFolderBuf[MAX_PATH];
    DWORD cTempFolder = GetTempPath(MAX_PATH, tempFolderBuf);
    wchar_t tempFileBuf[MAX_PATH];
    GetTempFileName(tempFolderBuf, L"squirrel", 0, tempFileBuf);
    DeleteFile(tempFileBuf);
    wstring tempFile(tempFileBuf);

    if (!extension.empty())
        tempFile += L"." + extension;

    return tempFile;
}

bool util::check_diskspace(uint64_t requiredSpace)
{
    TCHAR szPath[MAX_PATH];
    auto hr = SHGetFolderPath(NULL, CSIDL_LOCAL_APPDATA, NULL, 0, szPath);
    if (FAILED(hr))
        throwWin32Error(hr, L"Unable to locate %localappdata%.");

    ULARGE_INTEGER freeSpace;
    if (!GetDiskFreeSpaceEx(szPath, 0, 0, &freeSpace))
        throwLastWin32Error(L"Unable to verify sufficient available free space on disk.");

    return freeSpace.QuadPart > requiredSpace;
}

std::wstring util::get_current_process_path()
{
    wchar_t ourFile[MAX_PATH];
    HMODULE hMod = GetModuleHandle(NULL);
    GetModuleFileName(hMod, ourFile, _countof(ourFile));
    return wstring(ourFile);
}

void util::wexec(const wchar_t* cmd)
{
    LPTSTR szCmdline = _tcsdup(cmd); // https://stackoverflow.com/a/10044348/184746

    STARTUPINFO si = { 0 };
    si.cb = sizeof(STARTUPINFO);
    si.wShowWindow = SW_SHOW;
    si.dwFlags = STARTF_USESHOWWINDOW;

    PROCESS_INFORMATION pi = { 0 };
    if (!CreateProcess(NULL, szCmdline, NULL, NULL, false, 0, NULL, NULL, &si, &pi)) {
        throwLastWin32Error(L"Unable to start install process.");
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

void util::show_error_dialog(std::wstring msg)
{
    wstring myPath = get_current_process_path();
    wstring myName = get_filename_from_path(myPath);
    wstring errorTitle = myName + L" Error";
    MessageBox(0, msg.c_str(), errorTitle.c_str(), MB_OK | MB_ICONERROR);
}

// https://github.com/dotnet/runtime/blob/26c9b2883e0b6daaa98304fdc2912abec25dc216/src/native/corehost/hostmisc/pal.windows.cpp#L68
void* map_file_impl(const wstring& path, size_t* length, DWORD mapping_protect, DWORD view_desired_access)
{
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);

    if (file == INVALID_HANDLE_VALUE) {
        throwLastWin32Error(L"Failed to map file. CreateFileW() failed with error.");
    }

    if (length != nullptr) {
        LARGE_INTEGER fileSize;
        if (GetFileSizeEx(file, &fileSize) == 0) {
            CloseHandle(file);
            throwLastWin32Error(L"Failed to map file. GetFileSizeEx() failed with error.");
        }
        *length = (size_t)fileSize.QuadPart;
    }

    HANDLE map = CreateFileMappingW(file, NULL, mapping_protect, 0, 0, NULL);

    if (map == NULL) {
        CloseHandle(file);
        throwLastWin32Error(L"Failed to map file. CreateFileMappingW() failed with error.");
    }

    void* address = MapViewOfFile(map, view_desired_access, 0, 0, 0);

    // The file-handle (file) and mapping object handle (map) can be safely closed
    // once the file is mapped. The OS keeps the file open if there is an open mapping into the file.
    CloseHandle(map);
    CloseHandle(file);

    if (address == NULL) {
        throwLastWin32Error(L"Failed to map file. MapViewOfFile() failed with error.");
    }

    return address;
}

uint8_t* util::mmap_read(const std::wstring& filePath, size_t* length)
{
    return (uint8_t*)map_file_impl(filePath, length, PAGE_READONLY, FILE_MAP_READ);
}

bool util::munmap(uint8_t* addr)
{
    return UnmapViewOfFile(addr) != 0;
}
