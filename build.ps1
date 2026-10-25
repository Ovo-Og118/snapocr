# 打成单个 exe：自包含发布，目标机器不用装 .NET
param(
    [string]$Runtime = 'win-x64',
    [string]$Out = "$PSScriptRoot\dist"
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\SnapOCR\SnapOCR.csproj'

dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $Out

$exe = Join-Path $Out 'SnapOCR.exe'
if (Test-Path $exe) {
    $size = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "`n完成：$exe ($size MB)" -ForegroundColor Green
}
