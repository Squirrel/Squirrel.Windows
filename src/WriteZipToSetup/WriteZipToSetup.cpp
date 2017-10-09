// WriteZipToSetup.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"

using namespace std;
#include <regex>
#include <sstream>
#include <fstream>

#define IDR_LICENSE_RTF 138

BOOL CALLBACK EnumResLangProc(HMODULE hModule, LPCTSTR lpszType, LPCTSTR lpszName, WORD wIDLanguage, LONG_PTR lParam)
{
	HANDLE hUpdate = (HANDLE)lParam;
	HRSRC hFindItAgain = FindResourceEx(hModule, lpszType, lpszName, wIDLanguage);

	HGLOBAL hGlobal = LoadResource(hModule, hFindItAgain);
	if (!hGlobal) return true;

	UpdateResource(hUpdate, lpszType, lpszName, wIDLanguage, LockResource(hGlobal), SizeofResource(hModule, hFindItAgain));
	return true;
}

BOOL CALLBACK EnumResNameProc(HMODULE hModule, LPCTSTR lpszType, LPTSTR lpszName, LONG_PTR lParam)
{
	HANDLE hUpdate = (HANDLE)lParam;

	EnumResourceLanguages(hModule, lpszType, lpszName, EnumResLangProc, (LONG_PTR)hUpdate);
	return true;
}

BOOL CALLBACK EnumResTypeProc(HMODULE hMod, LPTSTR lpszType, LONG_PTR lParam) 
{
	std::vector<wchar_t*>* typeList = (std::vector<wchar_t*>*)lParam;
	if (IS_INTRESOURCE(lpszType)) {
		typeList->push_back(lpszType);
	} else {
		typeList->push_back(_wcsdup(lpszType));
	}

	return true;
}

int CopyResourcesToStubExecutable(wchar_t* src, wchar_t* dest) 
{
	HMODULE hSrc = LoadLibraryEx(src, NULL, LOAD_LIBRARY_AS_DATAFILE);
	if (!hSrc) return GetLastError();

	HANDLE hUpdate = BeginUpdateResource(dest, true);
	if (!hUpdate) return GetLastError();

	std::vector<wchar_t*> typeList;
	EnumResourceTypes(hSrc, EnumResTypeProc, (LONG_PTR)&typeList);

	for (auto& type : typeList) {
		EnumResourceNames(hSrc, type, EnumResNameProc, (LONG_PTR)hUpdate);
	}

	EndUpdateResource(hUpdate, false);
	return 0;
}

// Copy license-locale-name.rtf (i.e. license-en-US.rtf, license-de-DE.rtf) into Setup.exe resources
bool UpdateLicenseResources( HANDLE hRes, const std::wstring& licenseDirectory )
{
   WIN32_FIND_DATA findData;
   wstring licenseWildcard = licenseDirectory + L"\\license*.rtf";
   HANDLE hFind = ::FindFirstFile( licenseWildcard.c_str(), &findData );
   bool foundFile = ( hFind != INVALID_HANDLE_VALUE );
   bool success = true;
   std::wregex langRegex( L"license-(.*).rtf" );
   while ( foundFile && success )
   {
      std::wstring filename = findData.cFileName;
      std::wstring localeName;
      std::wsmatch match;

      if ( std::regex_search( filename, match, langRegex ) )
      {
         localeName = match.str( 1 );
         LCID lcid = LocaleNameToLCID( localeName.c_str(), 0 );

         std::ifstream is;
         std::wstring filepath = licenseDirectory + L"\\" + filename;
         is.open( filepath.c_str(), ios::binary );
         auto stringstream = std::ostringstream();
         stringstream << is.rdbuf();
         auto buffer = stringstream.str(); 
         if ( !UpdateResource( hRes, L"LICENSE", MAKEINTRESOURCE( IDR_LICENSE_RTF ), (WORD)lcid, (LPVOID)buffer.c_str(), buffer.length() ) )
         {
            success = false;
         }
      }


      foundFile = ::FindNextFile( hFind, &findData ) != 0;
   }

   if ( hFind != INVALID_HANDLE_VALUE )
   {
      ::FindClose( hFind );
   }
   return success;
}

int wmain(int argc, wchar_t* argv[])
{
   wstring licenseDir = L"";

	if (argc > 1 && wcscmp(argv[1], L"--copy-stub-resources") == 0) {
		if (argc != 4) goto fail;
		return CopyResourcesToStubExecutable(argv[2], argv[3]);
	}
   if ( argc < 3 )
   {
      goto fail;
   }
	bool setFramework = false;
   wchar_t* dotnetFramework;

   for ( int i = 0; i < argc; i++ )
   {
      if ( wcscmp( argv[i], L"--set-required-framework" ) == 0 && (i + 1 ) < argc )
      {
         setFramework = true;
         dotnetFramework = argv[i + 1];
      }
      else if ( wcscmp( argv[i], L"--license-dir" ) == 0 && ( i + 1 ) < argc )
      {
         licenseDir = argv[i + 1];
      }
   }

	wprintf(L"Setup: %s, Zip: %s\n", argv[1], argv[2]);

	// Read the entire zip file into memory, yolo
	HANDLE hFile = CreateFile(argv[2], GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
	if (hFile == INVALID_HANDLE_VALUE) {
		printf("Can't open Zip file\n");
		return -1;
	}

	BY_HANDLE_FILE_INFORMATION fileInfo;
	if (!GetFileInformationByHandle(hFile, &fileInfo)) {
		goto fail;
	}

	BYTE* pBuf = new BYTE[fileInfo.nFileSizeLow + 0x1000];
	BYTE* pCurrent = pBuf;
	DWORD dwBytesRead;

	printf("Starting to read in Zip file!\n");
	do {
		if (!ReadFile(hFile, pCurrent, 0x1000, &dwBytesRead, NULL)) {
			printf("Failed to read file! 0x%p\n", GetLastError());
			goto fail;
		}

		pCurrent += dwBytesRead;
	} while (dwBytesRead > 0);
   
	printf("Updating Resource!\n");
	HANDLE hRes = BeginUpdateResource(argv[1], false);
	if (!hRes) {
		printf("Couldn't open setup.exe for writing\n");
		goto fail;
	}

   if ( !licenseDir.empty() )
   {
      if ( !UpdateLicenseResources( hRes, licenseDir ) )
      {
         printf( "Failed to update license resources\n" );
         goto fail;
      }
   }

	if (!UpdateResource(hRes, L"DATA", (LPCWSTR)131, 0x0409, pBuf, fileInfo.nFileSizeLow)) {
		printf("Failed to update resource\n");
		goto fail;
	}

	if (setFramework) {
		if (!UpdateResource(hRes, L"FLAGS", (LPCWSTR)132, 0x0409, dotnetFramework, (wcslen( dotnetFramework )+1) * sizeof(wchar_t))) {
			printf("Failed to update resouce\n");
			goto fail;
		}
	}

	printf("Finished!\n");
	if (!EndUpdateResource(hRes, false)) {
		printf("Failed to update resource\n");
		goto fail;
	}

	printf("It worked!\n");
	return 0;

fail:
	printf("Usage: WriteZipToSetup [Setup.exe template] [Zip File]\n");
	return -1;
}