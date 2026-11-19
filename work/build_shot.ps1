$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$W = Split-Path $PSScriptRoot -Parent
$src = Join-Path $W "src"
$gac = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL"
$out = Join-Path $W "work\build\ui-shot.exe"

$refs = @()
Get-ChildItem "C:\Windows\System32\WinMetadata\*.winmd" | ForEach-Object { $refs += "/r:`"$($_.FullName)`"" }
foreach ($a in @(
    "System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll",
    "System.Runtime.Extensions\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.Extensions.dll",
    "System.Runtime.InteropServices\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.InteropServices.dll",
    "System.IO.Compression\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.dll",
    "System.IO.Compression.FileSystem\v4.0_4.0.0.0__b77a5c561934e089\System.IO.Compression.FileSystem.dll"
)) {
    $refs += "/r:`"$gac\$a`""
}
$refs += "/r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll"

& $csc /nologo /target:winexe /platform:x64 /main:OllamaSwitch.UiShot /out:$out $refs `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll `
    "$src\Core.cs" "$src\DocTranslate.cs" "$src\MainForm.cs" "$src\SpeechForm.cs" "$src\TranslateForm.cs" "$W\work\UiShot.cs"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "BUILT $out"
