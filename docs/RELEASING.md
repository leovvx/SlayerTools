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
   The script runs `gh release create <tag> <zip> --notes-from-tag` for every tag that does not have a release yet.
