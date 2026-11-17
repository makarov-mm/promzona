[CmdletBinding()]
param([switch]$Open)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Install Visual Studio 2026 with Desktop development with C++ and C++ CMake tools for Windows.'
}
$vs = & $vswhere -latest -products '*' -version '[18.0,19.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) {
    # A servicing installation can still have a working compiler and SDK.
    $vs = & $vswhere -all -latest -products '*' -version '[18.0,19.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
}
if ($LASTEXITCODE -ne 0 -or -not $vs) {
    throw 'Visual Studio 2026 C++ tools were not found. Add the Desktop development with C++ workload.'
}
$cmake = Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
if (-not (Test-Path -LiteralPath $cmake)) {
    throw 'Add C++ CMake tools for Windows in Visual Studio Installer.'
}
$capabilities = & $cmake -E capabilities
if ($LASTEXITCODE -ne 0) { throw 'Cannot run the Visual Studio CMake installation.' }
$info = $capabilities | ConvertFrom-Json
if ('Visual Studio 18 2026' -notin $info.generators.name) {
    throw 'CMake 4.2 or newer is needed for Visual Studio 2026. Update C++ CMake tools for Windows.'
}

# Generate locally: CMake writes machine-specific paths into .vcxproj files.
# No developer-shell setup is needed. The first run downloads SDL3 (see CMakeLists.txt).
Push-Location -LiteralPath $root
try {
    . (Join-Path $root 'tools/cmake-cache.ps1')
    $cacheArgs = @(Get-CMakeCacheArguments (Join-Path $root 'build/vs2026') $root)
    & $cmake @cacheArgs --preset vs2026 "-DCMAKE_GENERATOR_INSTANCE=$vs"
    if ($LASTEXITCODE -ne 0) { throw 'Visual Studio solution generation failed.' }
}
finally { Pop-Location }

# CMake 4.3 uses the VS 2026 .slnx format; 4.2 generates a traditional .sln.
$solution = Join-Path $root 'build/vs2026/Promzona.slnx'
if (-not (Test-Path -LiteralPath $solution)) {
    $solution = Join-Path $root 'build/vs2026/Promzona.sln'
}
if (-not (Test-Path -LiteralPath $solution)) { throw 'Generated Promzona solution not found in build/vs2026.' }
Write-Host "Solution: $solution"
Write-Host 'Choose Debug | x64 or Release | x64. F5 starts Promzona; RUN_TESTS runs the native tests after a build.'
if ($Open) {
    $ide = Join-Path $vs 'Common7/IDE/devenv.exe'
    if (-not (Test-Path -LiteralPath $ide)) {
        throw 'Solution generated, but this installation contains Build Tools only. Open it in the Visual Studio 2026 IDE.'
    }
    Start-Process -FilePath $ide -ArgumentList "`"$solution`""
}
