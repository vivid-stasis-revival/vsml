[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$submodulePath = Join-Path $repositoryRoot "UndertaleModTool"
$patchPath = Join-Path $repositoryRoot "patches\undertale-mod-tool-zero-padding-warning.patch"

if (-not (Test-Path (Join-Path $submodulePath ".git"))) {
    throw "UndertaleModTool is not initialized. Run git submodule update --init --recursive."
}

if (-not (Test-Path $patchPath -PathType Leaf)) {
    throw "Missing UndertaleModTool patch: $patchPath"
}

$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    & git -C $submodulePath apply --reverse --check --whitespace=nowarn $patchPath 2>$null
    $reverseCheckExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

if ($reverseCheckExitCode -eq 0) {
    Write-Host "UndertaleModTool zero-padding patch is already applied."
    exit 0
}

& git -C $submodulePath apply --check --whitespace=error-all $patchPath
if ($LASTEXITCODE -ne 0) {
    throw "UndertaleModTool zero-padding patch does not apply to the checked-out submodule commit."
}

& git -C $submodulePath apply --whitespace=nowarn $patchPath
if ($LASTEXITCODE -ne 0) {
    throw "Failed to apply the UndertaleModTool zero-padding patch."
}

Write-Host "Applied UndertaleModTool zero-padding patch."
