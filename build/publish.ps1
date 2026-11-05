<#
.SYNOPSIS
    发布 XNY SCADA（WPF / WinForms）并可选生成 Inno Setup 安装包。

.EXAMPLE
    ./build/publish.ps1                         # 测试 + 发布两个界面版本 + 压缩包
    ./build/publish.ps1 -Ui Wpf -SelfContained  # 只发布 WPF，自带 .NET 运行时
    ./build/publish.ps1 -SkipTests -Installer   # 跳过测试并生成安装包（需安装 Inno Setup 6）
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'Wpf', 'WinForms')]
    [string]$Ui = 'All',
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [switch]$SkipTests,
    [switch]$Installer,
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Output) { $Output = Join-Path $root 'artifacts' }
$Output = [System.IO.Path]::GetFullPath($Output)
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

function Invoke-Step([string]$title, [scriptblock]$action) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$title 失败 (exit $LASTEXITCODE)" }
}

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $version) { $version = '1.0.0' }
Write-Host "XNY SCADA $version  ($Configuration, $Runtime, SelfContained=$($SelfContained.IsPresent))"

if (-not $SkipTests) {
    Invoke-Step '单元/端到端测试' { dotnet test (Join-Path $root 'tests\XNY.Scada.Tests') -c $Configuration --nologo }
}

$variants = if ($Ui -eq 'All') { @('Wpf', 'WinForms') } else { @($Ui) }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

foreach ($variant in $variants) {
    $project = Join-Path $root "src\XNY.Scada.$variant\XNY.Scada.$variant.csproj"
    $dir = Join-Path $Output "publish\$variant"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }

    Invoke-Step "发布 $variant" {
        dotnet publish $project -c $Configuration -r $Runtime --self-contained $SelfContained.IsPresent `
            -o $dir --nologo -p:Version=$version -p:DebugType=none
    }

    foreach ($sub in 'plugins', 'data', 'logs') { New-Item -ItemType Directory -Force -Path (Join-Path $dir $sub) | Out-Null }
    Set-Content -Path (Join-Path $dir 'plugins\README.txt') -Encoding UTF8 -Value @(
        '将实现 IPlcDriver / INodeHandler 的扩展程序集 (*.dll) 放入本目录，重启后自动加载。',
        '驱动需标注 [PlcDriver]，流程节点需标注 [FlowNode]。'
    )

    $zip = Join-Path $Output "XNYScada-$variant-$version-$Runtime.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $dir '*') -DestinationPath $zip
    Write-Host "    压缩包: $zip" -ForegroundColor Green

    if ($Installer) {
        $iscc = @(
            (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
        if (-not $iscc) {
            Write-Warning '未找到 Inno Setup 6 (ISCC.exe)，跳过安装包生成'
        }
        else {
            Invoke-Step "生成 $variant 安装包" {
                & $iscc (Join-Path $PSScriptRoot 'installer\XNYScada.iss') `
                    "/DAppVariant=$variant" "/DAppVersion=$version" "/DSourceDir=$dir" "/DOutputDir=$Output" `
                    "/DSelfContained=$([int]$SelfContained.IsPresent)"
            }
        }
    }
}

Write-Host "完成，输出目录: $Output" -ForegroundColor Green
