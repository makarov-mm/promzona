# CMake records absolute paths for the source tree, build tree and compiler.
function Get-CMakeCacheArguments([string]$BuildDirectory, [string]$SourceDirectory) {
    $cache = Join-Path $BuildDirectory 'CMakeCache.txt'
    if (-not (Test-Path -LiteralPath $cache)) { return }
    foreach ($line in [IO.File]::ReadAllLines($cache)) {
        if ($line -match '^CMAKE_(HOME_DIRECTORY|CACHEFILE_DIR):INTERNAL=(.+)$') {
            $expected = if ($Matches[1] -eq 'HOME_DIRECTORY') { $SourceDirectory } else { $BuildDirectory }
            if ([IO.Path]::GetFullPath($Matches[2]) -eq [IO.Path]::GetFullPath($expected)) { continue }
        } elseif ($line -match '^CMAKE_(C|CXX)_COMPILER:FILEPATH=(.+)$') {
            # Servicing Visual Studio can remove the previous minor version of cl.exe.
            if (-not [IO.Path]::IsPathRooted($Matches[2]) -or (Test-Path -LiteralPath $Matches[2])) { continue }
        } else {
            continue
        }
        # CMake validates a preset's source path before it handles --fresh.
        Remove-Item -LiteralPath $cache -ErrorAction Stop
        '--fresh'
        return
    }
}
