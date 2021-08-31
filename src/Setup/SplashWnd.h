/*! file SplashWnd.h
\brief Класс, реализующий splash-окно в отдельном потоке. Без MFC.

Created 2006-06-08 by Kirill V. Lyadvinsky
Modified:
2021-08-31 Caelan Sayler - Add support for animated GIF's

* The contents of this file are subject to the terms of the Common Development and
* Distribution License ("CDDL")(collectively, the "License"). You may not use this
* file except in compliance with the License. You can obtain a copy of the CDDL at
* http://www.opensource.org/licenses/cddl1.php.

*/
#ifndef __SPLASHWND_H_
#define __SPLASHWND_H_
#include <GdiPlus.h>
#include "ImageEx.h"

class CSplashWnd
{

private:
    CSplashWnd(const CSplashWnd&) {};
    CSplashWnd& operator=(const CSplashWnd&) {};

protected:
    HANDLE			m_hThread;
    unsigned int    m_ThreadId;
    HANDLE			m_hEvent;
    ImageEx*        m_pImage;
    HWND		    m_hSplashWnd;
    HWND			m_hParentWnd;

public:
    CSplashWnd(HWND hParent = NULL);
    ~CSplashWnd();
    void SetImage(const wchar_t* resid, const wchar_t* restype);
    void SetWindowName(const wchar_t* windowName);
    void Show();
    void Hide();

    HWND GetWindowHwnd() const
    {
        return m_hSplashWnd;
    };

protected:
    static LRESULT CALLBACK SplashWndProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);
    static unsigned int __stdcall SplashThreadProc(void* lpParameter);

};

#endif //__SPLASHWND_H_
