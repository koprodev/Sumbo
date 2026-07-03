<#
.SYNOPSIS
  Syncs the public snapshot tree from this repo, allowlist-based.

.DESCRIPTION
  Copies only the allowlisted items into the public working tree; anything not on the
  allowlist is never copied. Directories are mirrored with robocopy /MIR, so files
  deleted here disappear from the public tree as well. The public tree's .git is left
  untouched (commit/push are separate manual steps).

.EXAMPLE
  .\scripts\publish-snapshot.ps1                       # default: <repo parent>\Sumbo-public
  .\scripts\publish-snapshot.ps1 -PublicDir d:\pub\Sumbo
#>
param(
    [string]$PublicDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Sumbo-public')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot   # parent of scripts\ = repo root

# ── Public allowlist (anything not listed here is never published) ──────────
$dirs  = @('src', 'tests', 'assets', '.github', 'scripts', 'docs', 'installer')
$files = @('Sumbo.slnx', '.gitignore', '.editorconfig', 'LICENSE',
           'THIRD-PARTY-NOTICES.md', 'README.md', 'README.ko.md')

if (-not (Test-Path $PublicDir)) { New-Item -ItemType Directory -Path $PublicDir | Out-Null }

foreach ($d in $dirs) {
    $srcPath = Join-Path $repo $d
    if (-not (Test-Path $srcPath)) { throw "allowlist 디렉토리 없음: $srcPath" }
    robocopy $srcPath (Join-Path $PublicDir $d) /MIR /XD bin obj _src /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy 실패(exit $LASTEXITCODE): $d" }
}
foreach ($f in $files) {
    $srcPath = Join-Path $repo $f
    if (-not (Test-Path $srcPath)) { throw "allowlist 파일 없음: $srcPath" }
    Copy-Item $srcPath (Join-Path $PublicDir $f) -Force
}

Write-Host "snapshot 완료 → $PublicDir"
exit 0
