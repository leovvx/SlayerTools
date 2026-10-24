# Creates a GitHub release for every version tag, using the tag message as release notes
# (plus the zip's SHA-256, checked by the mod's updater) and the matching prebuilt zip
# (SlayerTools-vX.Y.Z.zip) as the asset.
# Requires the GitHub CLI (winget install GitHub.cli; gh auth login), a pushed "origin"
# and pushed tags (git push origin main --tags).
#   .\scripts\publish-releases.ps1 -ZipDir "$env:USERPROFILE\Downloads\SlayerTools-en-releases"
param([Parameter(Mandatory = $true)][string]$ZipDir)
# Windows PowerShell 5.1 turns anything a native tool writes to stderr into an error,
# so we do not use "Stop" here and check $LASTEXITCODE instead.
$ErrorActionPreference = "Continue"

$tags = @(git tag --list "v*" --sort=v:refname)
if ($tags.Count -eq 0) { Write-Host "No v* tags found. Run this from the repository folder." -ForegroundColor Red; exit 1 }

# Releases that already exist (one call, nothing written to stderr)
$existing = @(gh release list --limit 200 --json tagName --jq ".[].tagName")
if ($LASTEXITCODE -ne 0) { Write-Host "gh failed: check 'gh auth status' and that the repository has an 'origin' on GitHub." -ForegroundColor Red; exit 1 }

# Tags must exist on GitHub before releases are created on them
git push origin --tags
if ($LASTEXITCODE -ne 0) { Write-Host "Could not push the tags to origin." -ForegroundColor Red; exit 1 }

foreach ($tag in $tags) {
    if ($existing -contains $tag) { Write-Host "$tag already released, skipping"; continue }
    $zip = Join-Path $ZipDir "SlayerTools-$tag.zip"
    $assets = @()
    if (Test-Path $zip) { $assets += $zip } else { Write-Host "No zip for $tag ($zip), publishing notes only" -ForegroundColor Yellow }
    $latest = if ($tag -eq $tags[-1]) { "--latest" } else { "--latest=false" }
    Write-Host "Publishing $tag..." -ForegroundColor Cyan
    # release notes = tag message + the zip's SHA-256 (the mod's updater refuses a zip without a matching hash)
    $notes = (git tag -l --format='%(contents)' $tag) -join "`n"
    $notes = ($notes -split "`n" | Select-Object -Skip 2) -join "`n"
    if (Test-Path $zip) { $notes += "`n`nSHA256: " + (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower() }
    $notesFile = New-TemporaryFile
    Set-Content -Path $notesFile -Value $notes -Encoding UTF8
    gh release create $tag @assets --title "Slayer Tools $($tag.TrimStart('v'))" --notes-file $notesFile $latest
    Remove-Item $notesFile
    if ($LASTEXITCODE -ne 0) { Write-Host "Failed to publish $tag" -ForegroundColor Red; exit 1 }
}
Write-Host "Done!" -ForegroundColor Green
