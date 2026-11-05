[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime = Join-Path $root "artifacts/$Configuration"
$exe = Join-Path $runtime 'Promzona.exe'
if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath (Join-Path $runtime 'Content'))) {
    throw "Build the runtime first: .\build.ps1 -Configuration $Configuration -Package"
}
$output = Join-Path $root "build/smoke/$Configuration"
New-Item -ItemType Directory -Path $output -Force | Out-Null
Add-Type -AssemblyName System.Drawing
foreach ($scenario in @('day','drive','walk','night','canal','bridge','overpass','underpass','portal','tunnel')) {
    $directory = Join-Path $output $scenario
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $bmp = Join-Path $directory 'frame.bmp'
    foreach ($name in @('native.log','frame.bmp','frame.png')) {
        $previous = Join-Path $directory $name
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous }
    }
    $frames = if ($scenario -eq 'bridge') { 480 } else { 180 }
    $arguments = "--smoke --scenario $scenario --frames $frames --smoke-output `"$directory`" --screenshot `"$bmp`""
    $process = Start-Process -FilePath $exe -WorkingDirectory $env:TEMP -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(45000)) {
            $process.Kill()
            throw "Smoke timed out: $scenario"
        }
        $log = Get-Content -LiteralPath (Join-Path $directory 'native.log') -Raw
        Write-Host $log.Trim()
        if ($process.ExitCode -ne 0 -or $log -notmatch 'SMOKE PASS' -or -not (Test-Path -LiteralPath $bmp)) {
            throw "Smoke failed: $scenario, exit $($process.ExitCode)"
        }
        $frame = [Drawing.Image]::FromFile($bmp)
        try { $frame.Save((Join-Path $directory 'frame.png'), [Drawing.Imaging.ImageFormat]::Png) }
        finally { $frame.Dispose() }
    }
    finally { $process.Dispose() }
}
Write-Host "GPU smoke passed. Logs and frames: $output"
