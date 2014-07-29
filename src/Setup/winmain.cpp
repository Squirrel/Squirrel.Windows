// Setup.cpp : Defines the entry point for the application.
//

#include "stdafx.h"
#include "Setup.h"

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
                     _In_opt_ HINSTANCE hPrevInstance,
                     _In_ LPWSTR lpCmdLine,
                     _In_ int       nCmdShow)
{
	HRESULT hr = ::CoInitialize(NULL);

	MessageBoxW(NULL, L"This is a test", L"My message is here", MB_OK);

	::CoUninitialize();
	return 0;
}