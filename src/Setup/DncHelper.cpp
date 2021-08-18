#include "stdafx.h"
#include "DncHelper.h"
#include <string>

std::string exec(const char* cmd) {
	char buffer[128];
	std::string result = "";
	FILE* pipe = _popen(cmd, "r");
	if (!pipe)
		return "";
	try {
		while (fgets(buffer, sizeof buffer, pipe) != NULL) {
			result += buffer;
		}
	}
	catch (...) {
		_pclose(pipe);
		return "";
	}
	_pclose(pipe);
	return result;
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

	STDMETHOD(OnStartBinding)(DWORD /*dwReserved*/, IBinding* pBinding) { return E_NOTIMPL; }
	STDMETHOD(GetPriority)(LONG* pnPriority) { return E_NOTIMPL; }
	STDMETHOD(OnLowResource)(DWORD /*reserved*/) { return E_NOTIMPL; }
	STDMETHOD(OnStopBinding)(HRESULT /*hresult*/, LPCWSTR /*szError*/) { return E_NOTIMPL; }
	STDMETHOD(GetBindInfo)(DWORD* pgrfBINDF, BINDINFO* pbindInfo) { return E_NOTIMPL; }
	STDMETHOD(OnDataAvailable)(DWORD grfBSCF, DWORD dwSize, FORMATETC* /*pformatetc*/, STGMEDIUM* pstgmed) { return E_NOTIMPL; }
	STDMETHOD(OnObjectAvailable)(REFIID /*riid*/, IUnknown* /*punk*/) { return E_NOTIMPL; }

private:
	CComPtr<IProgressDialog> m_spProgressDialog;
};

wchar_t* txtInstruction = L"Install .NET 5.0";
wchar_t* txtMain = L"This application requires .Net 5.0. Click the Install button to get started.";
wchar_t* txtExpanded = L"This application requires .NET 5.0 to run. Clicking the Install button will download the latest version of this operating system component from Microsoft and install it on your PC.";
wchar_t* txtInstallerUrl = L"https://download.visualstudio.microsoft.com/download/pr/8bc41df1-cbb4-4da6-944f-6652378e9196/1014aacedc80bbcc030dabb168d2532f/windowsdesktop-runtime-5.0.9-win-x64.exe";

bool DncHelper::IsNet50Installed()
{
	// it might be better to parse this registry entry
	// static const wchar_t* dncPath = L"SOFTWARE\\dotnet\\Setup\\InstalledVersions";

	// note, dotnet cli will only return x64 results.
	auto runtimes = exec("dotnet --list-runtimes");
	return runtimes.find("Desktop.App 5.0") != std::string::npos;
}

HRESULT DncHelper::InstallNet50(bool isQuiet)
{
	if (!isQuiet) {
		CTaskDialog dlg;
		TASKDIALOG_BUTTON buttons[] = {
			{ 1, L"Install", },
			{ 2, L"Cancel", },
		};

		dlg.SetButtons(buttons, 2);
		dlg.SetMainInstructionText(txtInstruction);
		dlg.SetContentText(txtMain);
		dlg.SetMainIcon(TD_INFORMATION_ICON);

		dlg.SetExpandedInformationText(txtExpanded);

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

	WCHAR szTempPath[_MAX_PATH];
	DWORD dwTempPathResult = GetTempPath(_MAX_PATH, szTempPath);

	if (dwTempPathResult == 0) {
		hr = AtlHresultFromLastError();
		goto out;
	}
	else if (dwTempPathResult > _MAX_PATH) {
		hr = DISP_E_BUFFERTOOSMALL;
		goto out;
	}

	WCHAR szTempFileName[_MAX_PATH];
	if (!GetTempFileName(szTempPath, L"DNC", 0, szTempFileName)) {
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
	}
	else {
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
			pd->SetLine(1, L"Downloading .NET Installer", FALSE, nullptr);
			pd->StartProgressDialog(nullptr, nullptr, 0, nullptr);

			CComObject<CDownloadProgressCallback>* bscbObj = nullptr;
			if (SUCCEEDED(CComObject<CDownloadProgressCallback>::CreateInstance(&bscbObj))) {
				bscbObj->SetProgressDialog(pd);
				bscb = bscbObj;
			}
		}
	}

	hr = URLDownloadToFile(nullptr, txtInstallerUrl, szFinalTempFileName, 0, bscb);
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
		execInfo.lpParameters = L"/install /quiet /norestart";
	}
	else {
		execInfo.lpParameters = L"/install /passive /norestart";
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

	// I can't find any documentation on restart logic for dnc installer
	// perhaps it's the same as full fx but can't be sure.

	//if (exitCode == 1641 || exitCode == 3010) {
	//    // The framework installer wants a reboot before we can continue
	//    // See https://msdn.microsoft.com/en-us/library/ee942965%28v=vs.110%29.aspx
	//    hr = HandleRebootRequirement(isQuiet);
	//    // Exit as a failure, so that setup doesn't carry on now
	//}
	//else {
	hr = exitCode != 0 ? E_FAIL : S_OK;
	//}

out:
	if (execInfo.hProcess != NULL && execInfo.hProcess != INVALID_HANDLE_VALUE) {
		CloseHandle(execInfo.hProcess);
	}

	if (*szFinalTempFileName != L'\0') {
		DeleteFile(szFinalTempFileName);
	}

	return hr;
}