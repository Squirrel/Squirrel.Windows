#include "stdafx.h"
#include "RuntimeInfo.h"
#include "Resource.h"

using std::wstring;
using std::string;
using std::vector;

RUNTIMEINFO supported_runtimes[] =
{

    // net45 through net46 is supported on Vista SP2 and newer

    {
        _WIN32_WINNT_VISTA, 2,
        L"net45", L".NET Framework 4.5",
        L"http://go.microsoft.com/fwlink/?LinkId=397707",
        378389
    },

    {
        _WIN32_WINNT_VISTA, 2,
        L"net451", L".NET Framework 4.5.1",
        L"http://go.microsoft.com/fwlink/?LinkId=397707",
        378675
    },

    {
        _WIN32_WINNT_VISTA, 2,
        L"net452", L".NET Framework 4.5.2",
        L"http://go.microsoft.com/fwlink/?LinkId=397707",
        379893
    },

    // net461 through net48 supports Windows 7 and newer

    {
        _WIN32_WINNT_WIN7, 0,
        L"net46", L".NET Framework 4.6",
        L"http://go.microsoft.com/fwlink/?LinkId=780596",
        393295
    },

    {
        _WIN32_WINNT_WIN7, 0,
        L"net461", L".NET Framework 4.6.1",
        L"http://go.microsoft.com/fwlink/?LinkId=780596",
        394254
    },

    {
        _WIN32_WINNT_WIN7, 0,
        L"net462", L".NET Framework 4.6.2",
        L"http://go.microsoft.com/fwlink/?LinkId=780596",
        394802
    },

    {
        _WIN32_WINNT_WIN7, 0,
        L"net47", L".NET Framework 4.7",
        L"http://go.microsoft.com/fwlink/?LinkId=863262",
        460798
    },

    {
        _WIN32_WINNT_WIN7, 0,
        L"net471", L".NET Framework 4.7.1",
        L"http://go.microsoft.com/fwlink/?LinkId=863262",
        461308
    },

    {
        _WIN32_WINNT_WIN7, 0,
        L"net472", L".NET Framework 4.7.2",
        L"http://go.microsoft.com/fwlink/?LinkId=863262",
        461808
    },

    {
        _WIN32_WINNT_WIN7, 0,
        L"net48", L".NET Framework 4.8",
        L"https://go.microsoft.com/fwlink/?LinkId=2085155",
        528040
    },

    // dotnet core is supported on Windows 7 SP1 and newer.
    // update this list periodically from https://dotnet.microsoft.com/download/dotnet
    // we could add support for 2.0/2.1/2.2 but since those runtimes didn't ship with desktop support it is probably not needed.

    {
        _WIN32_WINNT_WIN7, 1,
        L"netcoreapp3", L".NET Core 3.0.3",
        L"https://download.visualstudio.microsoft.com/download/pr/c525a2bb-6e98-4e6e-849e-45241d0db71c/d21612f02b9cae52fa50eb54de905986/windowsdesktop-runtime-3.0.3-win-x64.exe",
        0, L"WindowsDesktop.App 3.0"
    },

    {
        _WIN32_WINNT_WIN7, 1,
        L"netcoreapp31", L".NET Core 3.1.21",
        L"https://download.visualstudio.microsoft.com/download/pr/3f56df9d-6dc0-4897-a49b-ea891f9ad0f4/076e353a29908c70e24ba8b8d0daefb8/windowsdesktop-runtime-3.1.21-win-x64.exe",
        0, L"WindowsDesktop.App 3.1"
    },

    {
        _WIN32_WINNT_WIN7, 1,
        L"net5", L".NET 5.0.12",
        L"https://download.visualstudio.microsoft.com/download/pr/1daf85dc-291b-4bb8-812e-a0df5cdb6701/85455a4a851347de26e2901e043b81e1/windowsdesktop-runtime-5.0.12-win-x64.exe",
        0, L"WindowsDesktop.App 5.0"
    },

    {
        _WIN32_WINNT_WIN7, 1,
        L"net6", L".NET 6.0.0",
        L"https://download.visualstudio.microsoft.com/download/pr/a865ccae-2219-4184-bcd6-0178dc580589/ba452d37e8396b7a49a9adc0e1a07e87/windowsdesktop-runtime-6.0.0-win-x64.exe",
        0, L"WindowsDesktop.App 6.0"
    },

};

#define NUM_RUNTIMES (sizeof(supported_runtimes) / sizeof(RUNTIMEINFO))

const RUNTIMEINFO* GetRuntimeByName(wstring name)
{
    for (int i = 0; i < NUM_RUNTIMES; i++) {
        const RUNTIMEINFO* item = &supported_runtimes[i];
        auto itemName = wstring(item->name);

        if (name == itemName)
            return item;
    }

    return nullptr;
}

bool IsRuntimeSupported(const RUNTIMEINFO* runtime)
{
    return IsWindowsVersionOrGreater(HIBYTE(runtime->minOS), LOBYTE(runtime->minOS), runtime->minSP);
}

static const wchar_t* ndpPath = L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full";
bool IsFullNetFrameworkInstalled(DWORD requiredVersion)
{
    ATL::CRegKey key;

    if (key.Open(HKEY_LOCAL_MACHINE, ndpPath, KEY_READ) != ERROR_SUCCESS) {
        return false;
    }

    DWORD dwReleaseInfo = 0;
    if (key.QueryDWORDValue(L"Release", dwReleaseInfo) != ERROR_SUCCESS ||
        dwReleaseInfo < requiredVersion) {
        return false;
    }

    return true;
}

// TODO this is extremely messy, it should certainly be re-written.
wstring exec(const char* cmd)
{
    char buffer[128];
    string result = "";
    FILE* pipe = _popen(cmd, "r");
    if (!pipe)
        return L"";
    try {
        while (fgets(buffer, sizeof buffer, pipe) != NULL) {
            result += buffer;
        }
    }
    catch (...) {
        _pclose(pipe);
        return L"";
    }
    _pclose(pipe);

    // https://stackoverflow.com/a/8969776/184746
    std::wstring wsTmp(result.begin(), result.end());
    return wsTmp;
}

bool IsDotNetCoreInstalled(wstring searchString)
{
    // it is possible to parse this registry entry, but it only returns the newest version
    // and it might be necessary to install an older version of the runtime if it's not installed,
    // so we need a full list of installed runtimes. 
    // static const wchar_t* dncPath = L"SOFTWARE\\dotnet\\Setup\\InstalledVersions";

    // note, dotnet cli will only return x64 results.
    //auto runtimes = exec("dotnet --list-runtimes");
    auto runtimes = exec("dotnet --info");
    return runtimes.find(searchString) != std::wstring::npos;
}

bool IsRuntimeInstalled(const RUNTIMEINFO* runtime)
{
    if (runtime->fxReleaseVersion > 0) {
        return IsFullNetFrameworkInstalled(runtime->fxReleaseVersion);
    }
    else {
        return IsDotNetCoreInstalled(wstring(runtime->dncRuntimeVersionName));
    }
}

int ParseRuntimeString(std::wstring version, vector<const RUNTIMEINFO*>& runtimes)
{
    // split version string by comma
    int ret = S_OK;
    wstring temp;
    std::wstringstream wss(version);
    while (std::getline(wss, temp, L',')) {
        const RUNTIMEINFO* rt = GetRuntimeByName(temp);
        if (rt != nullptr)
            runtimes.push_back(rt);
        else
            ret = S_FALSE;
    }
    return ret;
}

vector<const RUNTIMEINFO*> GetRequiredRuntimes()
{
    vector<const RUNTIMEINFO*> runtimes;

    // get comma-delimited version string from exe resources
    wchar_t* versionFlag = (wchar_t*)LoadResource(NULL, FindResource(NULL, (LPCWSTR)IDR_FX_VERSION_FLAG, L"FLAGS"));
    if (versionFlag == nullptr)
        return runtimes;

    wstring version(versionFlag);
    if (version.length() == 0)
        return runtimes;

    ParseRuntimeString(version, runtimes);
    return runtimes;
}

int VerifyRuntimeString(std::wstring version)
{
    vector<const RUNTIMEINFO*> runtimes;
    return ParseRuntimeString(version, runtimes);
}

