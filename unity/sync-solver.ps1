# Builds Rubik.Core and Rubik.Solver3x3 and copies the DLLs into the Unity project.
# Run after every change in src/:  powershell -ExecutionPolicy Bypass -File unity/sync-solver.ps1
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src/Rubik.Solver3x3/Rubik.Solver3x3.csproj"
$out = Join-Path $repo "src/Rubik.Solver3x3/bin/$Configuration/netstandard2.1"
$dest = Join-Path $PSScriptRoot "RubikApp/Assets/Plugins/Rubik"

dotnet build $project -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

New-Item -ItemType Directory -Force $dest | Out-Null
foreach ($name in "Rubik.Core", "Rubik.Solver3x3") {
    foreach ($ext in "dll", "pdb") {
        Copy-Item (Join-Path $out "$name.$ext") $dest -Force
    }
}
Write-Host "Copied solver DLLs to $dest"
