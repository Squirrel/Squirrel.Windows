#include "stdafx.h"
#include "FxHelper.h"
#include "resource.h"

// http://msdn.microsoft.com/en-us/library/hh925568(v=vs.110).aspx#net_b
static const wchar_t* ndpPath = L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full";
static const int fx45ReleaseVersion = 378389;
static const int fx451ReleaseVersion = 378675; //Minimum version for .NET 4.5.1
static const int fx452ReleaseVersion = 379893;
static const int fx46ReleaseVersion = 393295; //Windows 10 version, other systems are higher
static const int fx461ReleaseVersion = 394254; // Minimum version for .NET 4.6.1
static const int fx462ReleaseVersion = 394802; // Minimum version for .NET 4.6.2

// According to https://msdn.microsoft.com/en-us/library/8z6watww%28v=vs.110%29.aspx,
// to install .NET 4.5 we must be Vista SP2+, Windows 7 SP1+, or later.
// However Paul thinks this is just for customer support, anything >= Vista will generally work.
bool CFxHelper::CanInstallDotNet4_5()
{
	return IsWindowsVistaOrGreater();
}

NetVersion CFxHelper::GetRequiredDotNetVersion()
{
	wchar_t* versionFlag = (wchar_t*)LoadResource(NULL, FindResource(NULL, (LPCWSTR)IDR_FX_VERSION_FLAG, L"FLAGS"));
	CString resourceFlag(versionFlag);
	if (resourceFlag.Compare(L"net451") == 0) return NetVersion::net451;
	if (resourceFlag.Compare(L"net452") == 0) return NetVersion::net452;
	if (resourceFlag.Compare(L"net46") == 0) return NetVersion::net46;
	if (resourceFlag.Compare(L"net461") == 0) return NetVersion::net461;
	if (resourceFlag.Compare(L"net462") == 0) return NetVersion::net462;

	//Default to standard net45
	return NetVersion::net45;
}

bool CFxHelper::IsDotNetInstalled(NetVersion required)
{
	ATL::CRegKey key;

	if (key.Open(HKEY_LOCAL_MACHINE, ndpPath, KEY_READ) != ERROR_SUCCESS) {
		return false;
	}

	DWORD dwReleaseInfo = 0;
	if (key.QueryDWORDValue(L"Release", dwReleaseInfo) != ERROR_SUCCESS ||
		dwReleaseInfo < GetDotNetVersionReleaseNumber(required)) {
		return false;
	}

	return true;
}

int CFxHelper::GetDotNetVersionReleaseNumber(NetVersion version)
{
	switch (version) {
	case NetVersion::net451:
		return fx451ReleaseVersion;
	case NetVersion::net452:
		return fx452ReleaseVersion;
	case NetVersion::net46:
		return fx46ReleaseVersion;
	case NetVersion::net461:
		return fx461ReleaseVersion;
	case NetVersion::net462:
		return fx462ReleaseVersion;
	case NetVersion::net45:
	default:
		return fx45ReleaseVersion;
	}
}

class ATL_NO_VTABLE CDownloadProgressCallback :
	public CComObjectRoot,
	public IBindStatusCallback
{
public:
	CDownloadProgressCallback()
	{
	}

	DECLARE_NOT_AGGREGATABLE(CDownloadProgressCallback)

	BEGIN_COM_MAP(CDownloadProgressCallback)
		COM_INTERFACE_ENTRY(IBindStatusCallback)
	END_COM_MAP()

	DECLARE_PROTECT_FINAL_CONSTRUCT()

	HRESULT FinalConstruct() { return S_OK; }

	void FinalRelease()
	{
	}

	void SetProgressDialog(IProgressDialog* pd)
	{
		m_spProgressDialog = pd;
	}

	STDMETHOD(OnProgress)(ULONG ulProgress, ULONG ulProgressMax, ULONG /*ulStatusCode*/, LPCWSTR /*szStatusText*/)
	{
		if (m_spProgressDialog != nullptr) {
			if (m_spProgressDialog->HasUserCancelled()) {
				return E_ABORT;
			}

			m_spProgressDialog->SetProgress(ulProgress, ulProgressMax);
		}

		return S_OK;
	}

	STDMETHOD(OnStartBinding)(DWORD /*dwReserved*/, IBinding *pBinding) { return E_NOTIMPL; }
	STDMETHOD(GetPriority)(LONG *pnPriority) { return E_NOTIMPL; }
	STDMETHOD(OnLowResource)(DWORD /*reserved*/) { return E_NOTIMPL; }
	STDMETHOD(OnStopBinding)(HRESULT /*hresult*/, LPCWSTR /*szError*/) { return E_NOTIMPL; }
	STDMETHOD(GetBindInfo)(DWORD *pgrfBINDF, BINDINFO *pbindInfo) { return E_NOTIMPL; }
	STDMETHOD(OnDataAvailable)(DWORD grfBSCF, DWORD dwSize, FORMATETC * /*pformatetc*/, STGMEDIUM *pstgmed) { return E_NOTIMPL; }
	STDMETHOD(OnObjectAvailable)(REFIID /*riid*/, IUnknown * /*punk*/) { return E_NOTIMPL; }

private:
	CComPtr<IProgressDialog> m_spProgressDialog;
};

HRESULT CFxHelper::InstallDotNetFramework(NetVersion version, bool isQuiet)
{
	if (!isQuiet) {
		CTaskDialog dlg;
		TASKDIALOG_BUTTON buttons[] = {
			{ 1, L"Install", },
			{ 2, L"Cancel", },
		};


		dlg.SetButtons(buttons, 2);
		dlg.SetMainInstructionText(GetInstallerMainInstructionForVersion(version));
		dlg.SetContentText(GetInstallerContentForVersion(version));
		dlg.SetMainIcon(TD_INFORMATION_ICON);

		dlg.SetExpandedInformationText(GetInstallerExpandedInfoForVersion(version));

		int nButton;
		if (FAILED(dlg.DoModal(::GetActiveWindow(), &nButton)) || nButton != 1) {
			return S_FALSE;
		}
	}

	HRESULT hr = E_FAIL;
	WCHAR szFinalTempFileName[_MAX_PATH] = L"";
	CComPtr<IBindStatusCallback> bscb;
	CComPtr<IProgressDialog> pd;
	SHELLEXECUTEINFO execInfo = { sizeof(execInfo), };

	CString url;
	url.LoadString(GetInstallerUrlForVersion(version));

	WCHAR szTempPath[_MAX_PATH];
	DWORD dwTempPathResult = GetTempPath(_MAX_PATH, szTempPath);

	if (dwTempPathResult == 0) {
		hr = AtlHresultFromLastError();
		goto out;
	} else if (dwTempPathResult > _MAX_PATH) {
		hr = DISP_E_BUFFERTOOSMALL;
		goto out;
	}

	WCHAR szTempFileName[_MAX_PATH];
	if (!GetTempFileName(szTempPath, L"NDP", 0, szTempFileName)) {
		hr = AtlHresultFromLastError();
		goto out;
	}

	szTempFileName[_countof(szTempFileName) - 1] = L'\0';
	if (wcscpy_s(szFinalTempFileName, _countof(szFinalTempFileName), szTempFileName) != 0) {
		hr = E_FAIL;
		goto out;
	}

	WCHAR* pLastDot = wcsrchr(szFinalTempFileName, L'.');
	if (pLastDot == nullptr) {
		if (wcscat_s(szFinalTempFileName, _countof(szFinalTempFileName), L".exe") != 0) {
			hr = E_FAIL;
			goto out;
		}
	} else {
		if (wcscpy_s(pLastDot, _countof(szFinalTempFileName) - (pLastDot - szFinalTempFileName), L".exe") != 0) {
			hr = E_FAIL;
			goto out;
		}
	}

	if (!MoveFile(szTempFileName, szFinalTempFileName)) {
		hr = AtlHresultFromLastError();
		goto out;
	}

	if (!isQuiet) {
		pd.CoCreateInstance(CLSID_ProgressDialog);

		if (pd != nullptr) {
			pd->SetTitle(L"Downloading");
			pd->SetLine(1, L"Downloading the .NET Framework installer", FALSE, nullptr);
			pd->StartProgressDialog(nullptr, nullptr, 0, nullptr);

			CComObject<CDownloadProgressCallback>* bscbObj = nullptr;
			if (SUCCEEDED(CComObject<CDownloadProgressCallback>::CreateInstance(&bscbObj))) {
				bscbObj->SetProgressDialog(pd);
				bscb = bscbObj;
			}
		}
	}

	hr = URLDownloadToFile(nullptr, url, szFinalTempFileName, 0, bscb);
	if (pd != nullptr) {
		pd->StopProgressDialog();
	}
	if (hr != S_OK) {
		goto out;
	}

	execInfo.fMask = SEE_MASK_NOCLOSEPROCESS;
	execInfo.lpVerb = L"open";
	execInfo.lpFile = szFinalTempFileName;

	if (isQuiet) {
		execInfo.lpParameters = L"/q /norestart";
	}
	else {
		execInfo.lpParameters = L"/passive /norestart /showrmui";
	}

	execInfo.nShow = SW_SHOW;
	if (!ShellExecuteEx(&execInfo)) {
		hr = AtlHresultFromLastError();
		goto out;
	}

	WaitForSingleObject(execInfo.hProcess, INFINITE);

	DWORD exitCode;
	if (!GetExitCodeProcess(execInfo.hProcess, &exitCode)) {
		hr = AtlHresultFromLastError();
		goto out;
	}

	if (exitCode == 1641 || exitCode == 3010) {
		// The framework installer wants a reboot before we can continue
		// See https://msdn.microsoft.com/en-us/library/ee942965%28v=vs.110%29.aspx
		hr = HandleRebootRequirement(isQuiet);
		// Exit as a failure, so that setup doesn't carry on now
	}
	else {
		hr = exitCode != 0 ? E_FAIL : S_OK;
	}


out:
	if (execInfo.hProcess != NULL && execInfo.hProcess != INVALID_HANDLE_VALUE) {
		CloseHandle(execInfo.hProcess);
	}

	if (*szFinalTempFileName != L'\0') {
		DeleteFile(szFinalTempFileName);
	}

	return hr;
}

UINT CFxHelper::GetInstallerMainInstructionForVersion(NetVersion version)
{
	if (version >= NetVersion::net46) {
		return IDS_FXINSTRUCTION46;
	}
	return IDS_FXINSTRUCTION;
}

UINT CFxHelper::GetInstallerContentForVersion(NetVersion version)
{
	if (version >= NetVersion::net46) {
		return IDS_FXCONTENT46;
	}
	return IDS_FXCONTENT;
}

UINT CFxHelper::GetInstallerExpandedInfoForVersion(NetVersion version)
{
	if (version >= NetVersion::net46) {
		return IDS_FXEXPANDEDINFO46;
	}
	return IDS_FXEXPANDEDINFO;
}

UINT CFxHelper::GetInstallerUrlForVersion(NetVersion version)
{
	if (version >= NetVersion::net46) {
		return IDS_FXDOWNLOADURL46;
	}
	return IDS_FXDOWNLOADURL;
}

// Deal with the aftermath of the framework installer telling us that we need to reboot
HRESULT CFxHelper::HandleRebootRequirement(bool isQuiet)
{
	if (isQuiet) {
		// Don't silently reboot - just error-out
		fprintf_s(stderr, "A reboot is required following .NET installation - reboot then run installer again.\n");
		return E_FAIL;
	}

	CTaskDialog dlg;
	TASKDIALOG_BUTTON buttons[] = {
		{ 1, L"Restart Now", },
		{ 2, L"Cancel", },
	};

	dlg.SetButtons(buttons, 2);
	dlg.SetMainInstructionText(L"Restart System");
	dlg.SetContentText(L"To finish installing the .NET Framework, the system now needs to restart.  The installation will finish after you restart and log-in again.");
	dlg.SetMainIcon(TD_INFORMATION_ICON);

	dlg.SetExpandedInformationText(L"If you click 'Cancel', you'll need to re-run this setup program yourself, after restarting your system.");

	int nButton;
	if (FAILED(dlg.DoModal(::GetActiveWindow(), &nButton)) || nButton != 1) {
		return S_FALSE;
	}

	// We need to set up a runonce entry to restart this installer once the reboot has happened
	if (!WriteRunOnceEntry()) {
		return E_FAIL;
	}

	// And now, reboot
	if (!RebootSystem()) {
		return E_FAIL;
	}

	// About to reboot, but just in case...
	return S_FALSE;
}

//
// Write a runonce entry to the registry to tell it to continue with 
// setup after a reboot
//
bool CFxHelper::WriteRunOnceEntry()
{
	ATL::CRegKey key;

	if (key.Open(HKEY_CURRENT_USER, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce", KEY_WRITE) != ERROR_SUCCESS) {
		return false;
	}

	TCHAR exePath[MAX_PATH];
	GetModuleFileName(NULL, exePath, MAX_PATH);

	if (key.SetStringValue(L"SquirrelInstall", exePath) != ERROR_SUCCESS) {
		return false;
	}

	return true;
}

bool CFxHelper::RebootSystem()
{
	// First we need to enable the SE_SHUTDOWN_NAME privilege
	LUID luid;
	if (!LookupPrivilegeValue(L"", SE_SHUTDOWN_NAME, &luid)) {
		return false;
	}

	HANDLE hToken = NULL;
	if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES, &hToken)) {
		return false;
	}

	TOKEN_PRIVILEGES tp;
	tp.PrivilegeCount = 1;
	tp.Privileges[0].Luid = luid;
	tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
	if (!AdjustTokenPrivileges(hToken, FALSE, &tp, sizeof(tp), NULL, 0)) {
		CloseHandle(hToken);
		return false;
	}

	// Now we have that privilege, we can ask Windows to restart
	return ExitWindowsEx(EWX_REBOOT, 0) != 0;
}

