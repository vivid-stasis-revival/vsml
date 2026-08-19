param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$zipName = "vividstasisModLoader-$Version.zip"
$zipPath = Join-Path $PublishDir $zipName
$smbUploadDir = "\\duskbreaker_sat\TVOFiles\模组加载器\"

Write-Host "PublishDir: $PublishDir"
Write-Host "Version: $Version"

# Clean ALL old zips before packaging
Get-ChildItem -LiteralPath $PublishDir -Filter "*.zip" -Force -ErrorAction SilentlyContinue | Remove-Item -Force
Write-Host "Cleaned old zips from publish dir"

if (-not (Test-Path -LiteralPath $PublishDir)) {
    throw "Publish directory not found: $PublishDir"
}

$items = Get-ChildItem -LiteralPath $PublishDir -Force | Where-Object {
    $_.Extension -ne ".pdb"
}

if (-not $items) {
    throw "Publish directory is empty, nothing to pack"
}

Compress-Archive -LiteralPath $items.FullName -DestinationPath $zipPath -Force
Write-Host "Zip created: $zipPath"

if (-not [string]::IsNullOrWhiteSpace($smbUploadDir)) {
    if (-not (Test-Path -LiteralPath $smbUploadDir)) {
        throw "SMB directory not accessible: $smbUploadDir"
    }
    Copy-Item -LiteralPath $zipPath -Destination $smbUploadDir -Force
    Write-Host "SMB upload done: $smbUploadDir$zipName"
}