#pragma once
#include "resource.h"
#include <atlctrls.h>

class LicenseDialog : public CDialogImpl<LicenseDialog>
{
private:
   CRichEditCtrl m_licenseText;

   void LoadLicenseFromResources();
   BOOL PrintRTF( HWND hwnd, HDC hdc );

   LRESULT OnInitDialog( UINT, WPARAM, LPARAM, BOOL& );   
   LRESULT OnContinue( WORD wNotifyCode, WORD wID, HWND hWndCtl, BOOL& bHandled );   
   LRESULT OnAccept( WORD wNotifyCode, WORD wID, HWND hWndCtl, BOOL& bHandled );
   LRESULT OnDecline( WORD wNotifyCode, WORD wID, HWND hWndCtl, BOOL& bHandled );
   LRESULT OnPrint( WORD wNotifyCode, WORD wID, HWND hWndCtl, BOOL& bHandled );
   LRESULT OnClose( UINT, WPARAM, LPARAM, BOOL& );

   BEGIN_MSG_MAP( LicenseDialog )
      MESSAGE_HANDLER( WM_INITDIALOG, OnInitDialog )
      MESSAGE_HANDLER( WM_CLOSE, OnClose )
      COMMAND_ID_HANDLER( IDC_CONTINUE, OnContinue )
      COMMAND_ID_HANDLER( IDC_ACCEPT, OnAccept )
      COMMAND_HANDLER( IDC_DECLINE, BN_CLICKED, OnDecline )
      COMMAND_ID_HANDLER( IDC_PRINT, OnPrint )
   END_MSG_MAP()
   
public:
   enum { IDD = IDD_LICENSE };

   bool AcceptLicense();
   bool ShouldShowLicense();
};
