<#
.SYNOPSIS
  Sumbo 공개 스냅샷 동기화 — allowlist 기반.

.DESCRIPTION
  로컬(작업) 리포에서 공개 리포 working tree 로 "공개 대상만" 복사한다.
  allowlist 에 없는 항목(내부 협업 문서·아티팩트 등)은 어떤 경우에도 복사되지 않는다.
  디렉토리는 robocopy /MIR 로 미러링하므로 로컬에서 삭제된 파일이 공개 tree 에도 반영된다.
  공개 리포의 .git 은 건드리지 않는다 (commit/push 는 별도 수동 수행).

.EXAMPLE
  .\scripts\publish-snapshot.ps1                       # 기본: <repo 상위>\Sumbo-public
  .\scripts\publish-snapshot.ps1 -PublicDir d:\pub\Sumbo
#>
param(
    [string]$PublicDir = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Sumbo-public')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot   # scripts\ 의 상위 = 리포 루트

# ── 공개 allowlist (여기 없는 항목은 절대 공개되지 않음) ──────────────────
$dirs  = @('src', 'tests', 'assets', '.github', 'scripts')
$files = @('Sumbo.slnx', '.gitignore', '.editorconfig', 'LICENSE',
           'THIRD-PARTY-NOTICES.md', 'README.md', 'README.ko.md')

if (-not (Test-Path $PublicDir)) { New-Item -ItemType Directory -Path $PublicDir | Out-Null }

foreach ($d in $dirs) {
    $srcPath = Join-Path $repo $d
    if (-not (Test-Path $srcPath)) { throw "allowlist 디렉토리 없음: $srcPath" }
    robocopy $srcPath (Join-Path $PublicDir $d) /MIR /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy 실패(exit $LASTEXITCODE): $d" }
}
foreach ($f in $files) {
    $srcPath = Join-Path $repo $f
    if (-not (Test-Path $srcPath)) { throw "allowlist 파일 없음: $srcPath" }
    Copy-Item $srcPath (Join-Path $PublicDir $f) -Force
}

Write-Host "snapshot 완료 → $PublicDir"
exit 0
