#pragma once
// public methods defined in SplashImageUtils.cpp
HANDLE ShowSplashAndCreateCloseEventIfImageFound(HINSTANCE hInstance, std::wstring splashPath);
DWORD PumpMsgWaitingForEvent(HANDLE hProcess, HANDLE hCloseSplashEvent, DWORD dwMilliseconds);
