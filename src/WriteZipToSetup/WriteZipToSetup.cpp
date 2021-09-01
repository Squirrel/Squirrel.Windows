// WriteZipToSetup.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"
#include "flags.h"

#define IDR_UPDATE_ZIP                  131
#define IDR_FX_VERSION_FLAG             132
#define IDR_SPLASH_IMG                  138
#define RESOURCE_LANG                   0x0409

using namespace std;

BOOL CALLBACK EnumResLangProc(HMODULE hModule, LPCTSTR lpszType, LPCTSTR lpszName, WORD wIDLanguage, LONG_PTR lParam)
{
    HANDLE hUpdate = (HANDLE)lParam;
    HRSRC hFindItAgain = FindResourceEx(hModule, lpszType, lpszName, wIDLanguage);

    HGLOBAL hGlobal = LoadResource(hModule, hFindItAgain);
    if (!hGlobal) return true;

    UpdateResource(hUpdate, lpszType, lpszName, wIDLanguage, LockResource(hGlobal), SizeofResource(hModule, hFindItAgain));
    return true;
}

BOOL CALLBACK EnumResNameProc(HMODULE hModule, LPCTSTR lpszType, LPTSTR lpszName, LONG_PTR lParam)
{
    HANDLE hUpdate = (HANDLE)lParam;

    EnumResourceLanguages(hModule, lpszType, lpszName, EnumResLangProc, (LONG_PTR)hUpdate);
    return true;
}

BOOL CALLBACK EnumResTypeProc(HMODULE hMod, LPTSTR lpszType, LONG_PTR lParam)
{
    std::vector<wchar_t*>* typeList = (std::vector<wchar_t*>*)lParam;
    if (IS_INTRESOURCE(lpszType)) {
        typeList->push_back(lpszType);
    }
    else {
        typeList->push_back(_wcsdup(lpszType));
    }

    return true;
}

int CopyResourcesToStubExecutable(wchar_t* src, wchar_t* dest)
{
    HMODULE hSrc = LoadLibraryEx(src, NULL, LOAD_LIBRARY_AS_DATAFILE);
    if (!hSrc) return GetLastError();

    HANDLE hUpdate = BeginUpdateResource(dest, true);
    if (!hUpdate) return GetLastError();

    std::vector<wchar_t*> typeList;
    EnumResourceTypes(hSrc, EnumResTypeProc, (LONG_PTR)&typeList);

    for (auto& type : typeList) {
        EnumResourceNames(hSrc, type, EnumResNameProc, (LONG_PTR)hUpdate);
    }

    EndUpdateResource(hUpdate, false);
    return 0;
}

int LoadFileIntoMemory(wstring fpath, BYTE** pBuf, int* cBuf)
{
    HANDLE hFile = CreateFile(fpath.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    if (hFile == INVALID_HANDLE_VALUE) {
        printf("Can't open file\n");
        return E_FAIL;
    }

    BY_HANDLE_FILE_INFORMATION fileInfo;
    if (!GetFileInformationByHandle(hFile, &fileInfo)) {
        printf("Can't read file handle\n");
        return E_FAIL;
    }

    *cBuf = fileInfo.nFileSizeLow;
    *pBuf = new BYTE[fileInfo.nFileSizeLow + 0x1000];

    BYTE* pCurrent = *pBuf;
    DWORD dwBytesRead;

    printf("Starting to read file!\n");
    do {
        if (!ReadFile(hFile, pCurrent, 0x1000, &dwBytesRead, NULL)) {
            printf("Failed to read file! 0x%u\n", GetLastError());
            return E_FAIL;
        }

        pCurrent += dwBytesRead;
    } while (dwBytesRead > 0);

    return S_OK;
}

int fail()
{
    printf("Usage: WriteZipToSetup [Setup.exe template] [Zip File]\n");
    return -1;
}

int wmain(int argc, wchar_t* argv[])
{
    // short circuit exit for stub executable
    if (argc > 1 && wcscmp(argv[1], L"--copy-stub-resources") == 0) {
        if (argc != 4) return fail();
        return CopyResourcesToStubExecutable(argv[2], argv[3]);
    }

    // parse command line arguments
    const flags::args args(argc, argv);
    const auto& parg = args.positional();
    if (parg.size() != 2) {
        return fail();
    }
    const auto setupFile = wstring(parg[0]);
    const auto zipFile = wstring(parg[1]);
    const auto requiredFramework = args.get<wstring>(L"set-required-framework");
    const auto splashImage = args.get<wstring>(L"set-splash");

    wprintf(L"Setup: %s, Zip: %s\n", setupFile.c_str(), zipFile.c_str());

    // Read the entire zip file into memory, yolo
    BYTE* pZipBuf, * pSplashBuf;
    int cZipBuf, cSplashBuf;

    if (FAILED(LoadFileIntoMemory(zipFile, &pZipBuf, &cZipBuf))) {
        printf("Couldn't read zip file.\n");
        return fail();
    }

    printf("Updating Resource!\n");
    HANDLE hRes = BeginUpdateResource(setupFile.c_str(), false);
    if (!hRes) {
        printf("Couldn't open setup.exe for writing\n");
        return fail();
    }

    if (!UpdateResource(hRes, L"DATA", MAKEINTRESOURCE(IDR_UPDATE_ZIP), RESOURCE_LANG, pZipBuf, cZipBuf)) {
        printf("Failed to update zip resource\n");
        return fail();
    }

    if (requiredFramework.has_value()) {
        wstring sReq = requiredFramework.value();
        LPVOID pReq = &sReq[0];
        if (!UpdateResource(hRes, L"FLAGS", MAKEINTRESOURCE(IDR_FX_VERSION_FLAG), RESOURCE_LANG, pReq, (sReq.length() + 1) * sizeof(wchar_t))) {
            printf("Failed to update required version resource\n");
            return fail();
        }
    }

    if (splashImage.has_value()) {
        if (FAILED(LoadFileIntoMemory(splashImage.value(), &pSplashBuf, &cSplashBuf))) {
            printf("Couldn't read splash image.\n");
            return fail();
        }

        if (!UpdateResource(hRes, L"DATA", MAKEINTRESOURCE(IDR_SPLASH_IMG), RESOURCE_LANG, pSplashBuf, cSplashBuf)) {
            printf("Failed to update splash resource\n");
            return fail();
        }
    }
    else {
        // if the user hasn't given us a splash screen, let's remove the default (there will be no splash at all)
        if (!UpdateResource(hRes, L"DATA", MAKEINTRESOURCE(IDR_SPLASH_IMG), RESOURCE_LANG, 0, 0)) {
            printf("Failed to update splash resource\n");
            return fail();
        }
    }

    printf("Finished!\n");
    if (!EndUpdateResource(hRes, false)) {
        printf("Failed to update resource\n");
        return fail();
    }

    printf("It worked!\n");
    return 0;
}
