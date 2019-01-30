// Setup.cpp : Defines the entry point for the application.
//

#include "stdafx.h"
#include "Setup.h"
#include "FxHelper.h"
#include "UpdateRunner.h"
#include "MachineInstaller.h"
#include <cstdio>
#include <string>

CAppModule* _Module;

typedef BOOL(WINAPI *SetDefaultDllDirectoriesFunction)(DWORD DirectoryFlags);

// Some libraries are still loaded from the current directories.
// If we pre-load them with an absolute path then we are good.
void PreloadLibs()
{
	wchar_t sys32Folder[MAX_PATH];
	GetSystemDirectory(sys32Folder, MAX_PATH);

	std::wstring version = (std::wstring(sys32Folder) + L"\\version.dll");
	std::wstring logoncli = (std::wstring(sys32Folder) + L"\\logoncli.dll");
	std::wstring sspicli = (std::wstring(sys32Folder) + L"\\sspicli.dll");

	LoadLibrary(version.c_str());
	LoadLibrary(logoncli.c_str());
	LoadLibrary(sspicli.c_str());
}

void MitigateDllHijacking()
{
	// Set the default DLL lookup directory to System32 for ourselves and kernel32.dll
	SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);

	HMODULE hKernel32 = LoadLibrary(L"kernel32.dll");
	ATLASSERT(hKernel32 != NULL);

	SetDefaultDllDirectoriesFunction pfn = (SetDefaultDllDirectoriesFunction)GetProcAddress(hKernel32, "SetDefaultDllDirectories");
	if (pfn) { (*pfn)(LOAD_LIBRARY_SEARCH_SYSTEM32); }

	PreloadLibs();
}

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
                      _In_opt_ HINSTANCE hPrevInstance,
                      _In_ LPWSTR lpCmdLine,
                      _In_ int nCmdShow)
{
	MitigateDllHijacking();	

	int exitCode = -1;
	CString cmdLine(lpCmdLine);

	if (cmdLine.Find(L"--checkInstall") >= 0) {
		// If we're already installed, exit as fast as possible
		if (!MachineInstaller::ShouldSilentInstall()) {
			return 0;
		}

		// Make sure update.exe gets silent
		wcscat(lpCmdLine, L" --silent");
	}

	HRESULT hr = ::CoInitialize(NULL);
	ATLASSERT(SUCCEEDED(hr));

	AtlInitCommonControls(ICC_COOL_CLASSES | ICC_BAR_CLASSES);
	_Module = new CAppModule();
	hr = _Module->Init(NULL, hInstance);

	bool isQuiet = (cmdLine.Find(L"-s") >= 0);
	bool weAreUACElevated = CUpdateRunner::AreWeUACElevated() == S_OK;
	bool attemptingToRerun = (cmdLine.Find(L"--rerunningWithoutUAC") >= 0);

	if (weAreUACElevated && attemptingToRerun) {
		CUpdateRunner::DisplayErrorMessage(CString(L"Please re-run this installer as a normal user instead of \"Run as Administrator\"."), NULL);
		exitCode = E_FAIL;
		goto out;
	}

	if (!CFxHelper::CanInstallDotNet4_5()) {
		// Explain this as nicely as possible and give up.
		MessageBox(0L, L"This program cannot run on Windows XP or before; it requires a later version of Windows.", L"Incompatible Operating System", 0);
		exitCode = E_FAIL;
		goto out;
	}

	NetVersion requiredVersion = CFxHelper::GetRequiredDotNetVersion();

	if (!CFxHelper::IsDotNetInstalled(requiredVersion)) {
		hr = CFxHelper::InstallDotNetFramework(requiredVersion, isQuiet);
		if (FAILED(hr)) {
			exitCode = hr; // #yolo
			CUpdateRunner::DisplayErrorMessage(CString(L"Failed to install the .NET Framework, try installing the latest version manually"), NULL);
			goto out;
		}
	
		// S_FALSE isn't failure, but we still shouldn't try to install
		if (hr != S_OK) {
			exitCode = 0;
			goto out;
		}
	}

	// If we're UAC-elevated, we shouldn't be because it will give us permissions
	// problems later. Just silently rerun ourselves.
	if (weAreUACElevated) {
		wchar_t buf[4096];
		HMODULE hMod = GetModuleHandle(NULL);
		GetModuleFileNameW(hMod, buf, 4096);
		wcscat(lpCmdLine, L" --rerunningWithoutUAC");

		CUpdateRunner::ShellExecuteFromExplorer(buf, lpCmdLine);
		exitCode = 0;
		goto out;
	}

	exitCode = CUpdateRunner::ExtractUpdaterAndRun(lpCmdLine, false);

out:
	_Module->Term();
	return exitCode;
}
