@echo off
setlocal EnableExtensions EnableDelayedExpansion

:: -----------------------------------------------
:: Config / temp files
:: -----------------------------------------------
set "ENVFILE=%TEMP%\ffcap_env_%RANDOM%.cmd"
set "PS1=%TEMP%\ffcap_helper_%RANDOM%.ps1"

:: Clean up on exit
set "_CLEANUP=1"
for %%A in ("%ENVFILE%" "%PS1%") do if exist "%%~A" del /q "%%~A" 2>nul

:: -----------------------------------------------
:: Create helper PowerShell script (for parsing & prompts)
:: -----------------------------------------------
(
  echo param(
  echo   [string]$Step,
  echo   [string]$SelectedDevice = "",
  echo   [string]$VideoSize = "",
  echo   [string]$FrameRate = ""
  echo ^)
  echo
  echo $ErrorActionPreference = 'Stop'
  echo function Run-FFMpeg(^[string[]^] $Args) ^{
  echo   $psi = New-Object System.Diagnostics.ProcessStartInfo
  echo   $psi.FileName = 'ffmpeg'
  echo   $psi.Arguments = ($Args -join ' ')
  echo   $psi.UseShellExecute = $false
  echo   $psi.RedirectStandardError = $true
  echo   $psi.RedirectStandardOutput = $true
  echo   $p = [System.Diagnostics.Process]::Start($psi)
  echo   $out = $p.StandardOutput.ReadToEnd(^)
  echo   $err = $p.StandardError.ReadToEnd(^)
  echo   $p.WaitForExit(^)
  echo   return @{Out=$out; Err=$err; Code=$p.ExitCode}
  echo ^}
  echo
  echo if ($Step -eq 'devices') ^{
  echo   $r = Run-FFMpeg @('-hide_banner','-f','dshow','-list_devices','true','-i','dummy')
  echo   # Pull lines that look like:  [dshow @ ...] "DEVICE NAME" (video)
  echo   $names = @()
  echo   foreach ($line in ($r.Err -split "`r?`n")) ^{
  echo     if ($line -match '^\s*\[.*\]\s*"(.+)"\s+\(video\)') ^{
  echo       $names += $matches[1]
  echo     }
  echo   }
  echo   if (-not $names) ^{
  echo     Write-Host "No DirectShow video devices found." -ForegroundColor Red
  echo     exit 1
  echo   }
  echo   # Present menu
  echo   Write-Host ""
  echo   Write-Host "Available video capture devices:" -ForegroundColor Cyan
  echo   $defaultIndex = $null
  echo   for ($i=0; $i -lt $names.Count; $i++) ^{
  echo     $n = $names[$i]
  echo     $mark = ""
  echo     if ($n -eq 'USB Video' -and -not $defaultIndex) { $defaultIndex = $i }
  echo     Write-Host ("  [{0}] {1}" -f $i, $n)
  echo   }
  echo   if ($null -ne $defaultIndex) ^{
  echo     Write-Host ("Default: [{0}] USB Video" -f $defaultIndex) -ForegroundColor DarkGray
  echo   } else {
  echo     $defaultIndex = 0
  echo     Write-Host ("Default: [{0}] {1}" -f $defaultIndex, $names[0]) -ForegroundColor DarkGray
  echo   }
  echo   $choice = Read-Host "Select device index (ENTER for default)"
  echo   if ([string]::IsNullOrWhiteSpace($choice)) { $choice = $defaultIndex }
  echo   if ($choice -notmatch '^\d+$' -or [int]$choice -ge $names.Count) ^{
  echo     Write-Host "Invalid selection." -ForegroundColor Red
  echo     exit 1
  echo   }
  echo   $sel = $names[[int]$choice]
  echo   # Write env for batch
  echo   'set "CAM_NAME={0}"' -f $sel | Out-File -Encoding ASCII -FilePath "%ENVFILE%"
  echo   exit 0
  echo ^}
  echo
  echo if ($Step -eq 'modes') ^{
  echo   if ([string]::IsNullOrWhiteSpace($SelectedDevice)) ^{
  echo     Write-Host "Missing SelectedDevice." -ForegroundColor Red; exit 1
  echo   }
  echo   # Query modes
  echo   $r = Run-FFMpeg @('-hide_banner','-f','dshow','-list_options','true','-i',('video="'+$SelectedDevice+'"'))
  echo   $lines = $r.Err -split "`r?`n"
  echo   # Parse lines like:
  echo   #   vcodec=mjpeg  min s=1920x1080 fps=10 max s=1920x1080 fps=60.0002
  echo   #   pixel_format=yuyv422  min s=1920x1080 fps=5 max s=1920x1080 fps=5
  echo   $rx = '^(?:\s*\[.*\]\s*)?(?<key>vcodec|pixel_format)=(?<fmt>\S+)\s+min s=(?<w>\d+)x(?<h>\d+)\s+fps=(?<min>[\d\.]+)\s+max s=\k<w>x\k<h>\s+fps=(?<max>[\d\.]+)'
  echo   $entries = @()
  echo   foreach ($line in $lines) ^{
  echo     if ($line -match $rx) ^{
  echo       $obj = [PSCustomObject]@{
  echo         Kind  = $matches['key']            # vcodec / pixel_format
  echo         Fmt   = $matches['fmt']            # mjpeg / yuyv422 / etc.
  echo         W     = [int]$matches['w']
  echo         H     = [int]$matches['h']
  echo         MinF  = [double]$matches['min']
  echo         MaxF  = [double]$matches['max']
  echo       }
  echo       $entries += $obj
  echo     }
  echo   }
  echo   if (-not $entries) ^{
  echo     Write-Host "No modes parsed from ffmpeg output." -ForegroundColor Red
  echo     exit 1
  echo   }
  echo   # Deduplicate on (Fmt,W,H,MinF,MaxF)
  echo   $uniq = $entries ^| Group-Object Fmt,W,H,MinF,MaxF ^| ForEach-Object { $_.Group[0] }
  echo
  echo   # Build human options list with discrete fps we care about
  echo   $CandidateFps = @(60,50,30,25,20,15,10,5)
  echo   $opts = @()
  echo   foreach ($e in $uniq ^| Sort-Object H,W,^[Fmt^]) ^{
  echo     foreach ($fps in $CandidateFps) ^{
  echo       if ($fps -ge [math]::Floor($e.MinF) -and $fps -le [math]::Ceiling($e.MaxF)) ^{
  echo         $opts += [PSCustomObject]@{
  echo           Label = "{0}x{1}@{2} {3}" -f $e.W,$e.H,$fps,$e.Fmt
  echo           W     = $e.W
  echo           H     = $e.H
  echo           FPS   = $fps
  echo           Fmt   = $e.Fmt
  echo         }
  echo       }
  echo     }
  echo   }
  echo   # Fallback: if nothing matched discrete list (shouldn't happen), add raw ranges
  echo   if (-not $opts) ^{
  echo     foreach ($e in $uniq) ^{
  echo       $fps = [math]::Min([math]::Max(30, [int][math]::Round($e.MinF))), [int][math]::Round($e.MaxF))
  echo       $opts += [PSCustomObject]@{
  echo         Label = "{0}x{1}@{2} {3}" -f $e.W,$e.H,$fps,$e.Fmt
  echo         W=$e.W; H=$e.H; FPS=$fps; Fmt=$e.Fmt
  echo       }
  echo     }
  echo   }
  echo   # Deduplicate identical labels
  echo   $opts = $opts ^| Group-Object Label ^| ForEach-Object { $_.Group[0] }
  echo
  echo   # Display menu
  echo   Write-Host ""
  echo   Write-Host "Available modes for '$SelectedDevice':" -ForegroundColor Cyan
  echo   for ($i=0; $i -lt $opts.Count; $i++) ^{
  echo     Write-Host ("  [{0}] {1}" -f $i, $opts[$i].Label)
  echo   }
  echo   # Default to 1920x1080@30 mjpeg if present; else first item
  echo   $defaultIndex = $null
  echo   for ($i=0; $i -lt $opts.Count; $i++) ^{
  echo     if ($opts[$i].Label -ieq '1920x1080@30 mjpeg') { $defaultIndex = $i; break }
  echo   }
  echo   if ($null -eq $defaultIndex) { $defaultIndex = 0 }
  echo   Write-Host ("Default: [{0}] {1}" -f $defaultIndex, $opts[$defaultIndex].Label) -ForegroundColor DarkGray
  echo   $choice = Read-Host "Select mode index (ENTER for default)"
  echo   if ([string]::IsNullOrWhiteSpace($choice)) { $choice = $defaultIndex }
  echo   if ($choice -notmatch '^\d+$' -or [int]$choice -ge $opts.Count) ^{
  echo     Write-Host "Invalid selection." -ForegroundColor Red; exit 1
  echo   }
  echo   $sel = $opts[[int]$choice]
  echo   'set "VIDEO_SIZE={0}x{1}"' -f $sel.W,$sel.H | Out-File -Encoding ASCII -FilePath "%ENVFILE%" -Append
  echo   'set "FRAMERATE={0}"' -f $sel.FPS | Out-File -Encoding ASCII -FilePath "%ENVFILE%" -Append
  echo   'set "INPUT_CODEC={0}"' -f $sel.Fmt | Out-File -Encoding ASCII -FilePath "%ENVFILE%" -Append
  echo   'set "MODE_LABEL={0}"' -f $sel.Label | Out-File -Encoding ASCII -FilePath "%ENVFILE%" -Append
  echo   exit 0
  echo ^}
  echo
  echo if ($Step -eq 'outfile') ^{
  echo   if ([string]::IsNullOrWhiteSpace($VideoSize) -or [string]::IsNullOrWhiteSpace($FrameRate)) ^{
  echo     Write-Host "Missing size/fps." -ForegroundColor Red; exit 1
  echo   }
  echo   $ts = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
  echo   $def = "{0}_{1}@{2}.mkv" -f $ts,$VideoSize,$FrameRate
  echo   Write-Host ""
  echo   Write-Host ("Suggested filename: {0}" -f $def) -ForegroundColor DarkGray
  echo   $name = Read-Host "Output filename (ENTER to accept)"
  echo   if ([string]::IsNullOrWhiteSpace($name)) { $name = $def }
  echo   # Ensure .mkv extension if none given
  echo   if (-not ([System.IO.Path]::GetExtension($name))) { $name = $name + ".mkv" }
  echo   'set "OUTFILE={0}"' -f $name | Out-File -Encoding ASCII -FilePath "%ENVFILE%" -Append
  echo   exit 0
  echo ^}
)>"%PS1%"

:: -----------------------------------------------
:: STEP 1: Select device
:: -----------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -Step devices
if errorlevel 1 goto :error
call "%ENVFILE%"
echo Selected device: "%CAM_NAME%"

:: -----------------------------------------------
:: STEP 2: Select mode (resolution + fps + codec)
:: -----------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -Step modes -SelectedDevice "%CAM_NAME%"
if errorlevel 1 goto :error
call "%ENVFILE%"
echo Selected mode : %MODE_LABEL%

:: -----------------------------------------------
:: STEP 3: Prompt for output filename
:: -----------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" -Step outfile -VideoSize "%VIDEO_SIZE%" -FrameRate "%FRAMERATE%"
if errorlevel 1 goto :error
call "%ENVFILE%"
echo Output file   : "%OUTFILE%"

:: -----------------------------------------------
:: STEP 4: Build ffmpeg command and start recording
:: -----------------------------------------------
echo.
echo ==============================================================
echo Press ^"q^" in the ffmpeg window to stop the recording cleanly.
echo ==============================================================

:: Input options (must be before -i)
set "DSHOW_IN=-hide_banner -f dshow -rtbufsize 512M -video_size %VIDEO_SIZE% -framerate %FRAMERATE% -vcodec %INPUT_CODEC% -i video=""%CAM_NAME%"""

:: Output options depend on input codec
if /I "%INPUT_CODEC%"=="mjpeg" (
  set "VOPTS=-c:v copy -fps_mode passthrough"
) else (
  :: For uncompressed modes (e.g., yuyv422), record losslessly with FFV1
  set "VOPTS=-pix_fmt yuv422p -c:v ffv1 -level 3 -g 1"
)

echo.
echo Running:
echo ffmpeg %DSHOW_IN% %VOPTS% "%OUTFILE%"
echo.

ffmpeg %DSHOW_IN% %VOPTS% "%OUTFILE%"
set "ERR=%ERRORLEVEL%"

goto :cleanup

:error
echo.
echo Failed. Please check that ffmpeg is installed and accessible in PATH.
echo.
set "ERR=1"

:cleanup
if exist "%ENVFILE%" del /q "%ENVFILE%" 2>nul
if exist "%PS1%" del /q "%PS1%" 2>nul
exit /b %ERR%
