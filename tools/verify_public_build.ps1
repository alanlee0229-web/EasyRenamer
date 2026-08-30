param(
    [string]$Configuration = "Release-Public",
    [string]$PublishDirectory = "artifacts\portable\public\win-x64"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\BatchRenamer.App\BatchRenamer.App.csproj"
$publish = Join-Path $repo $PublishDirectory

$compileItems = & dotnet msbuild $project "-p:Configuration=$Configuration" -getItem:Compile
if ($LASTEXITCODE -ne 0) { throw "Unable to evaluate Public compile items." }
if ($compileItems -match "InternalTools") { throw "FAIL: Public compile items contain InternalTools." }

if (-not (Test-Path -LiteralPath $publish)) { throw "FAIL: Public publish directory does not exist: $publish" }
$leaks = Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object {
    $_.Name -match "InternalTools|Internal.Test|QA"
}
if ($leaks) { throw "FAIL: Public publish contains internal tool files: $($leaks.FullName -join ', ')" }

Write-Output "PUBLIC_BUILD_PURITY = PASS"
Write-Output "Evidence: Release-Public compile items and publish files contain no InternalTools entries."
