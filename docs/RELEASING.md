# Releasing

Every version is a git tag (`v4.3.0`, `v5.0.0`, …). The tag message holds the release notes.

## Build a release zip
```powershell
.\build.ps1 -NoInstall
Compress-Archive -Path src\bin\Release\net6.0\SlayerTools.dll, RELEASE_README.txt -DestinationPath SlayerTools-v5.2.0.zip
```

## Publish on GitHub
1. Create an empty repository on GitHub, then:
   ```powershell
   git remote add origin https://github.com/<you>/SlayerTools.git
   git push -u origin main --tags
   ```
2. Install and log in to the GitHub CLI: `winget install GitHub.cli`, then `gh auth login`.
3. Publish every release from the prebuilt zips:
   ```powershell
   .\scripts\publish-releases.ps1 -ZipDir "$env:USERPROFILE\Downloads\SlayerTools-en-releases"
   ```
   For every tag without a release, the script runs `gh release create` with the zip and notes = tag message + `SHA256: <hash of the zip>`.

## Self-update
Since 6.1.0 the mod reads `releases/latest` of `leovvx/SlayerTools` and offers to install a newer version. It only accepts a `.zip` asset served from this repository's releases, whose SHA-256 matches the `SHA256:` line of the notes — so always publish with the script (or add that line by hand). The repository must stay public.
