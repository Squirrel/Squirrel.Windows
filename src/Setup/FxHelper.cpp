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

class ATL_NO_VTABLE CMyBindStatusCallback :
	public CComObjectRoot,
	public IBindStatusCallback
{
public:
	CMyBindStatusCallback()
	{
	}

DECLARE_NOT_AGGREGATABLE(CMyBindStatusCallback)

BEGIN_COM_MAP(CMyBindStatusCallback)
	COM_INTERFACE_ENTRY(IBindStatusCallback)
END_COM_MAP()

	DECLARE_PROTECT_FINAL_CONSTRUCT()

	HRESULT FinalConstruct()
	{
		return S_OK;
	}

	void FinalRelease()
	{
	}

	void SetProgressDialog(IProgressDialog* pd)
	{
		m_spProgressDialog = pd;
	}

	STDMETHOD(OnStartBinding)(DWORD /*dwReserved*/, IBinding *pBinding)
	{
		return E_NOTIMPL;
	}

	STDMETHOD(GetPriority)(LONG *pnPriority)
	{
		return E_NOTIMPL;
	}

	STDMETHOD(OnLowResource)(DWORD /*reserved*/)
	{
		return E_NOTIMPL;
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

	STDMETHOD(OnStopBinding)(HRESULT /*hresult*/, LPCWSTR /*szError*/)
	{
		return E_NOTIMPL;
	}

	STDMETHOD(GetBindInfo)(DWORD *pgrfBINDF, BINDINFO *pbindInfo)
	{
		return E_NOTIMPL;
	}

	STDMETHOD(OnDataAvailable)(DWORD grfBSCF, DWORD dwSize, FORMATETC * /*pformatetc*/, STGMEDIUM *pstgmed)
	{
		return E_NOTIMPL;
	}

	STDMETHOD(OnObjectAvailable)(REFIID /*riid*/, IUnknown * /*punk*/)
	{
		return E_NOTIMPL;
	}

private:
	CComPtr<IProgressDialog> m_spProgressDialog;
};

HRESULT CFxHelper::InstallDotNetFramework(bool isQuiet)
{
	if (!isQuiet) {
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
			L"the 'Install' button to download and install the latest\n"
			L"version of .NET.");

		int nButton;
		if (FAILED(dlg.DoModal(::GetActiveWindow(), &nButton)) || nButton != 1) {
			return S_FALSE;
		}
	}

	CString url;
	url.LoadString(IDS_FXDOWNLOADURL);

	WCHAR szTempPath[_MAX_PATH];
	DWORD dwTempPathResult = GetTempPath(_MAX_PATH, szTempPath);
	if (dwTempPathResult == 0) {
		return AtlHresultFromLastError();
	} else if (dwTempPathResult > _MAX_PATH) {
		return DISP_E_BUFFERTOOSMALL;
	}
	WCHAR szTempFileName[_MAX_PATH];
	if (!GetTempFileName(szTempPath, L"NDP", 0, szTempFileName)) {
		return AtlHresultFromLastError();
	}
	szTempFileName[_countof(szTempFileName) - 1] = L'\0';
	WCHAR szFinalTempFileName[_MAX_PATH];
	if (wcscpy_s(szFinalTempFileName, _countof(szFinalTempFileName), szTempFileName) != 0) {
		return E_FAIL;
	}
	WCHAR* pLastDot = wcsrchr(szFinalTempFileName, L'.');
	if (pLastDot == nullptr) {
		if (wcscat_s(szFinalTempFileName, _countof(szFinalTempFileName), L".exe") != 0) {
			return E_FAIL;
		}
	} else {
		if (wcscpy_s(szFinalTempFileName, _countof(szFinalTempFileName) - (pLastDot - szFinalTempFileName), L".exe") != 0) {
			return E_FAIL;
		}
	}
	if (!MoveFile(szTempFileName, szFinalTempFileName)) {
		return AtlHresultFromLastError();
	}

	CComPtr<IBindStatusCallback> bscb;
	CComPtr<IProgressDialog> pd;

	if (!isQuiet) {
		pd.CoCreateInstance(CLSID_ProgressDialog);
		if (pd != nullptr) {
			pd->SetTitle(L"Downloading");
			pd->SetLine(1, L"Downloading the .NET Framework installer", FALSE, nullptr);
			pd->StartProgressDialog(nullptr, nullptr, 0, nullptr);
			CComObject<CMyBindStatusCallback>* bscbObj = nullptr;
			if (SUCCEEDED(CComObject<CMyBindStatusCallback>::CreateInstance(&bscbObj))) {
				bscbObj->SetProgressDialog(pd);
				bscb = bscbObj;
			}
		}
	}

	HRESULT hr = URLDownloadToFile(nullptr, url, szFinalTempFileName, 0, bscb);
	if (pd != nullptr) {
		pd->StopProgressDialog();
	}
	if (hr != S_OK) {
		return hr;
	}

	SHELLEXECUTEINFO execInfo = {sizeof(execInfo),};
	execInfo.fMask = SEE_MASK_NOCLOSEPROCESS;
	execInfo.lpVerb = L"open";
	execInfo.lpFile = szFinalTempFileName;
	if (isQuiet) {
		execInfo.lpParameters = L"/q /norestart";
	} else {
		execInfo.lpParameters = L"/passive /norestart /showrmui";
	}
	execInfo.nShow = SW_SHOW;
	if (!ShellExecuteEx(&execInfo)) {
		return AtlHresultFromLastError();
	}
	WaitForSingleObject(execInfo.hProcess, INFINITE);
	DWORD exitCode;
	if (!GetExitCodeProcess(execInfo.hProcess, &exitCode)) {
		return AtlHresultFromLastError();
	}
	CloseHandle(execInfo.hProcess);

	DeleteFile(szFinalTempFileName);
	return exitCode;
}
