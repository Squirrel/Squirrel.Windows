#include "stdafx.h"
#include "unzip.h"
#include "MachineInstaller.h"
#include "resource.h"
#include <sddl.h>

bool directoryExists(wchar_t* path) {
	DWORD dwResult = GetFileAttributes(path);

	if (dwResult != INVALID_FILE_ATTRIBUTES) {
		return true;
	}

	// NB: The directory could exist but we can't access it, let's check
	DWORD dwLastError = GetLastError();
	if (dwLastError == ERROR_FILE_NOT_FOUND) return false;
	if (dwLastError == ERROR_PATH_NOT_FOUND) return false;

	return true;
}

bool MachineInstaller::ShouldSilentInstall()
{
	// Figure out the package name from our own EXE name 
	// The name consist of [$pkgName]DeploymentTool.exe
	wchar_t ourFile[MAX_PATH];
	HMODULE hMod = GetModuleHandle(NULL);
	GetModuleFileName(hMod, ourFile, _countof(ourFile));

	CString fullPath = CString(ourFile);
	CString pkgName = CString(ourFile + fullPath.ReverseFind(L'\\'));
	pkgName.Replace(L"DeploymentTool.exe", L"");
	
	wchar_t installFolder[MAX_PATH];

	// NB: Users often get into the sitch where they install the MSI, then try to
	// install the standalone package on top of that. In previous versions we tried
	// to detect if the app was properly installed, but now we're taking the much 
	// more conservative approach, that if the package dir exists in any way, we're
	// bailing out

	// C:\Users\Username\AppData\Local\$pkgName
	SHGetFolderPath(NULL, CSIDL_LOCAL_APPDATA, NULL, SHGFP_TYPE_CURRENT, installFolder);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, pkgName);

	if (directoryExists(installFolder)) {
		return false;
	}

	// C:\ProgramData\$pkgName\$username
	wchar_t username[512];
	DWORD unamesize = _countof(username);
	SHGetFolderPath(NULL, CSIDL_COMMON_APPDATA, NULL, SHGFP_TYPE_CURRENT, installFolder);
	GetUserName(username, &unamesize);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, pkgName);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, username);

	if (directoryExists(installFolder)) {
		return false;
	}

	// None of these exist, we should install
	return true;
}
