#include "platform_util.h"
#include "miniz.h"

#include <windows.h>
#include <shlobj_core.h>
#include <tchar.h>
#include <string>
#include <functional>

using namespace std;

std::wstring toWide(std::string const& in)
{
    std::wstring out{};
    if (in.length() > 0) {
        int len = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, in.c_str(), in.size(), NULL, 0);
        if (len == 0) throw wstring(L"Invalid character sequence.");

        out.resize(len);
        MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, in.c_str(), in.size(), out.data(), out.size());
    }
    return out;
}

std::string toMultiByte(std::wstring const& in)
{
    std::string out{};
    if (in.length() > 0) {
        int len = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, in.c_str(), in.size(), 0, 0, 0, 0);
        if (len == 0) throw wstring(L"Invalid character sequence.");

        out.resize(len);
        WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, in.c_str(), in.size(), out.data(), out.size(), 0, 0);
    }
    return out;
}

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
        throw wstring(L"Process exited with error code: "
            + to_wstring((int32_t)dwExitCode)
            + L". There may be more detailed information in '%localappdata%\\SquirrelClowdTemp\\Squirrel.log'.");
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

void throwLastMzError(mz_zip_archive* archive, wstring message)
{
    int errCode = (int)mz_zip_get_last_error(archive);
    const char* errMsg = mz_error(errCode);
    if (!errMsg)
        throw wstring(L"Error Code: " + to_wstring(errCode) + L". " + message);

    string mbmsg = string(errMsg);
    wstring msg = L"Error Code: " + to_wstring(errCode) + L". " + message + L" " + toWide(mbmsg);
    throw msg;
}

void extractSingleFile(void* zipBuf, size_t cZipBuf, wstring fileLocation, std::function<bool(mz_zip_archive_file_stat&)>& predicate)
{
    mz_zip_archive zip_archive;
    memset(&zip_archive, 0, sizeof(zip_archive));

    try {
        if (!mz_zip_reader_init_mem(&zip_archive, zipBuf, cZipBuf, 0))
            throwLastMzError(&zip_archive, L"Unable to open archive.");

        int numFiles = (int)mz_zip_reader_get_num_files(&zip_archive);

        mz_zip_archive_file_stat file_stat;
        bool foundItem = false;

        for (int i = 0; i < numFiles; i++) {
            if (!mz_zip_reader_file_stat(&zip_archive, i, &file_stat)) {
                // unable to read this file
                continue;
            }

            if (file_stat.m_is_directory) {
                // ignore directories
                continue;
            }

            if (predicate(file_stat)) {
                foundItem = true;
                break;
            }
        }

        if (!foundItem)
            throw wstring(L"No matching file in archive found.");

        // TODO: maybe we should use ...extract_to_cfile to avoid this string conversion
        string mbFilePath = toMultiByte(fileLocation);
        if (!mz_zip_reader_extract_to_file(&zip_archive, file_stat.m_file_index, mbFilePath.c_str(), 0))
            throwLastMzError(&zip_archive, L"Unable to extract selected file from archive.");
    }
    catch (...) {
        mz_zip_reader_end(&zip_archive);
        throw;
    }

    mz_zip_reader_end(&zip_archive);
}

// https://stackoverflow.com/a/874160/184746
bool hasEnding(std::wstring const& fullString, std::wstring const& ending)
{
    if (fullString.length() >= ending.length()) {
        return (0 == fullString.compare(fullString.length() - ending.length(), ending.length(), ending));
    }
    return false;
}

void util::extractUpdateExe(void* zipBuf, size_t cZipBuf, wstring fileLocation)
{
    std::function<bool(mz_zip_archive_file_stat&)> endsWithSquirrel([](mz_zip_archive_file_stat& z) {
        wstring fn = toWide(string(z.m_filename));
        return hasEnding(fn, L"Squirrel.exe");
    });
    extractSingleFile(zipBuf, cZipBuf, fileLocation, endsWithSquirrel);
}

// Prints to the provided buffer a nice number of bytes (KB, MB, GB, etc)
wstring util::pretty_bytes(uint64_t bytes)
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