#pragma once
class CUpdateRunner
{

public:
	static void DisplayErrorMessage(CString& errorMessage, wchar_t* logFile);
	static HRESULT AreWeUACElevated();
	static HRESULT ShellExecuteFromExplorer(LPWSTR pszFile, LPWSTR pszParameters);
	static int ExtractUpdaterAndRun(wchar_t* lpCommandLine, bool useFallbackDir);
};
