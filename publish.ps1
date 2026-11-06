<#
.SYNOPSIS
    Builds Roadmapper for other people: a zip holding a self-contained Roadmapper.exe (no .NET install needed).

.DESCRIPTION
    Runs the tests, publishes src/Roadmapper.Web for win-x64 and zips the result to
    artifacts/Roadmapper-<version>-win-x64.zip. Share the zip; see "Getting started" in README.md.

.EXAMPLE
    ./publish.ps1
    ./publish.ps1 -SkipTests
#>
param(
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $SkipTests) {
    dotnet test --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing was published.' }
}

$project = 'src/Roadmapper.Web/Roadmapper.Web.csproj'
$version = (Select-Xml -Path $project -XPath '//Version').Node.InnerText
$out = "artifacts/publish/$Runtime"
$zip = "artifacts/Roadmapper-$version-$Runtime.zip"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $project -c Release -r $Runtime -o $out --nologo -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# Developer-only settings, debug symbols and the IIS config don't belong in the package.
Remove-Item "$out/appsettings.Development.json", "$out/web.config", "$out/*.pdb" -ErrorAction SilentlyContinue

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out/*" -DestinationPath $zip

$size = '{0:N0} MB' -f ((Get-Item $zip).Length / 1MB)
Write-Host "`nCreated $zip ($size)." -ForegroundColor Green
Write-Host 'Unzip it and double-click Roadmapper.exe to run.'
