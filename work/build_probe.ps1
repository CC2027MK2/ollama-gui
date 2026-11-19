$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$W = Split-Path $PSScriptRoot -Parent
$gac = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL"
$out = Join-Path $W "work\build\ocr-probe.exe"

$refs = @()
Get-ChildItem "C:\Windows\System32\WinMetadata\*.winmd" | ForEach-Object { $refs += "/r:`"$($_.FullName)`"" }
foreach ($a in @(
    "System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll",
    "System.Runtime.Extensions\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.Extensions.dll",
    "System.Runtime.InteropServices\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.InteropServices.dll"
)) {
    $refs += "/r:`"$gac\$a`""
}
$refs += "/r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll"

& $csc /nologo /target:exe /platform:x64 /out:$out $refs `
    /r:System.dll /r:System.Core.dll `
    "$W\work\OcrProbe.cs"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "BUILT $out"
