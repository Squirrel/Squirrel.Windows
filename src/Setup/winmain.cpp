// Setup.cpp : Defines the entry point for the application.
//

#include "stdafx.h"
#include "Setup.h"
#include "FxHelper.h"
#include "UpdateRunner.h"

CAppModule _Module;

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
                      _In_opt_ HINSTANCE hPrevInstance,
                      _In_ LPWSTR lpCmdLine,
                      _In_ int nCmdShow)
{
	int exitCode = -1;
	HRESULT hr = ::CoInitialize(NULL);
	ATLASSERT(SUCCEEDED(hr));

	AtlInitCommonControls(ICC_COOL_CLASSES | ICC_BAR_CLASSES);
	hr = _Module.Init(NULL, hInstance);

	CString cmdLine(lpCmdLine);
	bool isQuiet = (cmdLine.Find(L"/quiet") >= 0);

	if (!CFxHelper::IsDotNet45OrHigherInstalled()) {
		hr = CFxHelper::InstallDotNetFramework(isQuiet);
		if (hr != S_OK) {
			goto out;
		}
	}

	exitCode = CUpdateRunner::ExtractUpdaterAndRun(lpCmdLine);

out:
	_Module.Term();
	::CoUninitialize();
	return exitCode;
}
