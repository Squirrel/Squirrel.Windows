#define _CRT_SECURE_NO_WARNINGS 1

#include "stdafx.h"
#include "unzip.h"
#include "MachineInstaller.h"
#include "resource.h"

bool findPackageFromEmbeddedZip(wchar_t* buf, DWORD cbSize) 
{
	bool ret = false;

	CResource zipResource;
	if (!zipResource.Load(L"DATA", IDR_UPDATE_ZIP)) {
		return false;
	}

	DWORD dwSize = zipResource.GetSize();
	if (dwSize < 0x100) {
		return false;
	}

	BYTE* pData = (BYTE*)zipResource.Lock();
	HZIP zipFile = OpenZip(pData, dwSize, NULL);

	ZRESULT zr;
	int index = 0;
	do {
		ZIPENTRY zentry;

		zr = GetZipItem(zipFile, index, &zentry);
		if (zr != ZR_OK && zr != ZR_MORE) {
			break;
		}

		if (wcsstr(zentry.name, L"nupkg")) {
			ZeroMemory(buf, cbSize);

			int idx = wcscspn(zentry.name, L"nupkg");
			memcpy(buf, zentry.name, sizeof(wchar_t) * idx);
			ret = true;
			break;
		}

		index++;
	} while (zr == ZR_MORE || zr == ZR_OK);

	CloseZip(zipFile);
	zipResource.Release();

	return ret;
}

bool createAdminOnlySecurityAttributes(LPSECURITY_ATTRIBUTES pAttributes)
{
	return false;
}

int MachineInstaller::PerformMachineInstallSetup()
{
	wchar_t packageName[512];

	if (!findPackageFromEmbeddedZip(packageName, sizeof(packageName))) {
		MessageBox(NULL, L"Corrupt installer", L"Cannot find package name for installer, is it created correctly?", MB_OK);
		return ERROR_INVALID_PARAMETER;
	}

	wchar_t machineInstallFolder[MAX_PATH];
	SHGetFolderPath(NULL, CSIDL_COMMON_APPDATA, NULL, SHGFP_TYPE_CURRENT, machineInstallFolder);
	wcscat(machineInstallFolder, L"\\SquirrelMachineInstalls");

	SECURITY_ATTRIBUTES secattrs;
	createAdminOnlySecurityAttributes(&secattrs);

	if (!CreateDirectory(machineInstallFolder, NULL/*&secattrs*/) && GetLastError() != ERROR_ALREADY_EXISTS) {
		return GetLastError();
	}

	wcscat(machineInstallFolder, L"\\");
	wcscat(machineInstallFolder, packageName);
	wcscat(machineInstallFolder, L".exe");

	wchar_t ourFile[MAX_PATH];
	HMODULE hMod = GetModuleHandle(NULL);
	GetModuleFileName(hMod, ourFile, _countof(ourFile));

	if (!CopyFile(ourFile, machineInstallFolder, false)) {
		return GetLastError();
	}

	HKEY runKey;
	DWORD dontcare;
	if (RegCreateKeyEx(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", 0, NULL, 0, KEY_ALL_ACCESS, NULL, &runKey, &dontcare) != ERROR_SUCCESS) {
		return GetLastError();
	}

	wcscat_s(machineInstallFolder, L" --checkInstall");

	if (RegSetValueEx(runKey, packageName, 0, REG_SZ, (BYTE*)machineInstallFolder, (wcsnlen(machineInstallFolder, sizeof(machineInstallFolder)) + 1) * sizeof(wchar_t)) != ERROR_SUCCESS) {
		return GetLastError();
	}

	RegCloseKey(runKey);
	return 0;
}


bool MachineInstaller::ShouldSilentInstall()
{
	// Figure out the package name from our own EXE name 
	wchar_t ourFile[MAX_PATH];
	HMODULE hMod = GetModuleHandle(NULL);
	GetModuleFileName(hMod, ourFile, _countof(ourFile));

	CString fullPath = CString(ourFile);
	CString pkgName = CString(ourFile + fullPath.ReverseFind(L'\\'));
	pkgName.Replace(L".exe", L"");
	
	wchar_t installFolder[MAX_PATH];

	// C:\Users\Username\AppData\Local\$pkgName
	SHGetFolderPath(NULL, CSIDL_LOCAL_APPDATA, NULL, SHGFP_TYPE_CURRENT, installFolder);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, pkgName);

	if (GetFileAttributes(installFolder) != INVALID_FILE_ATTRIBUTES) return false;

	// C:\ProgramData\$pkgName\$username
	wchar_t username[512];
	DWORD dontcare;
	SHGetFolderPath(NULL, CSIDL_COMMON_APPDATA, NULL, SHGFP_TYPE_CURRENT, installFolder);
	GetUserName(username, &dontcare);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, pkgName);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, username);

	if (GetFileAttributes(installFolder) != INVALID_FILE_ATTRIBUTES) return false;

	// Neither exist, create them
	return true;
}
