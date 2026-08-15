@echo off
REM ============================================================
REM  Test script: "stuck equipped tag" bug reproduction run
REM  1. Closes nothing; just checks the game is not running
REM  2. Starts the game via Steam
REM  3. After you finish testing, opens the mod log
REM ============================================================
setlocal
set "GAME=E:\steam\steamapps\common\DreamEcho"
set "DOOR=%GAME%\doorstop_config.ini"

if not exist "%DOOR%" (
  echo doorstop_config.ini not found: %DOOR%
  pause
  exit /b 1
)

REM Resolve profile BepInEx root from doorstop target_assembly
for /f "tokens=1,* delims==" %%a in ('findstr /i "target_assembly" "%DOOR%"') do set "TARGET=%%b"
if not defined TARGET (
  echo target_assembly not found in doorstop_config.ini
  pause
  exit /b 1
)
set "TARGET=%TARGET: =%"
for %%i in ("%TARGET%") do set "CORE=%%~dpi"
for %%i in ("%CORE%..") do set "BEP=%%~fi"
set "LOG=%BEP%\LogOutput.log"
echo Profile BepInEx: %BEP%

tasklist /FI "IMAGENAME eq DreamEchoes.exe" 2>NUL | find /I "DreamEchoes.exe" >NUL
if %errorlevel%==0 (
  echo DreamEchoes.exe is RUNNING. Close the game first, then run this script again.
  pause
  exit /b 1
)

echo Starting DreamEcho via Steam...
start steam://rungameid/3226060

echo.
echo ============ TEST STEPS (in game) ============
echo  1. Press F10 once: repairs existing stuck tags
echo  2. Open backpack, unequip a memory, check the tag
echo  3. Equip it again, then unequip again - tag must go away
echo  4. If a tag stays: reproduce it 2-3 times, then quit the game
echo  5. Optional bisect: edit BepInEx\config\com.dreamecho.mod.cfg,
echo     set EnableDisassemble=false (or EnableT1=false), restart, retry
echo ===============================================
echo.
echo When done testing, close the game, then press any key to open the log.
pause >NUL

if exist "%LOG%" (
  start notepad "%LOG%"
  echo Log opened: %LOG%
) else (
  echo Log not found: %LOG%
)
pause
