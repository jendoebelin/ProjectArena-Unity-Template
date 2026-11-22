@echo off
rem Cross-play check, native side: builds peer.cpp against the native networking and simulation (read only).
rem Usage: tools\cross_play\build.bat [native repo, default %PA_NATIVE_REPO%, else ..\ProjectArena-public]
setlocal
set N=%~1
if "%N%"=="" set N=%PA_NATIVE_REPO%
if "%N%"=="" set N=..\ProjectArena-public
set HERE=%~dp0
set B=%HERE%..\..\Build\cross_play
if not exist "%B%" mkdir "%B%"
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
cl /nologo /std:c++17 /O2 /EHsc /W3 /D_CRT_SECURE_NO_WARNINGS /D_WINSOCK_DEPRECATED_NO_WARNINGS /I"%N%\app\src" /Fe"%B%\peer.exe" /Fo"%B%\\" "%HERE%peer.cpp" "%N%\app\src\net.cpp" "%N%\app\src\game.cpp" "%N%\app\src\avatar.cpp" "%N%\app\src\log.cpp" ws2_32.lib || exit /b 1
