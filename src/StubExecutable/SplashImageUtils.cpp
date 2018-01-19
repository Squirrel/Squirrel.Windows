// SplashImageUtils.cpp : methods for displaying a splash image while starting
// the real program.  Many of these methods are adapted from those found at
// http://faithlife.codes/blog/2008/09/displaying_a_splash_screen_with_c_introduction/
// The original code is Copyright 2007-2008 Logos Bible Software and licensed
// under an MIT style license according  to
// http://faithlife.codes/blog/2008/09/logos_code_blog_license/.  The revisions
// and additions are Copyright 2017 SIL International and licensed under the same
// MIT style license.

// StubExecute gets renamed to the name of an executable program being installed, for
// example BloomBeta.exe.  This code looks for a PNG file named after the program,
// BloomBetaSplash.png for this example.  If this file is not found in the app folder
// where the real program exists, then StubExecute does nothing for displaying a splash
// image but behaves as before.  If the image file is found, then StubExecute tries to
// create a named event with a presumably unique name.  If this fails, then another
// instance of StubExecute must already be running and displaying a splash image.  In
// that case, StubExecute does not display a splash image but behaves as before.  If
// the event is successfully created, then StubExecute displays the splash image
// before starting the real program and then waits for the real program to use the
// named event to signal that StubExecute can shut down and stop displaying the splash
// image.  When displaying a splash image, StubExecute times out after 60 seconds and
// shuts down even if the invoked program has not signaled it.
//
// C# code to signal StubExecute would look something like this:
//
//	using (var closeSplashEvent = new System.Threading.EventWaitHandle(false,
//		System.Threading.EventResetMode.ManualReset, "CloseSquirrelSplashScreenEvent"))
//	{
//		closeSplashEvent.Set();
//	}

// Note that loading/creating the image uses COM based code, so the main function
// needs to call CoInitialize before calling ShowSplashAndCreateCloseEventIfImageFound().


#include "stdafx.h"
#include "Resource.h"

// forward declarations of local methods.
std::wstring GetSplashPath(std::wstring fullPath);
BOOL InitSplashWindows(HINSTANCE hInstance, LPCTSTR splashPath);
HWND CreateSplashWindow(HINSTANCE hInstance);
HBITMAP LoadSplashImage(LPCTSTR splashPath);
BOOL SetSplashImage(HWND hwndSplash, HBITMAP hbmpSplash);
ATOM RegisterWindowClass(HINSTANCE hInstance);

// If an appropriate image file exists in the same directory as the the real program, create a
// splash window and return an event handle for receiving a signal for StubExecute to close.
// (This value will be the second argument to PumpMsgWaitingForEvent() at the end of this file.)
// If there is no such image file, or the event cannot be created, then return NULL.
// hInstance is the HINSTANCE handle of this program (StubExecute by whatever name)
// fullPath is the path to the real executable program.
HANDLE ShowSplashAndCreateCloseEventIfImageFound(HINSTANCE hInstance, std::wstring exePath)
{
	HANDLE hCloseSplashEvent = NULL;
	std::wstring splashPath = GetSplashPath(exePath);
	if (splashPath.length() > 0)
	{
		// Create an event, making sure we're the first active process to create it.
		SetLastError(ERROR_SUCCESS);
		hCloseSplashEvent = CreateEvent(NULL, TRUE, FALSE, _T("CloseSquirrelSplashScreenEvent"));
		if (GetLastError() == ERROR_ALREADY_EXISTS)
			return NULL;
		if (!InitSplashWindows(hInstance, splashPath.c_str()))
			hCloseSplashEvent = NULL;
	}
	return hCloseSplashEvent;
}

// Replace the ".exe" at the end of the pathname with "Splash.png" and check
// whether the file exists.  If it does, return its path.  Otherwise return
// an empty string.
std::wstring GetSplashPath(std::wstring exePath)
{
	if (exePath.length() > 4)
	{
		std::wstring pngPath(exePath);
		pngPath.replace(pngPath.length() - 4, 4, L"Splash.png");
		if (PathFileExists(pngPath.c_str()))
			return pngPath;
	}
	return std::wstring(L"");
}

BOOL InitSplashWindows(HINSTANCE hInstance, LPCTSTR splashPath)
{
	if (!RegisterWindowClass(hInstance))
		return FALSE;
	HWND hwndSplash = CreateSplashWindow(hInstance);
	if (!hwndSplash)
		return FALSE;
	HBITMAP hbmpSplash = LoadSplashImage(splashPath);
	if (!hbmpSplash)
		return FALSE;
	return SetSplashImage(hwndSplash, hbmpSplash);
}

// Creates a 32-bit Device Independent Bitmap (DIB) from the specified Windows Imaging Component
// (WIC) bitmap source.  This allows the full 8 bits per color plus any "alpha" information which
// controls the transparency/opacity.
HBITMAP CreateHBITMAP(IWICBitmapSource * ipBitmap)
{
	// initialize return value
	HBITMAP hbmp = NULL;

	// get image attributes and check for valid image
	UINT width = 0;
	UINT height = 0;
	if (FAILED(ipBitmap->GetSize(&width, &height)) || width == 0 || height == 0)
		return NULL;

	// prepare structure giving bitmap information (negative height indicates a top-down DIB)
	BITMAPINFO bminfo;
	::ZeroMemory(&bminfo, sizeof(bminfo));
	bminfo.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
	bminfo.bmiHeader.biWidth = width;
	bminfo.bmiHeader.biHeight = -((LONG)height);
	bminfo.bmiHeader.biPlanes = 1;
	bminfo.bmiHeader.biBitCount = 32;
	bminfo.bmiHeader.biCompression = BI_RGB;

	// create a DIB section that can hold the image
	void * pvImageBits = NULL;
	HDC hdcScreen = ::GetDC(NULL);
	if (hdcScreen)
	{
		hbmp = ::CreateDIBSection(hdcScreen, &bminfo, DIB_RGB_COLORS, &pvImageBits, NULL, 0);
		::ReleaseDC(NULL, hdcScreen);
	}
	if (hbmp == NULL)
		return NULL;

	// extract the image into the HBITMAP
	const UINT cbStride = width * 4;
	const UINT cbImage = cbStride * height;
	if (FAILED(ipBitmap->CopyPixels(NULL, cbStride, cbImage, static_cast<BYTE *>(pvImageBits))))
	{
		// couldn't extract image; delete HBITMAP
		DeleteObject(hbmp);
		hbmp = NULL;
	}
	return hbmp;
}

// Loads a PNG image from the specified stream (using Windows Imaging Component).
IWICBitmapSource * LoadBitmapFromStream(IStream * ipImageStream)
{
	// load WIC's PNG decoder
	IWICBitmapDecoder * ipDecoder = NULL;
	if (FAILED(::CoCreateInstance(CLSID_WICPngDecoder, NULL, CLSCTX_INPROC_SERVER, __uuidof(ipDecoder), reinterpret_cast<void**>(&ipDecoder))))
		return NULL;

	// initialize return value
	IWICBitmapSource * ipBitmap = NULL;
	// load the PNG
	if (SUCCEEDED(ipDecoder->Initialize(ipImageStream, WICDecodeMetadataCacheOnLoad)))
	{
		// check for the presence of the first frame in the bitmap
		UINT nFrameCount = 0;
		if (SUCCEEDED(ipDecoder->GetFrameCount(&nFrameCount)) || nFrameCount != 1)
		{
			// load the first frame (i.e., the image)
			IWICBitmapFrameDecode * ipFrame = NULL;
			if (SUCCEEDED(ipDecoder->GetFrame(0, &ipFrame)))
			{
				// convert the image to 32bpp BGRA format with pre-multiplied alpha
				//   (it may not be stored in that format natively in the PNG resource,
				//   but we need this format to create the DIB to use on-screen)
				::WICConvertBitmapSource(GUID_WICPixelFormat32bppPBGRA, ipFrame, &ipBitmap);
				ipFrame->Release();
			}
		}
	}
	ipDecoder->Release();
	return ipBitmap;
}

// Loads the PNG file containing the splash image into a HBITMAP.
HBITMAP LoadSplashImage(LPCTSTR splashPath)
{
	HBITMAP hbmpSplash = NULL;
	// load the PNG image data into a stream
	IStream * ipImageStream = NULL;
	if (SUCCEEDED(SHCreateStreamOnFileEx(splashPath, STGM_READ, 0, FALSE, NULL, &ipImageStream)))
	{
		// load the bitmap with WIC
		IWICBitmapSource * ipBitmap = LoadBitmapFromStream(ipImageStream);
		if (ipBitmap != NULL)
		{
			// create a HBITMAP containing the image
			hbmpSplash = CreateHBITMAP(ipBitmap);
			ipBitmap->Release();
		}
		ipImageStream->Release();
	}
	return hbmpSplash;
}

// Window Class name
const TCHAR * c_szSplashClass = _T("SquirrelSplashWindow");

// Registers a window class for the splash and splash owner windows.
ATOM RegisterWindowClass(HINSTANCE hInstance)
{
	WNDCLASS wc = { 0 };
	wc.lpfnWndProc = DefWindowProc;
	wc.hInstance = hInstance;
	wc.hIcon = LoadIcon(hInstance, MAKEINTRESOURCE(IDI_STUBEXECUTABLE));
	wc.hCursor = LoadCursor(NULL, IDC_ARROW);
	wc.lpszClassName = c_szSplashClass;
	return ::RegisterClass(&wc);
}

// Creates the splash owner window and the splash window.
HWND CreateSplashWindow(HINSTANCE hInstance)
{
	HWND hwndOwner = ::CreateWindow(c_szSplashClass, NULL, WS_POPUP,
		0, 0, 0, 0, NULL, NULL, hInstance, NULL);
	if (!hwndOwner)
		return NULL;
	return ::CreateWindowEx(WS_EX_LAYERED, c_szSplashClass, NULL, WS_POPUP | WS_VISIBLE,
		0, 0, 0, 0, hwndOwner, NULL, hInstance, NULL);
}

// Calls UpdateLayeredWindow to set a bitmap (with alpha) as the content of the splash window.
BOOL SetSplashImage(HWND hwndSplash, HBITMAP hbmpSplash)
{
	// get the size of the bitmap
	BITMAP bm;
	if (!::GetObject(hbmpSplash, sizeof(bm), &bm))
		return FALSE;
	SIZE sizeSplash = { bm.bmWidth, bm.bmHeight };

	// get the primary monitor's info
	POINT ptZero = { 0 };
	HMONITOR hmonPrimary = ::MonitorFromPoint(ptZero, MONITOR_DEFAULTTOPRIMARY);
	if (!hmonPrimary)
		return FALSE;
	MONITORINFO monitorinfo = { 0 };
	monitorinfo.cbSize = sizeof(monitorinfo);
	if (!::GetMonitorInfo(hmonPrimary, &monitorinfo))
		return FALSE;

	// center the splash screen in the middle of the primary work area
	const RECT & rcWork = monitorinfo.rcWork;
	POINT ptOrigin;
	ptOrigin.x = rcWork.left + (rcWork.right - rcWork.left - sizeSplash.cx) / 2;
	ptOrigin.y = rcWork.top + (rcWork.bottom - rcWork.top - sizeSplash.cy) / 2;

	// create a memory DC holding the splash bitmap
	HDC hdcScreen = ::GetDC(NULL);
	if (!hdcScreen)
		return FALSE;
	HDC hdcMem = ::CreateCompatibleDC(hdcScreen);
	if (!hdcMem)
	{
		::ReleaseDC(NULL, hdcScreen);
		return FALSE;
	}
	HBITMAP hbmpOld = (HBITMAP)::SelectObject(hdcMem, hbmpSplash);
	if (!hbmpOld || hbmpOld == HGDI_ERROR)
	{
		::DeleteDC(hdcMem);
		::ReleaseDC(NULL, hdcScreen);
		return FALSE;
	}

	// use the source image's alpha channel for blending
	BLENDFUNCTION blend = { 0 };
	blend.BlendOp = AC_SRC_OVER;
	blend.SourceConstantAlpha = 255;
	blend.AlphaFormat = AC_SRC_ALPHA;

	// paint the window (in the right location) with the alpha-blended bitmap
	BOOL retval = ::UpdateLayeredWindow(hwndSplash, hdcScreen, &ptOrigin, &sizeSplash,
		hdcMem, &ptZero, RGB(0, 0, 0), &blend, ULW_ALPHA);

	// delete temporary objects
	::SelectObject(hdcMem, hbmpOld);
	::DeleteDC(hdcMem);
	::ReleaseDC(NULL, hdcScreen);
	return retval;
}

// Wait up to dwMilliseconds msec to receive a signal from the given process for the given event.
// Return as soon as the signal is received or the timeout occurs.
// The return value is what ::MsgWaitForMultipleObjects() returned the last time it was called
// in this method's internal loop.  It probably isn't useful, but maybe someday someone will want
// it for some reason.
DWORD PumpMsgWaitingForEvent(HANDLE hProcess, HANDLE hCloseSplashEvent, DWORD dwMilliseconds)
{
	HANDLE aHandles[2] = { hProcess, hCloseSplashEvent };
	const int numHandles = 2;

	// The starting time is needed to limit how long we wait for the shutdown message.
	const DWORD dwStartTickCount = ::GetTickCount();
	for (;;)
	{
		// calculate timeout, decreasing each time by the elapsed time so that we don't exceed the
		// original requested timeout period.
		const DWORD dwElapsed = ::GetTickCount() - dwStartTickCount;
		const DWORD dwTimeout = dwMilliseconds == INFINITE ? INFINITE :
			dwElapsed < dwMilliseconds ? dwMilliseconds - dwElapsed : 0;

		// Wait for a handle to be signaled or a message, timing out after dwTimeout msec.
		const DWORD dwWaitResult = ::MsgWaitForMultipleObjects(numHandles, aHandles, FALSE, dwTimeout, QS_ALLINPUT);
		if (dwWaitResult == WAIT_OBJECT_0 + numHandles)
		{
			// The msg is not from one of the handles.  Process it so that we can keep looking for the handle
			// to be signaled.  Pump all the messages for the current thread.  (This won't include signals from
			// the child process).
			MSG msg;
			while (::PeekMessage(&msg, NULL, 0, 0, PM_REMOVE) != FALSE)
			{
				// check for WM_QUIT -- the user might use Alt-F4 or the like to kill the splash image.
				if (msg.message == WM_QUIT)
				{
					// repost quit message and return
					::PostQuitMessage((int)msg.wParam);
					return dwWaitResult;
				}
				// dispatch thread message
				::TranslateMessage(&msg);
				::DispatchMessage(&msg);
			}
		}
		else
		{
			// The msg is from one of the handles (dwWaitResult = WAIT_OBJECT_0 + index), or maybe we timed out
			// (dwWaitResult = WAIT_TIMEOUT).  In either case, we can shut down so just return.
			return dwWaitResult;
		}
	}
}
