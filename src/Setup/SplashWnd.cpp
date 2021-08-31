/*! file SplashWnd.cpp
\brief Класс, реализующий splash-окно в отдельном потоке. Без MFC.

Created 2006-06-08 by Kirill V. Lyadvinsky
Modified:
2007-05-25 Добавлена дополнительная синхронизация. Поправлена работа с таймером.
2008-06-20 Добавлен вывод текста. Поддержка нескольких мониторов. Поддержка подгружаемых ресурсов (DLL)
2021-08-31 Caelan Sayler - Add support for animated GIF's

* The contents of this file are subject to the terms of the Common Development and
* Distribution License ("CDDL")(collectively, the "License"). You may not use this
* file except in compliance with the License. You can obtain a copy of the CDDL at
* http://www.opensource.org/licenses/cddl1.php.

*/

#include "stdafx.h"
#include <Windows.h>
#include <CommCtrl.h>
#include <string>
#include <process.h>
#include <GdiPlus.h>
#include "SplashWnd.h"

CSplashWnd::CSplashWnd(HWND hParent)
{
    m_hThread = NULL;
    m_pImage = NULL;
    m_hSplashWnd = NULL;
    m_ThreadId = 0;
    m_hEvent = NULL;
    m_hParentWnd = hParent;
}

CSplashWnd::~CSplashWnd()
{
    Hide();
    if (m_pImage) delete m_pImage;
}

void CSplashWnd::SetImage(const wchar_t* resid, const wchar_t* restype)
{
    m_pImage = new ImageEx(resid, restype);
}

void CSplashWnd::Show()
{
    if (m_hThread == NULL)
    {
        m_hEvent = CreateEvent(NULL, FALSE, FALSE, NULL);
        m_hThread = (HANDLE)_beginthreadex(NULL, 0, SplashThreadProc, static_cast<LPVOID>(this), 0, &m_ThreadId);
        if (WaitForSingleObject(m_hEvent, 5000) == WAIT_TIMEOUT)
        {
            OutputDebugString(L"Error starting SplashThread\n");
        }
    }
    else
    {
        PostThreadMessage(m_ThreadId, WM_ACTIVATE, WA_CLICKACTIVE, 0);
    }
}

void CSplashWnd::Hide()
{
    if (m_hThread)
    {
        PostThreadMessage(m_ThreadId, WM_QUIT, 0, 0);
        if (WaitForSingleObject(m_hThread, 9000) == WAIT_TIMEOUT)
        {
            ::TerminateThread(m_hThread, 2222);
        }
        CloseHandle(m_hThread);
        CloseHandle(m_hEvent);
    }
    m_hThread = NULL;
}

unsigned int __stdcall CSplashWnd::SplashThreadProc(void* lpParameter)
{
    CSplashWnd* pThis = static_cast<CSplashWnd*>(lpParameter);
    if (pThis->m_pImage == NULL)
        return 0;

    RectF rcBounds;
    Unit bmUnit = Unit::UnitPixel;

    if (pThis->m_pImage->GetBounds(&rcBounds, &bmUnit) != Status::Ok)
        return 0;

    if (rcBounds.IsEmptyArea())
        return 0;

    // Register your unique class name
    WNDCLASS wndcls = { 0 };

    wndcls.style = CS_HREDRAW | CS_VREDRAW;
    wndcls.lpfnWndProc = SplashWndProc;
    wndcls.hInstance = GetModuleHandle(NULL);
    wndcls.hCursor = LoadCursor(NULL, IDC_APPSTARTING);
    wndcls.hbrBackground = (HBRUSH)(COLOR_WINDOW + 1);
    wndcls.lpszClassName = L"SplashWnd";
    wndcls.hIcon = LoadIcon(wndcls.hInstance, MAKEINTRESOURCE(IDI_APPLICATION));

    if (!RegisterClass(&wndcls))
    {
        if (GetLastError() != 0x00000582) // already registered)
        {
            OutputDebugString(L"Unable to register class SplashWnd\n");
            return 0;
        }
    }

    // try to find monitor where mouse was last time
    POINT point = { 0 };
    MONITORINFO mi = { sizeof(MONITORINFO), 0 };
    HMONITOR hMonitor = 0;
    RECT rcArea = { 0 };

    ::GetCursorPos(&point);
    hMonitor = ::MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
    if (::GetMonitorInfo(hMonitor, &mi))
    {
        rcArea.left = (mi.rcMonitor.right + mi.rcMonitor.left - static_cast<long>(pThis->m_pImage->GetWidth())) / 2;
        rcArea.top = (mi.rcMonitor.top + mi.rcMonitor.bottom - static_cast<long>(pThis->m_pImage->GetHeight())) / 2;
    }
    else
    {
        SystemParametersInfo(SPI_GETWORKAREA, NULL, &rcArea, NULL);
        rcArea.left = (rcArea.right + rcArea.left - pThis->m_pImage->GetWidth()) / 2;
        rcArea.top = (rcArea.top + rcArea.bottom - pThis->m_pImage->GetHeight()) / 2;
    }

    pThis->m_hSplashWnd = CreateWindowEx(
        WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
        L"SplashWnd",
        L"Setup",
        WS_CLIPCHILDREN | WS_POPUP,
        rcArea.left, rcArea.top, pThis->m_pImage->GetWidth(), pThis->m_pImage->GetHeight(),
        pThis->m_hParentWnd,
        NULL,
        wndcls.hInstance,
        NULL);

    if (!pThis->m_hSplashWnd)
    {
        OutputDebugString(L"Unable to create SplashWnd\n");
        return 0;
    }

    SetWindowLongPtr(pThis->m_hSplashWnd, GWL_USERDATA, reinterpret_cast<LONG_PTR>(pThis));
    ShowWindow(pThis->m_hSplashWnd, SW_SHOWNOACTIVATE);

    pThis->m_pImage->InitAnimation(pThis->m_hSplashWnd, CPoint());

    MSG msg;
    BOOL bRet;
    LONG timerCount = 0;

    PeekMessage(&msg, NULL, 0, 0, 0); // invoke creating message queue
    SetEvent(pThis->m_hEvent);

    while ((bRet = GetMessage(&msg, NULL, 0, 0)) != 0)
    {
        if (msg.message == WM_QUIT) break;

        if (bRet == -1)
        {
            // handle the error and possibly exit
        }
        else
        {
            TranslateMessage(&msg);
            DispatchMessage(&msg);
        }
    }

    DestroyWindow(pThis->m_hSplashWnd);
    return 0;
}

LRESULT CALLBACK CSplashWnd::SplashWndProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
    CSplashWnd* pInstance = reinterpret_cast<CSplashWnd*>(GetWindowLongPtr(hwnd, GWL_USERDATA));
    if (pInstance == NULL)
    {
        return DefWindowProc(hwnd, uMsg, wParam, lParam);
    }

    switch (uMsg)
    {

    case WM_PAINT:
    {
        if (pInstance->m_pImage)
        {
            if (pInstance->m_pImage->IsAnimatedGIF())
            {
                // do nothing, the gif will be drawn by it's own thread.
            }
            else
            {
                Gdiplus::Graphics gdip(hwnd);
                gdip.DrawImage(pInstance->m_pImage, 0, 0, pInstance->m_pImage->GetWidth(), pInstance->m_pImage->GetHeight());
            }
        }
        ValidateRect(hwnd, NULL);
        return 0;
    }

    case WM_LBUTTONDOWN:
    {
        GetCursorPos(&pInstance->m_ptMouseDown);
        SetCapture(hwnd);
        return 0;
    }

    case WM_MOUSEMOVE:
    {
        if (GetCapture() == hwnd)
        {
            RECT rcWnd;
            GetWindowRect(hwnd, &rcWnd);

            POINT pt;
            GetCursorPos(&pt);

            POINT& ptDown = pInstance->m_ptMouseDown;
            auto xdiff = ptDown.x - pt.x;
            auto ydiff = ptDown.y - pt.y;

            SetWindowPos(hwnd, 0, rcWnd.left - xdiff, rcWnd.top - ydiff, 0, 0,
                SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOZORDER);

            pInstance->m_ptMouseDown = pt;
        }
        return 0;
    }

    case WM_LBUTTONUP:
    {
        ReleaseCapture();
        return 0;
    }

    }

    return DefWindowProc(hwnd, uMsg, wParam, lParam);
}