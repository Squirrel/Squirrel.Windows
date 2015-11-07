// WriteZipToSetup.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"


int wmain(int argc, wchar_t* argv[])
{
	if (argc != 2) {
		goto fail;
	}

	// Read the entire zip file into memory, yolo
	HANDLE hFile = CreateFile(argv[1], GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
	if (hFile == INVALID_HANDLE_VALUE) {
		printf("Can't open Zip file\n");
		return -1;
	}

	BY_HANDLE_FILE_INFORMATION fileInfo;
	if (!GetFileInformationByHandle(hFile, &fileInfo)) {
		goto fail;
	}

	BYTE* pBuf = new BYTE[fileInfo.nFileSizeLow];
	BYTE* pCurrent = pBuf;
	DWORD dwBytesRead;

	do {
		if (!ReadFile(hFile, pCurrent, 0x1000, &dwBytesRead, NULL)) {
			goto fail;
		}

		pCurrent += dwBytesRead;
	} while (dwBytesRead > 0);

	HANDLE hRes = BeginUpdateResource(argv[0], false);
	if (!hRes) {
		printf("Couldn't open setup.exe for writing\n");
		goto fail;
	}

	if (!UpdateResource(hRes, L"DATA", (LPCWSTR)131, 0x0409, pBuf, fileInfo.nFileSizeLow)) {
		printf("Failed to update resource\n");
		goto fail;
	}

	if (!EndUpdateResource(hRes, false)) {
		printf("Failed to update resource\n");
		goto fail;
	}

	return 0;

fail:
	printf("Usage: WriteZipToSetup [Setup.exe template] [Zip File]\n");
	return -1;
}