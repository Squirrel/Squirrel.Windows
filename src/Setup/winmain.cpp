// Setup.cpp : Defines the entry point for the application.
//

#include "stdafx.h"
#include "Setup.h"
#include "FxHelper.h"
#include "UpdateRunner.h"
#include "MachineInstaller.h"

CAppModule _Module;

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
                      _In_opt_ HINSTANCE hPrevInstance,
                      _In_ LPWSTR lpCmdLine,
                      _In_ int nCmdShow)
{
	int exitCode = -1;
	CString cmdLine(lpCmdLine);

	if (cmdLine.Find(L"--checkInstall") >= 0) {
		// If we're already installed, exit as fast as possible
		if (!MachineInstaller::ShouldSilentInstall()) {
			exitCode = 0;
			goto out;
		}
	}

	HRESULT hr = ::CoInitialize(NULL);
	ATLASSERT(SUCCEEDED(hr));

	AtlInitCommonControls(ICC_COOL_CLASSES | ICC_BAR_CLASSES);
	hr = _Module.Init(NULL, hInstance);

	bool isQuiet = (cmdLine.Find(L"-s") >= 0);

	if (cmdLine.Find(L"--machine") >= 0) {
		exitCode = MachineInstaller::PerformMachineInstallSetup();
		if (exitCode != 0) goto out;
		isQuiet = true;
	}

	if (!CFxHelper::CanInstallDotNet4_5()) {
		// Explain this as nicely as possible and give up.
		MessageBox(0L, L"This program cannot run on Windows XP or before; it requires a later version of Windows.", L"Incompatible Operating System", 0);
		exitCode = E_FAIL;
		goto out;
	}

	if (!CFxHelper::IsDotNet45OrHigherInstalled()) {
		hr = CFxHelper::InstallDotNetFramework(isQuiet);
		if (FAILED(hr)) {
			exitCode = hr; // #yolo
			goto out;
		}
	
		// S_FALSE isn't failure, but we still shouldn't try to install
		if (hr != S_OK) {
			exitCode = 0;
			goto out;
		}
	}

	hr = CUpdateRunner::AreWeUACElevated();

	// If we're UAC-elevated, we shouldn't be because it will give us permissions
	// problems later. Just silently rerun ourselves.
	if (hr == S_OK) {
		wchar_t buf[4096];
		HMODULE hMod = GetModuleHandle(NULL);
		GetModuleFileNameW(hMod, buf, 4096);

		CUpdateRunner::ShellExecuteFromExplorer(buf, lpCmdLine);
		exitCode = 0;
		goto out;
	}

	exitCode = CUpdateRunner::ExtractUpdaterAndRun(lpCmdLine, false);

out:
	_Module.Term();
	::CoUninitialize();
	return exitCode;
}
