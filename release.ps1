# Publishes a new version of Vibe Stop Processing.
# Usage:  .\release.ps1 -Version 2.2.0 -Notes "What changed in this version"
# Users with "Install updates automatically" on get it on their next app start.

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = ""
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 2.2.0" }
if (git tag --list "v$Version") { throw "v$Version already exists. Use a higher version number." }

# 1. stamp the version into the source
$path = Join-Path $PSScriptRoot 'StopProcessing.cs'
$cs = [IO.File]::ReadAllText($path)
$cs = $cs -replace 'AssemblyVersion\("[\d\.]+"\)', "AssemblyVersion(`"$Version.0`")"
$cs = $cs -replace 'AssemblyFileVersion\("[\d\.]+"\)', "AssemblyFileVersion(`"$Version.0`")"
[IO.File]::WriteAllText($path, $cs, (New-Object Text.UTF8Encoding $false))

# 2. build
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ `
    /out:StopProcessing.exe /win32manifest:app.manifest `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll StopProcessing.cs
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# 3. commit, push and publish the GitHub release (the app downloads StopProcessing.exe from here)
git add -A
git commit -m "Release v$Version"
git push
if (-not $Notes) { $Notes = "Vibe Stop Processing v$Version" }
gh release create "v$Version" StopProcessing.exe --title "Vibe Stop Processing v$Version" --notes $Notes

Write-Host "Released v$Version" -ForegroundColor Green
