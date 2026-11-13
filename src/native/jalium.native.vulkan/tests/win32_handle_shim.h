#pragma once
// Host-only handle ABI for fake-dispatch tests. Windows target builds use the
// actual Windows SDK and compile/run the same assertions as a native PE test.
#include <stdint.h>
#include <wchar.h>
using HANDLE=void*;
using HINSTANCE=void*;
using HWND=void*;
using HMONITOR=void*;
using LPCWSTR=const wchar_t*;
using DWORD=uint32_t;
struct SECURITY_ATTRIBUTES;
constexpr int FALSE=0;
constexpr uint32_t DUPLICATE_SAME_ACCESS=2;
inline int win32TestHandles=0;
inline uintptr_t win32TestNext=10000;
inline HANDLE GetCurrentProcess(){return reinterpret_cast<HANDLE>(uintptr_t(-1));}
inline DWORD GetCurrentProcessId(){return 77;}
inline HANDLE CreateEventW(void*,int,int,const wchar_t*){++win32TestHandles;return reinterpret_cast<HANDLE>(++win32TestNext);}
inline int DuplicateHandle(HANDLE,HANDLE source,HANDLE,HANDLE* out,DWORD,int,DWORD){
    if(!source)return 0;
    ++win32TestHandles;*out=reinterpret_cast<HANDLE>(++win32TestNext);return 1;
}
inline int CloseHandle(HANDLE handle){if(!handle)return 0;--win32TestHandles;return 1;}
