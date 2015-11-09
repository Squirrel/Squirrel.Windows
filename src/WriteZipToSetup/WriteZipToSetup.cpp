// WriteZipToSetup.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"


int wmain(int argc, wchar_t* argv[])
{
	if (argc != 3) {
		goto fail;
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

	if (!UpdateResource(hRes, L"DATA", (LPCWSTR)131, 0x0409, pBuf, fileInfo.nFileSizeLow)) {
		printf("Failed to update resource\n");
		goto fail;
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