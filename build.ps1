[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [switch]$Package,
    [switch]$Run
)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if(-not (Test-Path -LiteralPath $vswhere)){throw 'Install Visual Studio C++ Desktop workload and CMake tools.'}
$vs=& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $vs){throw 'MSVC C++ tools not found.'}
$cmake=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ctest=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/ctest.exe'
$ninja=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe'
foreach($tool in @($cmake,$ctest,$ninja)){if(-not(Test-Path -LiteralPath $tool)){throw "Missing tool: $tool"}}
# Import the compiler environment into this process only. No registry/PATH changes.
$savedPath=$env:PATH
try {
    # A repeated developer/tool PATH can exceed cmd.exe's 8191-character limit.
    $env:PATH="$env:SystemRoot/System32;$env:SystemRoot;$env:SystemRoot/System32/WindowsPowerShell/v1.0"
    Import-Module (Join-Path $vs 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
    Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
    $env:PATH=($env:PATH.Split(';')+$savedPath.Split(';') | Select-Object -Unique) -join ';'
}catch{$env:PATH=$savedPath;throw}
$build=Join-Path $root "build/native-$Configuration"
. (Join-Path $root 'tools/cmake-cache.ps1')
$cacheArgs = @(Get-CMakeCacheArguments $build $root)
& $cmake @cacheArgs -S $root -B $build -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" "-DCMAKE_BUILD_TYPE=$Configuration" '-DBUILD_TESTING=ON'
if($LASTEXITCODE -ne 0){throw 'CMake configure failed.'}
& $cmake --build $build --parallel 6
if($LASTEXITCODE -ne 0){throw 'Native compilation failed.'}
& $ctest --test-dir $build --output-on-failure
if($LASTEXITCODE -ne 0){throw 'Native tests failed.'}
& (Join-Path $root 'tools/verify-native.ps1') -Executable (Join-Path $build 'Promzona.exe')
if($Package){
    $destination=Join-Path $root "artifacts/$Configuration"
    & $cmake --install $build --prefix $destination
    if($LASTEXITCODE -ne 0){throw 'Runtime staging failed.'}
    Write-Host "Runtime: $destination/Promzona.exe"
}
if($Run){& (Join-Path $build 'Promzona.exe')}
Write-Host "Built: $build/Promzona.exe"
