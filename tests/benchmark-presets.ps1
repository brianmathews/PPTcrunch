param(
    [string]$InputVideo,
    [string]$FFmpeg = 'ffmpeg',
    [switch]$Nvenc
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'artifacts/preset-benchmark'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskReference = Join-Path $taskOutput 'reference.mkv'
if (!$InputVideo) { throw 'Provide a representative input video via -InputVideo.' }

function Run-Encode([string[]]$Parameters, [string]$Log) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $FFmpeg @Parameters 2> $Log
    if ($LASTEXITCODE -ne 0) { throw (Get-Content -LiteralPath $Log -Raw) }
    $timer.Stop()
    return $timer.Elapsed.TotalSeconds
}

$null = Run-Encode @('-v','error','-i',$InputVideo,'-t','3','-vf','scale=1280:-2:flags=lanczos,format=yuv420p','-an','-c:v','ffv1','-y',$taskReference) (Join-Path $taskOutput 'reference.log')
$cases = @()
foreach ($codec in @('libx264','libx265')) {
    $q = if ($codec -eq 'libx264') { '22' } else { '24' }
    foreach ($preset in @('medium','slow','slower')) {
        $cases += @{ Name="$codec-$preset"; Options=@('-c:v',$codec,'-crf',$q,'-preset',$preset) }
    }
}
foreach ($speed in @('4','2','1','0')) {
    $cases += @{ Name="vp9-speed$speed"; Options=@('-c:v','libvpx-vp9','-crf','30','-b:v','0','-deadline','good','-cpu-used',$speed,'-row-mt','1','-lag-in-frames','25','-g','240') }
}
$cases += @{ Name='vp9-two-pass'; TwoPass=$true; Options=@('-c:v','libvpx-vp9','-crf','30','-b:v','0','-deadline','good','-cpu-used','2','-row-mt','1','-lag-in-frames','25','-g','240') }
if ($Nvenc) {
    foreach ($codec in @('h264_nvenc','hevc_nvenc')) {
        $q = if ($codec -eq 'h264_nvenc') { '22' } else { '24' }
        $cases += @{ Name="$codec-old-slow"; Options=@('-c:v',$codec,'-cq',$q,'-b:v','0','-rc','vbr','-preset','slow','-bf','3','-refs','3') }
        foreach ($preset in @('p6','p7')) {
            $cases += @{ Name="$codec-$preset"; Options=@('-c:v',$codec,'-cq',$q,'-b:v','0','-rc','vbr','-preset',$preset,'-tune','hq','-multipass','1','-rc-lookahead','32','-spatial-aq','1','-temporal-aq','1') }
        }
    }
}
$results = @()
foreach ($case in $cases) {
    $output = Join-Path $taskOutput ($case.Name + '.mkv')
    $log = Join-Path $taskOutput ($case.Name + '.log')
    $parameters = @('-v','error','-i',$taskReference,'-an','-pix_fmt','yuv420p') + $case.Options
    $elapsed = 0
    if ($case.TwoPass) {
        $passLog = Join-Path $taskOutput 'vp9-pass'
        $nullOutput = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'NUL' } else { '/dev/null' }
        $elapsed += Run-Encode ($parameters + @('-cpu-used','4','-pass','1','-passlogfile',$passLog,'-f','null','-y',$nullOutput)) $log
        $parameters += @('-pass','2','-passlogfile',$passLog)
    }
    $elapsed += Run-Encode ($parameters + @('-y',$output)) $log
    $metricLog = Join-Path $taskOutput ($case.Name + '-psnr.log')
    $null = Run-Encode @('-i',$output,'-i',$taskReference,'-lavfi','psnr','-an','-f','null','-') $metricLog
    $match = [regex]::Match((Get-Content -LiteralPath $metricLog -Raw),'average:([0-9.]+)')
    $result = [pscustomobject]@{ Name=$case.Name; Seconds=[math]::Round($elapsed,3); Bytes=(Get-Item -LiteralPath $output).Length; PSNR=[double]::Parse($match.Groups[1].Value,[cultureinfo]::InvariantCulture) }
    $results += $result
    $result | Format-Table -HideTableHeaders
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'results.json')
}
