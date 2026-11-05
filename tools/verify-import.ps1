[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest = Get-Content -LiteralPath (Join-Path $root 'migration/import-manifest.json') -Raw | ConvertFrom-Json
$errors = [Collections.Generic.List[string]]::new()
$prefix = $root.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
foreach ($entry in $manifest.files) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $entry.path))
    if (-not $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Invalid path in import manifest: $($entry.path)"
    }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $errors.Add("Missing: $($entry.path)")
        continue
    }
    if ((Get-Item -LiteralPath $path).Length -ne $entry.size) {
        $errors.Add("Size changed: $($entry.path)")
        continue
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
        $errors.Add("Hash changed: $($entry.path)")
    }
}
if ($errors.Count -gt 0) { throw ($errors -join [Environment]::NewLine) }
Write-Host "Import verified: $($manifest.files.Count) files from PROMZONA $($manifest.source_version), sizes and SHA-256 match."
