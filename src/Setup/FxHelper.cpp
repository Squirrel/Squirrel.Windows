#include "stdafx.h"
#include "FxHelper.h"
#include <string>

using std::wstring;

class ATL_NO_VTABLE CDownloadProgressCallback :
    public CComObjectRoot,
    public IBindStatusCallback
{
public:
    CDownloadProgressCallback()
    {}

    DECLARE_NOT_AGGREGATABLE(CDownloadProgressCallback)
    BEGIN_COM_MAP(CDownloadProgressCallback)
        COM_INTERFACE_ENTRY(IBindStatusCallback)
    END_COM_MAP()
    DECLARE_PROTECT_FINAL_CONSTRUCT()

    HRESULT FinalConstruct() { return S_OK; }

    void FinalRelease()
    {}

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

HRESULT CFxHelper::InstallDotnet(const RUNTIMEINFO* runtime, bool isQuiet)
{
    auto runtimeName = wstring(runtime->friendlyName);
    auto runtimeUrl = wstring(runtime->installerUrl);

    if (!isQuiet) {
        CTaskDialog dlg;
        TASKDIALOG_BUTTON buttons[] = {
            { 1, L"Install", },
            { 2, L"Cancel", },
        };

        wstring txtInstruction = L"Install " + runtimeName;
        wstring txtMain = L"This application requires " + runtimeName + L". Click the Install button to get started.";
        wstring txtExpanded = L"Clicking install will download the latest version of this operating system component from Microsoft and install it on your PC. Setup can not continue until this is complete.";

        dlg.SetButtons(buttons, 2);
        dlg.SetMainInstructionText(txtInstruction.c_str());
        dlg.SetContentText(txtMain.c_str());
        dlg.SetMainIcon(TD_INFORMATION_ICON);

        dlg.SetExpandedInformationText(txtExpanded.c_str());

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
            pd->SetLine(1, L"Downloading the .NET installer", FALSE, nullptr);
            pd->StartProgressDialog(nullptr, nullptr, 0, nullptr);

            CComObject<CDownloadProgressCallback>* bscbObj = nullptr;
            if (SUCCEEDED(CComObject<CDownloadProgressCallback>::CreateInstance(&bscbObj))) {
                bscbObj->SetProgressDialog(pd);
                bscb = bscbObj;
            }
        }
    }

    hr = URLDownloadToFile(nullptr, runtimeUrl.c_str(), szFinalTempFileName, 0, bscb);
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
        // hr = HandleRebootRequirement(isQuiet);
        // Exit as a failure, so that setup doesn't carry on now
        hr = ERROR_SUCCESS_REBOOT_REQUIRED;
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
