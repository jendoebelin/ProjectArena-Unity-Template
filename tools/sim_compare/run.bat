@echo off
rem Same-results check, native side: builds trace.cpp against the native simulation (read only) and writes the traces.
rem Usage: tools\sim_compare\run.bat [native repo, default %PA_NATIVE_REPO%, else ..\ProjectArena-public]
setlocal
set N=%~1
if "%N%"=="" set N=%PA_NATIVE_REPO%
if "%N%"=="" set N=..\ProjectArena-public
set HERE=%~dp0
set B=%HERE%..\..\Build\sim_compare
if not exist "%B%" mkdir "%B%"
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
cl /nologo /std:c++17 /O2 /EHsc /W3 /D_CRT_SECURE_NO_WARNINGS /I"%N%\app\src" /Fe"%B%\trace.exe" /Fo"%B%\\" "%HERE%trace.cpp" "%N%\app\src\game.cpp" || exit /b 1
"%B%\trace.exe" "%N%\app\assets_open\arena.col" "%B%" || exit /b 1
python -I "%HERE%pack.py" "%B%" "%HERE%traces" "%N%" || exit /b 1
