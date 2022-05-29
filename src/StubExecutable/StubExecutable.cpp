#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <tchar.h>
#include <string>
#include <sstream>
#include <vector>

using namespace std;

void throwLastWin32Error(wstring addedInfo)
{
    HRESULT hr = GetLastError();
    if (hr == 0) return;

    // https://stackoverflow.com/a/17387176/184746
    // https://stackoverflow.com/a/455533/184746
    LPWSTR messageBuffer = nullptr;
    size_t size = FormatMessage(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
                                NULL, hr, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (LPWSTR)&messageBuffer, 0, NULL);

    wstring message(messageBuffer, size);

    if (messageBuffer) {
        LocalFree(messageBuffer);
        messageBuffer = nullptr;
    }

    if (addedInfo.empty()) throw message;
    else throw wstring(addedInfo + L" " + message);
}

wstring getProcessPath()
{
    wchar_t ourFile[MAX_PATH];
    HMODULE hMod = GetModuleHandle(NULL);
    GetModuleFileName(hMod, ourFile, _countof(ourFile));
    return wstring(ourFile);
}

void wexec(const wchar_t* cmd)
{
    LPTSTR szCmdline = _tcsdup(cmd); // https://stackoverflow.com/a/10044348/184746

    STARTUPINFO si = { 0 };
    si.cb = sizeof(STARTUPINFO);
    si.wShowWindow = SW_SHOW;
    si.dwFlags = STARTF_USESHOWWINDOW;

    PROCESS_INFORMATION pi = { 0 };
    if (!CreateProcess(NULL, szCmdline, NULL, NULL, false, 0, NULL, NULL, &si, &pi)) {
        throwLastWin32Error(L"Unable to start process.");
    }

    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
}

vector<wchar_t> commandChars{ L' ', L'"', L'\n', L'\t', L'\v' };
wstring argsToCommandLine(const vector<wstring>& args)
{
    wstringstream ss;
    for (unsigned int i = 0; i < args.size(); i++) {
        auto& arg = args[i];
        if (arg.empty()) continue;
        if (ss.tellp() > 0) ss << L" ";

        bool ctrlChar = false;
        for (unsigned int n = 0; n < commandChars.size(); n++) {
            if (arg.find(commandChars[n]) != wstring::npos) {
                // there is a control char in this argument
                ctrlChar = true;
                break;
            }
        }

        if (!ctrlChar) {
            ss << arg;
            continue;
        }

        // need to surround with quotes and escape all the control characters
        ss << L"\"";
        for (unsigned int c = 0; c < arg.size(); c++) {
            int backslashes = 0;
            while (c < arg.size() && arg[c] == L'\\') {
                c++;
                backslashes++;
            }
            if (c == arg.size()) {
                ss << wstring(backslashes * 2, '\\');
                break;
            }
            else if (arg[c] == '"') {
                ss << wstring(backslashes * 2 + 1, '\\');
                ss << L"\"";
            }
            else {
                ss << wstring(backslashes, '\\');
                ss << arg[c];
            }
        }
        ss << L"\"";
    }
    return ss.str();
}

// https://stackoverflow.com/a/3418285/184746
bool replace(std::wstring& str, const std::wstring& from, const std::wstring& to)
{
    size_t start_pos = str.find(from);
    if (start_pos == std::wstring::npos)
        return false;
    str.replace(start_pos, from.length(), to);
    return true;
}

int APIENTRY wWinMain(_In_ HINSTANCE hInstance, _In_opt_ HINSTANCE hPrevInstance, _In_ LPWSTR lpCmdLine, _In_ int nCmdShow)
{
    try {
        wstring myexepath = getProcessPath();
        wstring arguments(lpCmdLine);
        bool dryRun = replace(arguments, L"--stub-dry-run", L"");

        auto lastSlash = myexepath.find_last_of(L'\\');
        if (lastSlash == wstring::npos) {
            throw wstring(L"Unable to find/parse running exe file path (no backslash).");
        }

        wstring mydirectory = myexepath.substr(0, lastSlash);
        wstring myname = myexepath.substr(lastSlash + 1);
        if (myname.empty() || mydirectory.empty()) {
            throw wstring(L"Unable to find/parse running exe file path (empty).");
        }

        wstring updatepath = mydirectory + L"\\Update.exe";

        vector<wstring> nargs{};
        nargs.push_back(updatepath);
        nargs.emplace_back(L"--processStart");
        nargs.push_back(myname);

        if (!arguments.empty()) {
            nargs.emplace_back(L"--process-start-args");
            nargs.push_back(arguments);
        }

        wstring cmd = argsToCommandLine(nargs);

        if (dryRun) MessageBox(0, cmd.c_str(), L"Stub Test Run", MB_OK);
        else wexec(cmd.c_str());
    }
    catch (wstring err) {
        wstring message = L"Stub: " + err;
        MessageBox(0, message.c_str(), L"Stub Failed", MB_OK | MB_ICONERROR);
    }
    catch (...) {
        MessageBox(0, L"An unknown error has occurred.", L"Stub Failed", MB_OK | MB_ICONERROR);
    }
}