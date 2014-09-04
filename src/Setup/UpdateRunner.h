#pragma once
class CUpdateRunner
{
private:
	static void DisplayErrorMessage(CString& errorMessage, wchar_t* logFile);
public:
	static int ExtractUpdaterAndRun(wchar_t* lpCommandLine);
};
