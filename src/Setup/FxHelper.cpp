#include "stdafx.h"
#include "FxHelper.h"

CFxHelper::CFxHelper() { }
CFxHelper::~CFxHelper() { }

// http://msdn.microsoft.com/en-us/library/hh925568(v=vs.110).aspx#net_b
const wchar_t* ndpPath = L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full";
const int fx45ReleaseVersion = 378389;

bool CFxHelper::IsDotNet45OrHigherInstalled(void)
{
	ATL::CRegKey key;

	if (key.Open(HKEY_LOCAL_MACHINE, ndpPath, KEY_READ) != ERROR_SUCCESS) {
		DWORD dwErr = GetLastError();
		return false;
	}

	DWORD dwReleaseInfo = 0;
	if (key.QueryDWORDValue(L"Release", dwReleaseInfo) != ERROR_SUCCESS ||
		dwReleaseInfo < fx45ReleaseVersion) {
		return false;
	}

	return true;
}