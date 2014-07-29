#ifndef _unzip_H
#define _unzip_H

// UNZIPPING functions -- for unzipping.
// This file is a repackaged form of extracts from the zlib code available
// at www.gzip.org/zlib, by Jean-Loup Gailly and Mark Adler. The original
// copyright notice may be found in unzip.cpp. The repackaging was done
// by Lucian Wischik to simplify and extend its use in Windows/C++. Also
// encryption and unicode filenames have been added.


#ifndef _zip_H
DECLARE_HANDLE(HZIP);
#endif
// An HZIP identifies a zip file that has been opened

typedef DWORD ZRESULT;
// return codes from any of the zip functions. Listed later.

typedef struct
{ int index;                 // index of this file within the zip
  TCHAR name[MAX_PATH];      // filename within the zip
  DWORD attr;                // attributes, as in GetFileAttributes.
  FILETIME atime,ctime,mtime;// access, create, modify filetimes
  long comp_size;            // sizes of item, compressed and uncompressed. These
  long unc_size;             // may be -1 if not yet known (e.g. being streamed in)
} ZIPENTRY;


HZIP OpenZip(const TCHAR *fn, const char *password);
HZIP OpenZip(void *z,unsigned int len, const char *password);
HZIP OpenZipHandle(HANDLE h, const char *password);
// OpenZip - opens a zip file and returns a handle with which you can
// subsequently examine its contents. You can open a zip file from:
// from a pipe:             OpenZipHandle(hpipe_read,0);
// from a file (by handle): OpenZipHandle(hfile,0);
// from a file (by name):   OpenZip("c:\\test.zip","password");
// from a memory block:     OpenZip(bufstart, buflen,0);
// If the file is opened through a pipe, then items may only be
// accessed in increasing order, and an item may only be unzipped once,
// although GetZipItem can be called immediately before and after unzipping
// it. If it's opened in any other way, then full random access is possible.
// Note: pipe input is not yet implemented.
// Note: zip passwords are ascii, not unicode.
// Note: for windows-ce, you cannot close the handle until after CloseZip.
// but for real windows, the zip makes its own copy of your handle, so you
// can close yours anytime.

ZRESULT GetZipItem(HZIP hz, int index, ZIPENTRY *ze);
// GetZipItem - call this to get information about an item in the zip.
// If index is -1 and the file wasn't opened through a pipe,
// then it returns information about the whole zipfile
// (and in particular ze.index returns the number of index items).
// Note: the item might be a directory (ze.attr & FILE_ATTRIBUTE_DIRECTORY)
// See below for notes on what happens when you unzip such an item.
// Note: if you are opening the zip through a pipe, then random access
// is not possible and GetZipItem(-1) fails and you can't discover the number
// of items except by calling GetZipItem on each one of them in turn,
// starting at 0, until eventually the call fails. Also, in the event that
// you are opening through a pipe and the zip was itself created into a pipe,
// then then comp_size and sometimes unc_size as well may not be known until
// after the item has been unzipped.

ZRESULT FindZipItem(HZIP hz, const TCHAR *name, bool ic, int *index, ZIPENTRY *ze);
// FindZipItem - finds an item by name. ic means 'insensitive to case'.
// It returns the index of the item, and returns information about it.
// If nothing was found, then index is set to -1 and the function returns
// an error code.

ZRESULT UnzipItem(HZIP hz, int index, const TCHAR *fn);
ZRESULT UnzipItem(HZIP hz, int index, void *z,unsigned int len);
ZRESULT UnzipItemHandle(HZIP hz, int index, HANDLE h);
// UnzipItem - given an index to an item, unzips it. You can unzip to:
// to a pipe:             UnzipItemHandle(hz,i, hpipe_write);
// to a file (by handle): UnzipItemHandle(hz,i, hfile);
// to a file (by name):   UnzipItem(hz,i, ze.name);
// to a memory block:     UnzipItem(hz,i, buf,buflen);
// In the final case, if the buffer isn't large enough to hold it all,
// then the return code indicates that more is yet to come. If it was
// large enough, and you want to know precisely how big, GetZipItem.
// Note: zip files are normally stored with relative pathnames. If you
// unzip with ZIP_FILENAME a relative pathname then the item gets created
// relative to the current directory - it first ensures that all necessary
// subdirectories have been created. Also, the item may itself be a directory.
// If you unzip a directory with ZIP_FILENAME, then the directory gets created.
// If you unzip it to a handle or a memory block, then nothing gets created
// and it emits 0 bytes.
ZRESULT SetUnzipBaseDir(HZIP hz, const TCHAR *dir);
// if unzipping to a filename, and it's a relative filename, then it will be relative to here.
// (defaults to current-directory).


ZRESULT CloseZip(HZIP hz);
// CloseZip - the zip handle must be closed with this function.

unsigned int FormatZipMessage(ZRESULT code, TCHAR *buf,unsigned int len);
// FormatZipMessage - given an error code, formats it as a string.
// It returns the length of the error message. If buf/len points
// to a real buffer, then it also writes as much as possible into there.


// These are the result codes:
#define ZR_OK         0x00000000     // nb. the pseudo-code zr-recent is never returned,
#define ZR_RECENT     0x00000001     // but can be passed to FormatZipMessage.
// The following come from general system stuff (e.g. files not openable)
#define ZR_GENMASK    0x0000FF00
#define ZR_NODUPH     0x00000100     // couldn't duplicate the handle
#define ZR_NOFILE     0x00000200     // couldn't create/open the file
#define ZR_NOALLOC    0x00000300     // failed to allocate some resource
#define ZR_WRITE      0x00000400     // a general error writing to the file
#define ZR_NOTFOUND   0x00000500     // couldn't find that file in the zip
#define ZR_MORE       0x00000600     // there's still more data to be unzipped
#define ZR_CORRUPT    0x00000700     // the zipfile is corrupt or not a zipfile
#define ZR_READ       0x00000800     // a general error reading the file
#define ZR_PASSWORD   0x00001000     // we didn't get the right password to unzip the file
// The following come from mistakes on the part of the caller
#define ZR_CALLERMASK 0x00FF0000
#define ZR_ARGS       0x00010000     // general mistake with the arguments
#define ZR_NOTMMAP    0x00020000     // tried to ZipGetMemory, but that only works on mmap zipfiles, which yours wasn't
#define ZR_MEMSIZE    0x00030000     // the memory size is too small
#define ZR_FAILED     0x00040000     // the thing was already failed when you called this function
#define ZR_ENDED      0x00050000     // the zip creation has already been closed
#define ZR_MISSIZE    0x00060000     // the indicated input file size turned out mistaken
#define ZR_PARTIALUNZ 0x00070000     // the file had already been partially unzipped
#define ZR_ZMODE      0x00080000     // tried to mix creating/opening a zip
// The following come from bugs within the zip library itself
#define ZR_BUGMASK    0xFF000000
#define ZR_NOTINITED  0x01000000     // initialisation didn't work
#define ZR_SEEK       0x02000000     // trying to seek in an unseekable file
#define ZR_NOCHANGE   0x04000000     // changed its mind on storage, but not allowed
#define ZR_FLATE      0x05000000     // an internal error in the de/inflation code





// e.g.
//
// SetCurrentDirectory("c:\\docs\\stuff");
// HZIP hz = OpenZip("c:\\stuff.zip",0);
// ZIPENTRY ze; GetZipItem(hz,-1,&ze); int numitems=ze.index;
// for (int i=0; i<numitems; i++)
// { GetZipItem(hz,i,&ze);
//   UnzipItem(hz,i,ze.name);
// }
// CloseZip(hz);
//
//
// HRSRC hrsrc = FindResource(hInstance,MAKEINTRESOURCE(1),RT_RCDATA);
// HANDLE hglob = LoadResource(hInstance,hrsrc);
// void *zipbuf=LockResource(hglob);
// unsigned int ziplen=SizeofResource(hInstance,hrsrc);
// HZIP hz = OpenZip(zipbuf, ziplen, 0);
//   - unzip to a membuffer -
// ZIPENTRY ze; int i; FindZipItem(hz,"file.dat",true,&i,&ze);
// char *ibuf = new char[ze.unc_size];
// UnzipItem(hz,i, ibuf, ze.unc_size);
// delete[] ibuf;
//   - unzip to a fixed membuff -
// ZIPENTRY ze; int i; FindZipItem(hz,"file.dat",true,&i,&ze);
// char ibuf[1024]; ZRESULT zr=ZR_MORE; unsigned long totsize=0;
// while (zr==ZR_MORE)
// { zr = UnzipItem(hz,i, ibuf,1024);
//   unsigned long bufsize=1024; if (zr==ZR_OK) bufsize=ze.unc_size-totsize;
//   totsize+=bufsize;
// }
//   - unzip to a pipe -
// HANDLE hwrite; HANDLE hthread=CreateWavReaderThread(&hwrite);
// int i; ZIPENTRY ze; FindZipItem(hz,"sound.wav",true,&i,&ze);
// UnzipItemHandle(hz,i, hwrite);
// CloseHandle(hwrite);
// WaitForSingleObject(hthread,INFINITE);
// CloseHandle(hwrite); CloseHandle(hthread);
//   - finished -
// CloseZip(hz);
// // note: no need to free resources obtained through Find/Load/LockResource
//
//
// SetCurrentDirectory("c:\\docs\\pipedzipstuff");
// HANDLE hread,hwrite; CreatePipe(&hread,&hwrite,0,0);
// CreateZipWriterThread(hwrite);
// HZIP hz = OpenZipHandle(hread,0);
// for (int i=0; ; i++)
// { ZIPENTRY ze;
//   ZRESULT zr=GetZipItem(hz,i,&ze); if (zr!=ZR_OK) break; // no more
//   UnzipItem(hz,i, ze.name);
// }
// CloseZip(hz);
//
//




// Now we indulge in a little skullduggery so that the code works whether
// the user has included just zip or both zip and unzip.
// Idea: if header files for both zip and unzip are present, then presumably
// the cpp files for zip and unzip are both present, so we will call
// one or the other of them based on a dynamic choice. If the header file
// for only one is present, then we will bind to that particular one.
ZRESULT CloseZipU(HZIP hz);
unsigned int FormatZipMessageU(ZRESULT code, TCHAR *buf,unsigned int len);
bool IsZipHandleU(HZIP hz);
#ifdef _zip_H
#undef CloseZip
#define CloseZip(hz) (IsZipHandleU(hz)?CloseZipU(hz):CloseZipZ(hz))
#else
#define CloseZip CloseZipU
#define FormatZipMessage FormatZipMessageU
#endif



#endif // _unzip_H
