/*! file SplashWnd.h
\brief Класс, реализующий splash-окно в отдельном потоке. Без MFC.

Created 2006-06-08 by Kirill V. Lyadvinsky

* The contents of this file are subject to the terms of the Common Development and
* Distribution License ("CDDL")(collectively, the "License"). You may not use this
* file except in compliance with the License. You can obtain a copy of the CDDL at
* http://www.opensource.org/licenses/cddl1.php.

*/
#ifndef __SPLASHWND_H_
#define __SPLASHWND_H_
#include <GdiPlus.h>

class CSplashWnd
{

private:
    CSplashWnd(const CSplashWnd&) {};
    CSplashWnd& operator=(const CSplashWnd&) {};
protected:
    HANDLE			m_hThread;
    unsigned int    m_ThreadId;
    HANDLE			m_hEvent;
    Gdiplus::Image* m_pImage;
    HWND		    m_hSplashWnd;
    std::wstring	m_WindowName;
    HWND			m_hProgressWnd;
    HWND			m_hParentWnd;
    std::wstring	m_ProgressMsg;
    UINT_PTR        m_TimerId;

public:
    CSplashWnd(HWND hParent = NULL);
    ~CSplashWnd();
    void SetImage(Gdiplus::Image* pImage);
    void SetWindowName(const wchar_t* windowName);
    void Show();
    void Hide();
    void SetProgress(UINT procent);
    void SetProgress(UINT procent, const wchar_t* msg);
    void SetProgress(UINT procent, UINT nResourceID = 0, HMODULE hModule = NULL);
    void SetAutoProgress(UINT from, UINT to, UINT steps);
    void SetProgressBarColor(COLORREF color);

    HWND GetWindowHwnd() const
    {
        return m_hSplashWnd;
    };

protected:
    static LRESULT CALLBACK SplashWndProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);
    static unsigned int __stdcall SplashThreadProc(void* lpParameter);

};

#endif //__SPLASHWND_H_
