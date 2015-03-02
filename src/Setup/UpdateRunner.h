#pragma once
class CUpdateRunner
{
private:
	static void DisplayErrorMessage(CString& errorMessage, wchar_t* logFile);

public:
	static HRESULT AreWeUACElevated();
	static HRESULT ShellExecuteFromExplorer(LPWSTR pszFile, LPWSTR pszParameters);
	static int ExtractUpdaterAndRun(wchar_t* lpCommandLine);
};
