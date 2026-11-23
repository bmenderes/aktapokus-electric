@echo off
setlocal
chcp 65001 >nul

rem Uygulama klasoru sirayla aranir: betigin yanindaki app (dist\setup.bat), depo kokundeki dist\app (tools\setup.bat),
rem ust klasorde aktapokus-electric\dist\app (eski yerlesim).
set "SRC_DIR=%~dp0app"
if not exist "%SRC_DIR%\Ake.Editor.exe" for %%I in ("%~dp0..\dist\app") do if exist "%%~fI\Ake.Editor.exe" set "SRC_DIR=%%~fI"
if not exist "%SRC_DIR%\Ake.Editor.exe" (
    if exist "%~dp0aktapokus-electric\dist\app\Ake.Editor.exe" (
        set "SRC_DIR=%~dp0aktapokus-electric\dist\app"
    )
)

if not exist "%SRC_DIR%\Ake.Editor.exe" (
    echo HATA: Ake.Editor.exe bulunamadi ^(aranan: "%SRC_DIR%"^).
    echo       Once uygulamayi derleyin: powershell -ExecutionPolicy Bypass -File tools\build-setup.ps1
    echo       Sonra dist\setup.bat ya da dist\aktapokus-electric-setup.exe dosyasini calistirin.
    if not "%AKE_NO_PAUSE%"=="1" pause
    exit /b 1
)

if not defined AKE_INSTALL_DIR set "AKE_INSTALL_DIR=%LOCALAPPDATA%\Programs\aktapokus-electric"
set "INSTALL_DIR=%AKE_INSTALL_DIR%"

echo aktapokus-electric kuruluyor...
echo   Kaynak : "%SRC_DIR%"
echo   Hedef  : "%INSTALL_DIR%"

if not "%AKE_SKIP_KILL%"=="1" (
    taskkill /F /IM Ake.Editor.exe >nul 2>&1
    taskkill /F /IM ake.exe >nul 2>&1
    ping -n 2 127.0.0.1 >nul
)

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
if errorlevel 1 (
    echo HATA: Kurulum klasoru olusturulamadi: "%INSTALL_DIR%"
    if not "%AKE_NO_PAUSE%"=="1" pause
    exit /b 1
)

copy /Y "%SRC_DIR%\Ake.Editor.exe" "%INSTALL_DIR%\Ake.Editor.exe" >nul
if errorlevel 1 (
    echo HATA: Ake.Editor.exe kopyalanamadi. Acik pencereleri kapatip tekrar deneyin.
    if not "%AKE_NO_PAUSE%"=="1" pause
    exit /b 1
)

copy /Y "%SRC_DIR%\ake.exe" "%INSTALL_DIR%\ake.exe" >nul
if errorlevel 1 (
    echo HATA: ake.exe kopyalanamadi. Acik CLI islemlerini kapatip tekrar deneyin.
    if not "%AKE_NO_PAUSE%"=="1" pause
    exit /b 1
)

if exist "%SRC_DIR%\LICENSE" copy /Y "%SRC_DIR%\LICENSE" "%INSTALL_DIR%\LICENSE" >nul
if exist "%SRC_DIR%\yapay-zeka.md" copy /Y "%SRC_DIR%\yapay-zeka.md" "%INSTALL_DIR%\yapay-zeka.md" >nul
if exist "%SRC_DIR%\yardim.html" copy /Y "%SRC_DIR%\yardim.html" "%INSTALL_DIR%\yardim.html" >nul

icacls "%INSTALL_DIR%\Ake.Editor.exe" /setintegritylevel Medium >nul 2>&1
icacls "%INSTALL_DIR%\ake.exe" /setintegritylevel Medium >nul 2>&1

"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $dir=$env:AKE_INSTALL_DIR; $exe=Join-Path $dir 'Ake.Editor.exe'; $cli=Join-Path $dir 'ake.exe'; Unblock-File -Path $exe -ErrorAction SilentlyContinue; Unblock-File -Path $cli -ErrorAction SilentlyContinue; $desktop=if($env:AKE_DESKTOP_DIR){$env:AKE_DESKTOP_DIR}else{[Environment]::GetFolderPath('Desktop')}; $startMenu=if($env:AKE_STARTMENU_DIR){$env:AKE_STARTMENU_DIR}else{Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft\Windows\Start Menu\Programs'}; New-Item -ItemType Directory -Force -Path $desktop,$startMenu | Out-Null; $old=Join-Path $desktop 'Ake.Editor.exe - Shortcut.lnk'; if(Test-Path $old){Remove-Item $old -Force -ErrorAction SilentlyContinue}; $ws=New-Object -ComObject WScript.Shell; foreach($lnk in @((Join-Path $desktop 'aktapokus-electric.lnk'),(Join-Path $startMenu 'aktapokus-electric.lnk'))){$s=$ws.CreateShortcut($lnk); $s.TargetPath=$exe; $s.WorkingDirectory=$dir; $s.IconLocation=$exe+',0'; $s.Description='aktapokus-electric Elektrik Projelendirme'; $s.Save()}; if($env:AKE_SKIP_PATH -ne '1'){$cur=[Environment]::GetEnvironmentVariable('PATH','User'); $parts=@(); if($cur){$parts=$cur -split ';'|Where-Object{$_ -ne ''}}; if(-not ($parts|Where-Object{$_.TrimEnd('\') -ieq $dir.TrimEnd('\')})){$newPath=if($cur -and -not $cur.EndsWith(';')){$cur+';'+$dir}elseif($cur){$cur+$dir}else{$dir}; [Environment]::SetEnvironmentVariable('PATH',$newPath,'User')}}; $desktopLnk=Join-Path $desktop 'aktapokus-electric.lnk'; if(-not (Test-Path $exe)){throw 'Hedef Ake.Editor.exe bulunamadi.'}; if(-not (Test-Path $desktopLnk)){throw 'Masaustu kisayolu olusturulamadi.'}; Write-Host ('  Kuruldu  : ' + $exe); Write-Host ('  Kisayol  : ' + $desktopLnk)"
if errorlevel 1 (
    echo.
    echo HATA: Kisayol veya kurulum yapilandirmasi tamamlanamadi.
    if not "%AKE_NO_PAUSE%"=="1" pause
    exit /b 1
)

echo.
echo Kurulum basariyla tamamlandi! Masaustunde "aktapokus-electric" simgesi olusturuldu.
if not "%AKE_NO_PAUSE%"=="1" pause
exit /b 0
