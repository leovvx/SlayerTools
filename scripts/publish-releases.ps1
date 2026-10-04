# Creates a GitHub release for every version tag, using the tag message as release notes
# and the matching prebuilt zip (SlayerTools-vX.Y.Z.zip) as the asset.
# Requires the GitHub CLI (winget install GitHub.cli; gh auth login) and a pushed "origin".
#   .\scripts\publish-releases.ps1 -ZipDir "$env:USERPROFILE\Downloads\SlayerTools-en-releases"
param([Parameter(Mandatory = $true)][string]$ZipDir)
$ErrorActionPreference = "Stop"

$tags = git tag --list "v*" --sort=v:refname
foreach ($tag in $tags) {
    gh release view $tag *> $null
    if ($LASTEXITCODE -eq 0) { Write-Host "$tag already released, skipping"; continue }
    $zip = Join-Path $ZipDir "SlayerTools-$tag.zip"
    $assets = @()
    if (Test-Path $zip) { $assets += $zip } else { Write-Host "No zip for $tag ($zip), publishing notes only" -ForegroundColor Yellow }
    $latest = if ($tag -eq $tags[-1]) { "--latest" } else { "--latest=false" }
    gh release create $tag @assets --title "Slayer Tools $($tag.TrimStart('v'))" --notes-from-tag $latest
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Write-Host "Done!" -ForegroundColor Green
