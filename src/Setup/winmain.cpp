// Setup.cpp : Defines the entry point for the application.
//

#include "stdafx.h"
#include "Setup.h"
#include "FxHelper.h"
#include "UpdateRunner.h"
#include "MachineInstaller.h"
#include <cstdio>
#include "LicenseDialog.h"

CAppModule* _Module;

typedef BOOL(WINAPI *SetDefaultDllDirectoriesFunction)(DWORD DirectoryFlags);

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
                      _In_opt_ HINSTANCE hPrevInstance,
                      _In_ LPWSTR lpCmdLine,
                      _In_ int nCmdShow)
{
	// Attempt to mitigate http://textslashplain.com/2015/12/18/dll-hijacking-just-wont-die
	HMODULE hKernel32 = LoadLibrary(L"kernel32.dll");
	ATLASSERT(hKernel32 != NULL);

	SetDefaultDllDirectoriesFunction pfn = (SetDefaultDllDirectoriesFunction) GetProcAddress(hKernel32, "SetDefaultDllDirectories");
	if (pfn) { (*pfn)(LOAD_LIBRARY_SEARCH_SYSTEM32); }

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
   
   LicenseDialog license;
	HRESULT hr = ::CoInitialize(NULL);
	ATLASSERT(SUCCEEDED(hr));

	AtlInitCommonControls(ICC_COOL_CLASSES | ICC_BAR_CLASSES);
	_Module = new CAppModule();
	hr = _Module->Init(NULL, hInstance);

	bool isQuiet = (cmdLine.Find(L"-s") >= 0);
	bool weAreUACElevated = CUpdateRunner::AreWeUACElevated() == S_OK;
	bool attemptingToRerun = (cmdLine.Find(L"--rerunningWithoutUAC") >= 0);

	if (weAreUACElevated && attemptingToRerun) {
      CString strElevation;
      strElevation.LoadString( IDS_ELEVATION_ERROR );
		CUpdateRunner::DisplayErrorMessage( strElevation, NULL);
		exitCode = E_FAIL;
		goto out;
	}

	if (!CFxHelper::CanInstallDotNet4_5()) {
		// Explain this as nicely as possible and give up.
      CString strIncompatibleVersion;
      strIncompatibleVersion.LoadString( IDS_INCOMPATIBLE_VERSION_ERROR );
      CString strIncompatibleVersionTitle;
      strIncompatibleVersionTitle.LoadString( IDS_INCOMPATIBLE_VERSION_ERROR_TITLE );
		MessageBox(0L, strIncompatibleVersion, strIncompatibleVersionTitle, 0);
		exitCode = E_FAIL;
		goto out;
	}

   if ( license.ShouldShowLicense() )
   {
      if ( !license.AcceptLicense() )
      {
         exitCode = E_FAIL;
         goto out;
      }
   }

	NetVersion requiredVersion = CFxHelper::GetRequiredDotNetVersion();

	if (!CFxHelper::IsDotNetInstalled(requiredVersion)) {
		hr = CFxHelper::InstallDotNetFramework(requiredVersion, isQuiet);
		if (FAILED(hr)) {
			exitCode = hr; // #yolo
         CString strDotNetFrameworkError;
         strDotNetFrameworkError.LoadString( IDS_DOTNETFRAMEWORK_FAILED_ERROR );
			CUpdateRunner::DisplayErrorMessage( strDotNetFrameworkError, NULL);
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
