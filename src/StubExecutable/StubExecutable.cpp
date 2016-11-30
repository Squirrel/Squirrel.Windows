// StubExecutable.cpp : Defines the entry point for the application.
//

#include "stdafx.h"
#include "StubExecutable.h"

#include "semver200.h"

using namespace std;

wchar_t* FindRootAppDir() 
{
	wchar_t* ourDirectory = new wchar_t[MAX_PATH];

	GetModuleFileName(GetModuleHandle(NULL), ourDirectory, MAX_PATH);
	wchar_t* lastSlash = wcsrchr(ourDirectory, L'\\');
	if (!lastSlash) {
		delete[] ourDirectory;
		return NULL;
	}

	// Null-terminate the string at the slash so now it's a directory
	*lastSlash = 0x0;
	return ourDirectory;
}

wchar_t* FindOwnExecutableName() 
{
	wchar_t* ourDirectory = new wchar_t[MAX_PATH];

	GetModuleFileName(GetModuleHandle(NULL), ourDirectory, MAX_PATH);
	wchar_t* lastSlash = wcsrchr(ourDirectory, L'\\');
	if (!lastSlash) {
		delete[] ourDirectory;
		return NULL;
	}

	wchar_t* ret = _wcsdup(lastSlash + 1);
	delete[] ourDirectory;
	return ret;
}

std::wstring FindLatestAppDir() 
{
	std::wstring ourDir;
	ourDir.assign(FindRootAppDir());

	ourDir += L"\\app-*";

	WIN32_FIND_DATA fileInfo = { 0 };
	HANDLE hFile = FindFirstFile(ourDir.c_str(), &fileInfo);
	if (hFile == INVALID_HANDLE_VALUE) {
		return NULL;
	}

	version::Semver200_version acc("0.0.0");
	std::wstring acc_s;

	do {
		std::wstring appVer = fileInfo.cFileName;
		appVer = appVer.substr(4);   // Skip 'app-'
		if (!(fileInfo.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) {
			continue;
		}

		std::string s(appVer.begin(), appVer.end());

		version::Semver200_version thisVer(s);

		if (thisVer > acc) {
			acc = thisVer;
			acc_s = appVer;
		}
	} while (FindNextFile(hFile, &fileInfo));

	if (acc == version::Semver200_version("0.0.0")) {
		return NULL;
	}

	ourDir.assign(FindRootAppDir());
	std::wstringstream ret;
	ret << ourDir << L"\\app-" << acc_s;

	FindClose(hFile);
	return ret.str();
}

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
                     _In_opt_ HINSTANCE hPrevInstance,
                     _In_ LPWSTR    lpCmdLine,
                     _In_ int       nCmdShow)
{
	std::wstring appName;
	appName.assign(FindOwnExecutableName());

	std::wstring fullPath = FindLatestAppDir();
	fullPath += L"\\" + appName;

	STARTUPINFO si = { 0 };
	PROCESS_INFORMATION pi = { 0 };

	si.cb = sizeof(si);
	si.dwFlags = STARTF_USESHOWWINDOW;
	si.wShowWindow = nCmdShow;

	if (!CreateProcess(fullPath.c_str(), lpCmdLine, NULL, NULL, true, 0, NULL, NULL, &si, &pi)) {
		return -1;
	}

	AllowSetForegroundWindow(pi.dwProcessId);
	WaitForInputIdle(pi.hProcess, 5 * 1000);
	return 0;
}
