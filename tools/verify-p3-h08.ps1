<#
.SYNOPSIS
    P3 门禁脚本（H08 数据分析与人工复判：增量 6 主页面 + 增量 7 结果输出 + 增量 8 按 Die 查看/Sinf/AI 推理，1.4.0）。
.DESCRIPTION
    自动执行构建 + 单元测试 + ui-error.log 空校验，收口打印 ALL DONE。
    覆盖 docs/P3-Migration-Status.md 的验证证据（含 ResultExportServiceTests 的 T-037 诚实占位、SinfGeneratorTests 的 B-12、RecheckServiceTests 的 R-H08-07/BR-66 断言）。
.PARAMETER Configuration
    构建/测试配置，默认 Release。
.PARAMETER Uia
    可选：额外运行 UIAutomation 冒烟（需要已登录的 Windows 会话与显示器，默认跳过）。
.EXAMPLE
    pwsh tools/verify-p3-h08.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $Uia
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
# 仓库根目录（tools/ 的上一级）
$repo = (Get-Item $root).Parent.FullName
Set-Location $repo

function Fail([string] $msg) {
    Write-Host "FAILED: $msg" -ForegroundColor Red
    exit 1
}

Write-Host "== [1/3] 构建 $Configuration ==" -ForegroundColor Cyan
dotnet build WaferInspect.slnx -c $Configuration
if ($LASTEXITCODE -ne 0) { Fail "dotnet build 失败" }

Write-Host "== [2/3] 运行单元测试（不重新构建）==" -ForegroundColor Cyan
dotnet test WaferInspect.slnx -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { Fail "dotnet test 失败" }

Write-Host "== [3/3] 校验运行期 ui-error.log 为空 ==" -ForegroundColor Cyan
$log = Join-Path $repo 'ui-error.log'
if (Test-Path $log) {
    $size = (Get-Item $log).Length
    if ($size -gt 0) { Fail "ui-error.log 非空（$size 字节），存在未处理界面异常" }
    Write-Host "ui-error.log 存在且为空（0 字节）。" -ForegroundColor Green
} else {
    Write-Host "ui-error.log 不存在（运行期无未处理异常）。" -ForegroundColor Green
}

if ($Uia) {
    Write-Host "== [可选] UIAutomation 冒烟（需已登录 Windows 会话）==" -ForegroundColor Cyan
    dotnet run --project src/WaferInspect.App -c $Configuration --no-build -- smokescreen
    if ($LASTEXITCODE -ne 0) { Fail "UIA 冒烟失败" }
}

Write-Host "ALL DONE" -ForegroundColor Green
exit 0
