# Publishes a new version of Vibe Stop Processing.
# Version rule:
#   normal release  -> last number +1   (2.5.0 -> 2.5.1 -> 2.5.2 ...)
#   -Minor          -> middle number +1 (2.5.2 -> 2.6.0)   for bigger feature updates
# Usage:  .\release.ps1 -Notes "What changed in this version"
#         .\release.ps1 -Minor -Notes "Big new feature"
#         .\release.ps1 -BuildOnly      (build exe + installer locally, publish nothing)
# Users with "Install updates automatically" on get it on their next app start.

param(
    [string]$Notes = "",
    [switch]$Minor,
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Source code lives in the PRIVATE repo (git origin).
# Users download from the PUBLIC repo, which only holds README, LICENSE and the releases.
$PublicRepo = "jhfarhanyt20-coder/Vibe-stop-processing"
$SourceRepo = "jhfarhanyt20-coder/Vibe-stop-processing-source"

# Safety: this folder must be its OWN git repository whose origin is the private
# source repo. If it sits inside another repository (for example a copy inside the
# VibeCoder folder), "git add / push" would publish that other repository instead.
$top = (git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $top -or ((Resolve-Path $top).Path.TrimEnd('\') -ne (Resolve-Path $PSScriptRoot).Path.TrimEnd('\'))) {
    throw "This folder is not its own git repository. Clone $SourceRepo and run release.ps1 there."
}
$origin = (git remote get-url origin 2>$null)
if ($origin -notmatch [regex]::Escape($SourceRepo) + '(\.git)?$') {
    throw "git origin is '$origin', expected the private repo $SourceRepo. Nothing was changed."
}

# find the highest released version (public releases + local tags) and add 0.1
git fetch --tags --quiet
$tags = @(git tag --list "v*") + @(gh release list --repo $PublicRepo --limit 100 --json tagName --jq ".[].tagName")
$latest = $tags |
    Where-Object { $_ -match '^v(\d+)\.(\d+)\.(\d+)$' } |
    ForEach-Object { [version]($_.TrimStart('v')) } |
    Sort-Object -Descending | Select-Object -First 1
if (-not $latest) { $Version = "1.0.0" }
elseif ($Minor) { $Version = "{0}.{1}.0" -f $latest.Major, ($latest.Minor + 1) }          # big update: 2.5.3 -> 2.6.0
else { $Version = "{0}.{1}.{2}" -f $latest.Major, $latest.Minor, ($latest.Build + 1) }   # normal:     2.5.0 -> 2.5.1
Write-Host "Building v$Version (previous: v$latest)" -ForegroundColor Cyan

# 1. stamp the version into the source (source is pure ASCII, so encoding can't break text)
$path = Join-Path $PSScriptRoot 'StopProcessing.cs'
$cs = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
if ($cs -match '[^\x00-\x7F]') { throw "StopProcessing.cs contains non-ASCII characters. Use \uXXXX escapes instead." }
$cs = $cs -replace 'AssemblyVersion\("[\d\.]+"\)', "AssemblyVersion(`"$Version.0`")"
$cs = $cs -replace 'AssemblyFileVersion\("[\d\.]+"\)', "AssemblyFileVersion(`"$Version.0`")"
[IO.File]::WriteAllText($path, $cs, (New-Object Text.UTF8Encoding $false))

# 2. build the app
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ `
    /out:StopProcessing.exe /win32manifest:app.manifest /win32icon:app.ico `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll StopProcessing.cs
if ($LASTEXITCODE -ne 0) { git checkout -- StopProcessing.cs 2>$null; throw "Build failed" }

# 3. build the installer
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
          "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
          "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }
& $iscc /Q "/DAppVersion=$Version" installer.iss
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
$setup = "dist\VibeStopProcessing-Setup-$Version.exe"

# 4. latest.json - what the in-app updater reads (no GitHub API rate limit)
if (-not $Notes) { $Notes = "Vibe Stop Processing v$Version" }
$exe = Get-Item StopProcessing.exe
$manifest = [ordered]@{
    version = $Version
    notes   = $Notes
    url     = "https://github.com/$PublicRepo/releases/download/v$Version/StopProcessing.exe"
    size    = $exe.Length
    sha256  = (Get-FileHash $exe.FullName -Algorithm SHA256).Hash.ToLower()
}
[IO.File]::WriteAllText("$PSScriptRoot\dist\latest.json", ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

Write-Host "Built StopProcessing.exe, $setup and dist\latest.json" -ForegroundColor Green
if ($BuildOnly) { return }

# 5. commit + tag the source in the private repo
#    git prints harmless warnings (line endings) on stderr; with 'Stop' PowerShell would treat
#    them as errors, so from here every step is checked with $LASTEXITCODE instead
$ErrorActionPreference = 'Continue'
#    only the project's own files are added (never a stray log, key or download)
git add StopProcessing.cs StopProcessing.exe installer.iss release.ps1 app.manifest app.ico README.md LICENSE public/README.md .gitignore
if ($LASTEXITCODE -ne 0) { throw "git add failed" }
git commit -m "Release v$Version"
if ($LASTEXITCODE -ne 0) { throw "git commit failed" }
git tag "v$Version"
if ($LASTEXITCODE -ne 0) { throw "git tag failed" }
git push
if ($LASTEXITCODE -ne 0) { throw "git push failed - nothing was published" }
git push origin "v$Version"
if ($LASTEXITCODE -ne 0) { throw "pushing the tag failed - nothing was published" }

# 6. publish the release on the PUBLIC repo (no source code there)
#    - VibeStopProcessing-Setup-x.y.z.exe : for new users
#    - StopProcessing.exe + latest.json   : used by the in-app auto updater
gh release create "v$Version" $setup StopProcessing.exe dist\latest.json --repo $PublicRepo --target main `
    --title "Vibe Stop Processing v$Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "Publishing the release failed. Check https://github.com/$PublicRepo/releases" }

# 7. make sure every file the updater and new users need is really there
$names = @(gh release view "v$Version" --repo $PublicRepo --json assets --jq ".assets[].name")
foreach ($n in @("VibeStopProcessing-Setup-$Version.exe", "StopProcessing.exe", "latest.json")) {
    if ($names -notcontains $n) { throw "The release is missing $n" }
}

Write-Host "Released v$Version" -ForegroundColor Green
