$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$icon = Join-Path $PSScriptRoot 'boc.ico'
$source = Join-Path $PSScriptRoot 'RateStrip.cs'
$output = Join-Path $PSScriptRoot '中行美元牌价.exe'
& $csc /nologo /target:winexe /optimize+ "/win32icon:$icon" "/out:$output" $source
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
