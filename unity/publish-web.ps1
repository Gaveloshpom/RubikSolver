# Copies the web build (unity/RubikApp/Builds/Web, made by Rubik -> Build Web) into the gh-pages branch
# and commits it there. The current branch and working tree are not touched (a temporary worktree is used).
#   powershell -ExecutionPolicy Bypass -File unity/publish-web.ps1          # commit only
#   powershell -ExecutionPolicy Bypass -File unity/publish-web.ps1 -Push    # commit and push to origin
param([switch]$Push)

# "Continue": in Windows PowerShell 5.1 "Stop" turns any git stderr output into an exception;
# failures are checked explicitly through $LASTEXITCODE instead.
$ErrorActionPreference = "Continue"
$repo = Split-Path -Parent $PSScriptRoot
$build = Join-Path $PSScriptRoot "RubikApp/Builds/Web"
if (-not (Test-Path (Join-Path $build "index.html"))) { throw "No web build at $build. Run Rubik -> Build Web in Unity first." }

$version = (Select-String -Path (Join-Path $PSScriptRoot "RubikApp/Assets/Editor/RubikBuild.cs") -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
$worktree = Join-Path ([System.IO.Path]::GetTempPath()) "rubik-gh-pages"

function Invoke-Git { & git.exe -C $repo @args; if ($LASTEXITCODE -ne 0) { throw "git $args failed" } }

if (Test-Path $worktree) { & git -C $repo worktree remove --force $worktree 2>$null; Remove-Item -Recurse -Force $worktree -ErrorAction SilentlyContinue }
& git -C $repo worktree prune

& git -C $repo fetch origin gh-pages 2>$null
$hasLocal = (& git -C $repo branch --list gh-pages)
$hasRemote = (& git -C $repo branch -r --list origin/gh-pages)

if ($hasLocal) {
    Invoke-Git worktree add $worktree gh-pages
} elseif ($hasRemote) {
    Invoke-Git worktree add -b gh-pages $worktree origin/gh-pages
} else {
    # first publish: an orphan branch that only ever contains the built site
    Invoke-Git worktree add --detach $worktree
    & git -C $worktree checkout --orphan gh-pages; if ($LASTEXITCODE -ne 0) { throw "orphan checkout failed" }
    & git -C $worktree rm -rf --quiet .; if ($LASTEXITCODE -ne 0) { throw "clearing the orphan branch failed" }
}

try {
    Get-ChildItem -Force $worktree | Where-Object { $_.Name -ne ".git" } | Remove-Item -Recurse -Force -ErrorAction Stop
    Get-ChildItem -Force $build | Copy-Item -Destination $worktree -Recurse -Force -ErrorAction Stop

    & git -C $worktree add -A
    & git -C $worktree diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { Write-Host "Web build unchanged, nothing to commit." }
    else {
        & git -C $worktree commit -q -m "Publish web build $version"
        if ($LASTEXITCODE -ne 0) { throw "commit failed" }
        Write-Host "Committed web build $version to gh-pages."
    }

    if ($Push) {
        & git -C $worktree push origin gh-pages
        if ($LASTEXITCODE -ne 0) { throw "push failed" }
    } else {
        Write-Host "Not pushed. To publish: git push origin gh-pages"
    }
} finally {
    & git -C $repo worktree remove --force $worktree
}
