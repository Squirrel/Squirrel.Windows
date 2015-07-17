#include "stdafx.h"
#include "unzip.h"
#include "MachineInstaller.h"
#include "resource.h"
#include <sddl.h>

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

			int idx = wcscspn(zentry.name, L"-");
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

	// NB: This is the DACL for Program Files
	wchar_t sddl[512] = L"D:PAI(A;;FA;;;S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464)(A;CIIO;GA;;;S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464)(A;;0x1301bf;;;SY)(A;OICIIO;GA;;;SY)(A;;0x1301bf;;;BA)(A;OICIIO;GA;;;BA)(A;;0x1200a9;;;BU)(A;OICIIO;GXGR;;;BU)(A;OICIIO;GA;;;CO)";

	if (IsWindows8OrGreater()) {
		// Add ALL APPLICATION PACKAGES account (Only available on Windows 8 and greater)
		wcscat(sddl, L"(A;;0x1200a9;;;AC)(A;OICIIO;GXGR;;;AC)");
	}

	PSECURITY_DESCRIPTOR descriptor;
	ConvertStringSecurityDescriptorToSecurityDescriptor(
		sddl,
		SDDL_REVISION_1,
		&descriptor, NULL);

	SECURITY_ATTRIBUTES attrs;
	attrs.nLength = sizeof(SECURITY_ATTRIBUTES);
	attrs.bInheritHandle = false;
	attrs.lpSecurityDescriptor = descriptor;

	if (!CreateDirectory(machineInstallFolder, &attrs) && GetLastError() != ERROR_ALREADY_EXISTS) {
		LocalFree(descriptor);
		return GetLastError();
	}

	LocalFree(descriptor);

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

	// C:\Users\Username\AppData\Local\$pkgName\packages
	SHGetFolderPath(NULL, CSIDL_LOCAL_APPDATA, NULL, SHGFP_TYPE_CURRENT, installFolder);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, pkgName);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, L"packages");

	if (GetFileAttributes(installFolder) != INVALID_FILE_ATTRIBUTES) {
		return false;
	}

	// C:\Users\Username\AppData\Local\$pkgName\.dead (was machine-installed but user uninstalled)
	SHGetFolderPath(NULL, CSIDL_LOCAL_APPDATA, NULL, SHGFP_TYPE_CURRENT, installFolder);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, pkgName);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, L".dead");

	if (GetFileAttributes(installFolder) != INVALID_FILE_ATTRIBUTES) {
		return false;
	}

	// C:\ProgramData\$pkgName\$username\packages
	wchar_t username[512];
	DWORD unamesize = _countof(username);
	SHGetFolderPath(NULL, CSIDL_COMMON_APPDATA, NULL, SHGFP_TYPE_CURRENT, installFolder);
	GetUserName(username, &unamesize);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, pkgName);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, username);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, L"packages");

	if (GetFileAttributes(installFolder) != INVALID_FILE_ATTRIBUTES) {
		return false;
	}

	// C:\ProgramData\$pkgName\$username\.dead
	SHGetFolderPath(NULL, CSIDL_COMMON_APPDATA, NULL, SHGFP_TYPE_CURRENT, installFolder);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, pkgName);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, username);
	wcscat(installFolder, L"\\");
	wcscat(installFolder, L".dead");

	if (GetFileAttributes(installFolder) != INVALID_FILE_ATTRIBUTES) {
		return false;
	}

	// None of these exist, we should install
	return true;
}
