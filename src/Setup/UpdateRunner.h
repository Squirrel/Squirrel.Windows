#pragma once
class CUpdateRunner
{
private:
	static void DisplayErrorMessage(CString& errorMessage);
public:
	static int ExtractUpdaterAndRun(wchar_t* lpCommandLine);
};
