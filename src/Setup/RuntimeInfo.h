#pragma once
#include <string>
#include <vector>
#include <sstream>

typedef struct
{
    WORD minOS;
    WORD minSP;
    wchar_t name[32];
    wchar_t friendlyName[32];
    wchar_t installerUrl[256];
    DWORD fxReleaseVersion;
    wchar_t dncRuntimeVersionName[32];
} RUNTIMEINFO;

const RUNTIMEINFO* GetRuntimeByName(std::wstring name);
bool IsRuntimeSupported(const RUNTIMEINFO* runtime);
bool IsRuntimeInstalled(const RUNTIMEINFO* runtime);
std::vector<const RUNTIMEINFO*> GetRequiredRuntimes();
int VerifyRuntimeString(std::wstring runtimes);