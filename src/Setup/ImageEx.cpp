// GDIPlusHelper.cpp: implementation of the CGDIPlusHelper class.
//
//////////////////////////////////////////////////////////////////////

#include "stdafx.h"
#include "ImageEx.h"
#include <process.h>

void TRACE(wchar_t* msg) {}
void ASSERT(void* obj) {}

// https://stackoverflow.com/a/66238748/184746
IStream* LoadImageFromResource(const wchar_t* resid, const wchar_t* restype)
{
	IStream* pStream = nullptr;
	HGLOBAL hGlobal = nullptr;

	HINSTANCE hInst = GetModuleHandle(NULL);
	HRSRC hrsrc = FindResourceW(hInst, resid, restype);     // get the handle to the resource
	if (hrsrc)
	{
		DWORD dwResourceSize = SizeofResource(hInst, hrsrc);
		if (dwResourceSize > 0)
		{
			HGLOBAL hGlobalResource = LoadResource(hInst, hrsrc); // load it
			if (hGlobalResource)
			{
				void* imagebytes = LockResource(hGlobalResource); // get a pointer to the file bytes

				// copy image bytes into a real hglobal memory handle
				hGlobal = ::GlobalAlloc(GHND, dwResourceSize);
				if (hGlobal)
				{
					void* pBuffer = ::GlobalLock(hGlobal);
					if (pBuffer)
					{
						memcpy(pBuffer, imagebytes, dwResourceSize);
						HRESULT hr = CreateStreamOnHGlobal(hGlobal, TRUE, &pStream);
						if (SUCCEEDED(hr))
						{
							// pStream now owns the global handle and will invoke GlobalFree on release
							hGlobal = nullptr;
						}
					}
				}
			}
		}
	}

	if (hGlobal)
	{
		GlobalFree(hGlobal);
		hGlobal = nullptr;
	}

	return pStream;
}

ImageEx::ImageEx(const wchar_t* resid, const wchar_t* restype)
{
	Initialize();

	auto stream = LoadImageFromResource(resid, restype);
	if (stream)
	{
		nativeImage = NULL;
		lastResult = DllExports::GdipLoadImageFromStreamICM(stream, &nativeImage);
		m_bIsInitialized = true;
		TestForAnimatedGIF();
		stream->Release();
	}
}

ImageEx::~ImageEx()
{
	Destroy();
}

bool ImageEx::InitAnimation(HWND hWnd, CPoint pt)
{

	m_hWnd = hWnd;
	m_pt = pt;

	if (!m_bIsInitialized)
	{
		TRACE(_T("GIF not initialized\n"));
		return false;
	};

	if (IsAnimatedGIF())
	{
		if (m_hThread == NULL)
		{

			unsigned int nTID = 0;

			m_hThread = (HANDLE)_beginthreadex(NULL, 0, _ThreadAnimationProc, this, CREATE_SUSPENDED, &nTID);

			if (!m_hThread)
			{
				TRACE(_T("Couldn't start a GIF animation thread\n"));
				return true;
			}
			else
				ResumeThread(m_hThread);
		}
	}

	return false;

}

CSize ImageEx::GetSize()
{
	return CSize(GetWidth(), GetHeight());
}

bool ImageEx::TestForAnimatedGIF()
{
	UINT count = 0;
	count = GetFrameDimensionsCount();
	GUID* pDimensionIDs = new GUID[count];

	// Get the list of frame dimensions from the Image object.
	GetFrameDimensionsList(pDimensionIDs, count);

	// Get the number of frames in the first dimension.
	m_nFrameCount = GetFrameCount(&pDimensionIDs[0]);

	// Assume that the image has a property item of type PropertyItemEquipMake.
	// Get the size of that property item.
	int nSize = GetPropertyItemSize(PropertyTagFrameDelay);

	// Allocate a buffer to receive the property item.
	m_pPropertyItem = (PropertyItem*)malloc(nSize);

	GetPropertyItem(PropertyTagFrameDelay, nSize, m_pPropertyItem);

	delete pDimensionIDs;

	return m_nFrameCount > 1;
}

void ImageEx::Initialize()
{
	m_nFramePosition = 0;
	m_nFrameCount = 0;
	lastResult = InvalidParameter;
	m_hThread = NULL;
	m_bIsInitialized = false;
	m_pPropertyItem = NULL;

#ifdef INDIGO_CTRL_PROJECT
	m_hInst = _Module.GetResourceInstance();
#else
	m_hInst = GetModuleHandle(NULL);// AfxGetResourceHandle();
#endif

	m_bPause = false;
	m_hExitEvent = CreateEvent(NULL, TRUE, FALSE, NULL);
	m_hPause = CreateEvent(NULL, TRUE, TRUE, NULL);
}

UINT WINAPI ImageEx::_ThreadAnimationProc(LPVOID pParam)
{
	ASSERT(pParam);
	ImageEx* pImage = reinterpret_cast<ImageEx*> (pParam);
	pImage->ThreadAnimation();

	return 0;
}

void ImageEx::ThreadAnimation()
{
	m_nFramePosition = 0;

	bool bExit = false;
	while (bExit == false)
	{
		bExit = DrawFrameGIF();
	}
}

bool ImageEx::DrawFrameGIF()
{
	::WaitForSingleObject(m_hPause, INFINITE);

	GUID pageGuid = FrameDimensionTime;

	long hmWidth = GetWidth();
	long hmHeight = GetHeight();

	HDC hDC = GetDC(m_hWnd);
	if (hDC)
	{
		Graphics graphics(hDC);
		graphics.DrawImage(this, m_pt.x, m_pt.y, hmWidth, hmHeight);
		ReleaseDC(m_hWnd, hDC);
	}

	SelectActiveFrame(&pageGuid, m_nFramePosition++);

	if (m_nFramePosition == m_nFrameCount)
		m_nFramePosition = 0;

	long lPause = ((long*)m_pPropertyItem->value)[m_nFramePosition] * 10;
	DWORD dwErr = WaitForSingleObject(m_hExitEvent, lPause);
	return dwErr == WAIT_OBJECT_0;
}

void ImageEx::SetPause(bool bPause)
{
	if (!IsAnimatedGIF())
		return;

	if (bPause && !m_bPause)
	{
		::ResetEvent(m_hPause);
	}
	else
	{

		if (m_bPause && !bPause)
		{
			::SetEvent(m_hPause);
		}
	}

	m_bPause = bPause;
}


void ImageEx::Destroy()
{

	if (m_hThread)
	{
		// If pause un pause
		SetPause(false);

		SetEvent(m_hExitEvent);
		WaitForSingleObject(m_hThread, INFINITE);
	}

	CloseHandle(m_hThread);
	CloseHandle(m_hExitEvent);
	CloseHandle(m_hPause);

	free(m_pPropertyItem);

	m_pPropertyItem = NULL;
	m_hThread = NULL;
	m_hExitEvent = NULL;
	m_hPause = NULL;
}