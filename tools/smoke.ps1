[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release',
      [string]$RuntimePath,
      [string[]]$Scenarios = @('day','crowd','drive','walk','people','bus','night','storm','canal','canal-impact','bridge','overpass','underpass','portal','tunnel','facade','facade-night','park','park-unshadowed','park-evening','streaming','radio-menu','settings','save-slots','market','workshop','console','job-board','contract-series','street-hail','courier','pursuit','arrest','arrest-car','traffic-lanes','transit','tram-ride','trolley-ride','carjack','recovery','rollover','tram-recovery','tunnel-exit','tank','drone','leaflets','taxi','wildlife','scooter','delivery-rider','ambulance','hydrant','phone-booth','soda-machine','puddle','pothole','estate','courtyard','cottages','centre','garages','rail-yard','seafront','canal-mouth'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime = if ($RuntimePath) { (Resolve-Path -LiteralPath $RuntimePath).Path } else { Join-Path $root "artifacts/$Configuration" }
$exe = Join-Path $runtime 'Promzona.exe'
if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath (Join-Path $runtime 'Content'))) {
    throw "Build the runtime first: .\build.ps1 -Configuration $Configuration -Package"
}
$output = Join-Path $root "build/smoke/$Configuration"
New-Item -ItemType Directory -Path $output -Force | Out-Null
Add-Type -AssemblyName System.Drawing
foreach ($scenario in $Scenarios) {
    $directory = Join-Path $output $scenario
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $bmp = Join-Path $directory 'frame.bmp'
    foreach ($name in @('native.log','frame.bmp','frame.png')) {
        $previous = Join-Path $directory $name
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous }
    }
    $frames = if ($scenario -in 'tram-ride','trolley-ride') { 4200 } elseif ($scenario -in 'bridge','traffic-lanes','arrest','arrest-car','rollover','tram-recovery') { 480 } elseif ($scenario -eq 'leaflets') { 210 } elseif ($scenario -eq 'streaming') { 900 } else { 180 }
    $arguments = "--smoke --scenario $scenario --frames $frames --smoke-output `"$directory`" --screenshot `"$bmp`""
    $process = Start-Process -FilePath $exe -WorkingDirectory $env:TEMP -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        $timeout = if ($scenario -in 'tram-ride','trolley-ride') { 180000 } else { 45000 }
        if (-not $process.WaitForExit($timeout)) {
            $process.Kill()
            throw "Smoke timed out: $scenario"
        }
        $logFile = Join-Path $directory 'native.log'
        if (-not (Test-Path -LiteralPath $logFile)) { throw "Smoke $scenario exited before logging (exit $($process.ExitCode))." }
        $log = Get-Content -LiteralPath $logFile -Raw
        Write-Host $log.Trim()
        if ($process.ExitCode -ne 0 -or $log -notmatch 'SMOKE PASS' -or -not (Test-Path -LiteralPath $bmp)) {
            throw "Smoke failed: $scenario, exit $($process.ExitCode)"
        }
        foreach ($capture in Get-ChildItem -LiteralPath $directory -Filter '*.bmp') {
            $frame = [Drawing.Image]::FromFile($capture.FullName)
            try { $frame.Save([IO.Path]::ChangeExtension($capture.FullName, '.png'), [Drawing.Imaging.ImageFormat]::Png) }
            finally { $frame.Dispose() }
        }
    }
    finally { $process.Dispose() }
}
Write-Host "GPU smoke passed. Logs and frames: $output"
