#include "stdafx.h"
#include "unzip.h"
#include "Resource.h"
#include "UpdateRunner.h"
#include <vector>

void CUpdateRunner::DisplayErrorMessage(CString& errorMessage, wchar_t* logFile)
{
	CTaskDialog dlg;
	TASKDIALOG_BUTTON buttons[] = {
		{ 1, L"Open Setup Log", },
		{ 2, L"Close", },
	};

	// TODO: Something about contacting support?
	dlg.SetButtons(buttons, 2, 1);
	dlg.SetMainInstructionText(L"Installation has failed");
	dlg.SetContentText(errorMessage);
	dlg.SetMainIcon(TD_ERROR_ICON);

	int nButton;

	if (FAILED(dlg.DoModal(::GetActiveWindow(), &nButton))) {
		return;
	}

	if (nButton == 1) {
		ShellExecute(NULL, NULL, logFile, NULL, NULL, SW_SHOW);
	}
}

HRESULT CUpdateRunner::AreWeUACElevated()
{
	HANDLE hProcess = GetCurrentProcess();
	HANDLE hToken = 0;
	HRESULT hr;

	if (!OpenProcessToken(hProcess, TOKEN_QUERY, &hToken)) {
		hr = HRESULT_FROM_WIN32(GetLastError());
		goto out;
	}

	TOKEN_ELEVATION_TYPE elevType;
	DWORD dontcare;
	if (!GetTokenInformation(hToken, TokenElevationType, &elevType, sizeof(TOKEN_ELEVATION_TYPE), &dontcare)) {
		hr = HRESULT_FROM_WIN32(GetLastError());
		goto out;
	}

	hr = (elevType == TokenElevationTypeFull ? S_OK : S_FALSE);

out:
	if (hToken) {
		CloseHandle(hToken);
	}

	return hr;
}

HRESULT FindDesktopFolderView(REFIID riid, void **ppv)
{
	HRESULT hr;

	CComPtr<IShellWindows> spShellWindows;
	spShellWindows.CoCreateInstance(CLSID_ShellWindows);

	CComVariant vtLoc(CSIDL_DESKTOP);
	CComVariant vtEmpty;
	long lhwnd;
	CComPtr<IDispatch> spdisp;

	hr = spShellWindows->FindWindowSW(
		&vtLoc, &vtEmpty,
		SWC_DESKTOP, &lhwnd, SWFO_NEEDDISPATCH, &spdisp);
	if (FAILED(hr)) return hr;

	CComPtr<IShellBrowser> spBrowser;
	hr = CComQIPtr<IServiceProvider>(spdisp)->QueryService(SID_STopLevelBrowser, IID_PPV_ARGS(&spBrowser));
	if (FAILED(hr)) return hr;

	CComPtr<IShellView> spView;
	hr = spBrowser->QueryActiveShellView(&spView);
	if (FAILED(hr)) return hr;

	hr = spView->QueryInterface(riid, ppv);
	if (FAILED(hr)) return hr;

	return S_OK;
}

HRESULT GetDesktopAutomationObject(REFIID riid, void **ppv)
{
	HRESULT hr;

	CComPtr<IShellView> spsv;
	hr = FindDesktopFolderView(IID_PPV_ARGS(&spsv));
	if (FAILED(hr)) return hr;

	CComPtr<IDispatch> spdispView;
	hr = spsv->GetItemObject(SVGIO_BACKGROUND, IID_PPV_ARGS(&spdispView));
	if (FAILED(hr)) return hr;

	return spdispView->QueryInterface(riid, ppv);
}

HRESULT CUpdateRunner::ShellExecuteFromExplorer(LPWSTR pszFile, LPWSTR pszParameters)
{
	HRESULT hr;

	CComPtr<IShellFolderViewDual> spFolderView;
	hr = GetDesktopAutomationObject(IID_PPV_ARGS(&spFolderView));
	if (FAILED(hr)) return hr;

	CComPtr<IDispatch> spdispShell;
	hr = spFolderView->get_Application(&spdispShell);
	if (FAILED(hr)) return hr;

	return CComQIPtr<IShellDispatch2>(spdispShell)->ShellExecute(
		CComBSTR(pszFile),
		CComVariant(pszParameters ? pszParameters : L""),
		CComVariant(L""),
		CComVariant(L""),
		CComVariant(SW_SHOWDEFAULT));
}

int CUpdateRunner::ExtractUpdaterAndRun(wchar_t* lpCommandLine)
{
	PROCESS_INFORMATION pi = { 0 };
	STARTUPINFO si = { 0 };
	CResource zipResource;
	wchar_t targetDir[MAX_PATH];
	wchar_t logFile[MAX_PATH];
	std::vector<CString> to_delete;

	ExpandEnvironmentStrings(L"%LocalAppData%\\SquirrelTemp", targetDir, _countof(targetDir));
	if (!CreateDirectory(targetDir, NULL) && GetLastError() != ERROR_ALREADY_EXISTS) {
		goto failedExtract;
	}

	swprintf_s(logFile, L"%s\\SquirrelSetup.log", targetDir);

	if (!zipResource.Load(L"DATA", IDR_UPDATE_ZIP)) {
		goto failedExtract;
	}

	DWORD dwSize = zipResource.GetSize();
	if (dwSize < 0x100) {
		goto failedExtract;
	}

	BYTE* pData = (BYTE*)zipResource.Lock();
	HZIP zipFile = OpenZip(pData, dwSize, NULL);
	SetUnzipBaseDir(zipFile, targetDir);

	// NB: This library is kind of a disaster
	ZRESULT zr;
	int index = 0;
	do {
		ZIPENTRY zentry;
		wchar_t targetFile[MAX_PATH];

		zr = GetZipItem(zipFile, index, &zentry);
		if (zr != ZR_OK && zr != ZR_MORE) {
			break;
		}

		// NB: UnzipItem won't overwrite data, we need to do it ourselves
		swprintf_s(targetFile, L"%s\\%s", targetDir, zentry.name);
		DeleteFile(targetFile);

		if (UnzipItem(zipFile, index, zentry.name) != ZR_OK) break;
		to_delete.push_back(CString(targetFile));
		index++;
	} while (zr == ZR_MORE || zr == ZR_OK);

	CloseZip(zipFile);
	zipResource.Release();

	// nfi if the zip extract actually worked, check for Update.exe
	wchar_t updateExePath[MAX_PATH];
	swprintf_s(updateExePath, L"%s\\%s", targetDir, L"Update.exe");

	if (GetFileAttributes(updateExePath) == INVALID_FILE_ATTRIBUTES) {
		goto failedExtract;
	}

	// Run Update.exe
	si.cb = sizeof(STARTUPINFO);
	si.wShowWindow = SW_SHOW;
	si.dwFlags = STARTF_USESHOWWINDOW;

	if (!lpCommandLine || wcsnlen_s(lpCommandLine, MAX_PATH) < 1) {
		lpCommandLine = L"";
	}

	wchar_t cmd[MAX_PATH];
	swprintf_s(cmd, L"\"%s\" --install . %s", updateExePath, lpCommandLine);

	if (!CreateProcess(NULL, cmd, NULL, NULL, false, 0, NULL, targetDir, &si, &pi)) {
		goto failedExtract;
	}

	WaitForSingleObject(pi.hProcess, INFINITE);

	DWORD dwExitCode;
	if (!GetExitCodeProcess(pi.hProcess, &dwExitCode)) {
		dwExitCode = (DWORD)-1;
	}

	if (dwExitCode != 0) {
		DisplayErrorMessage(CString(
			L"There was an error while installing the application. " 
			L"Check the setup log for more information and contact the author."), logFile);
	}

	for (unsigned int i = 0; i < to_delete.size(); i++) {
		DeleteFile(to_delete[i]);
	}

	CloseHandle(pi.hProcess);
	CloseHandle(pi.hThread);
	return (int) dwExitCode;

failedExtract:
	DisplayErrorMessage(CString(L"Failed to extract installer"), NULL);
	return (int) dwExitCode;
}
