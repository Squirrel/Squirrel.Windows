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
				L"This application requires .NET Framework 4.5 or above. Clicking "
				L"the Install button will download the latest version of this operating "
				L"system component from Microsoft and install it on your PC.");

		int nButton;
		if (FAILED(dlg.DoModal(::GetActiveWindow(), &nButton)) || nButton != 1) {
			return S_FALSE;
		}
	}

	HRESULT hr = E_FAIL;
	WCHAR szFinalTempFileName[_MAX_PATH] = L"";
	CComPtr<IBindStatusCallback> bscb;
	CComPtr<IProgressDialog> pd;
	SHELLEXECUTEINFO execInfo = {sizeof(execInfo),};

	CString url;
	url.LoadString(IDS_FXDOWNLOADURL);

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
	} else {
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

	hr = exitCode != 0 ? E_FAIL : S_OK;

out:
	if (execInfo.hProcess != NULL && execInfo.hProcess != INVALID_HANDLE_VALUE) {
		CloseHandle(execInfo.hProcess);
	}

	if (*szFinalTempFileName != L'\0') {
		DeleteFile(szFinalTempFileName);
	}

	return hr;
}
