#pragma once
class CUpdateRunner
{
public:
	static void DisplayErrorMessage(CString& errorMessage, wchar_t* logFile);
	static HRESULT AreWeUACElevated();
	static HRESULT ShellExecuteFromExplorer(LPWSTR pszFile, LPWSTR pszParameters);
	static bool DirectoryExists(wchar_t* szPath);
	static bool DirectoryIsWritable(wchar_t* szPath);
	static int ExtractUpdaterAndRun(wchar_t* lpCommandLine, bool useFallbackDir);

protected:
	static void CheckSpecialBuild(wchar_t* targetDir, wchar_t* commandLine);
};
