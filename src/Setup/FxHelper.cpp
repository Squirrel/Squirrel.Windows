#include "stdafx.h"
#include "FxHelper.h"
#include "resource.h"

// http://msdn.microsoft.com/en-us/library/hh925568(v=vs.110).aspx#net_b
static const wchar_t* ndpPath = L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full";
static const int fx45ReleaseVersion = 378389;

bool CFxHelper::IsDotNet45OrHigherInstalled()
{
	ATL::CRegKey key;

	if (key.Open(HKEY_LOCAL_MACHINE, ndpPath, KEY_READ) != ERROR_SUCCESS) {
		return false;
	}

	DWORD dwReleaseInfo = 0;
	if (key.QueryDWORDValue(L"Release", dwReleaseInfo) != ERROR_SUCCESS ||
		dwReleaseInfo < fx45ReleaseVersion) {
		return false;
	}

	return true;
}


void CFxHelper::HelpUserInstallDotNetFramework(bool isQuiet)
{
	if (isQuiet) return;

	CTaskDialog dlg;
	TASKDIALOG_BUTTON buttons [] = {
		{ 1, L"Install", },
		{ 2, L"Cancel", },
	};

	dlg.SetButtons(buttons, 2);
	dlg.SetMainInstructionText(L"Install .NET 4.5");
	dlg.SetContentText(L"This application requires the .NET Framework 4.5. Click the Install button to get started.");
	dlg.SetMainIcon(TD_INFORMATION_ICON);

	dlg.SetExpandedInformationText(
		L"This application requires .NET Framework 4.5 or above. Click\n"
		L"the 'Install' button in order to navigate to a website which\n"
		L"will help you to install the latest version of .NET");

	int nButton;
	if (SUCCEEDED(dlg.DoModal(::GetActiveWindow(), &nButton)) && nButton == 1) {
		CString url;
		url.LoadString(IDS_FXDOWNLOADURL);

		ShellExecute(NULL, NULL, url, NULL, NULL, SW_SHOW);
	}
}