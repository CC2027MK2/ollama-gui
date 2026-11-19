$ErrorActionPreference = "Stop"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = Join-Path (Split-Path $PSScriptRoot -Parent) "src"
$gac = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL"

$out = if ($args.Count -gt 0) { $args[0] } else { Join-Path (Split-Path $PSScriptRoot -Parent) "work\build\ui-check.exe" }
$resArgs = @()
if ($args.Count -gt 1) {
    foreach ($r in $args[1..($args.Count-1)]) {
        if ($r -match '=(.+)$') {
            $file = $Matches[1]
            $name = [System.IO.Path]::GetFileName($file)
            $resArgs += "/resource:`"$file`",$name"
        }
    }
}

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

& $csc /nologo /target:winexe /platform:x64 /out:$out $resArgs $refs `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.Web.Extensions.dll /r:Microsoft.CSharp.dll `
    "$src\Core.cs" "$src\DocTranslate.cs" "$src\MainForm.cs" "$src\SpeechForm.cs" "$src\TranslateForm.cs"

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "BUILT $out"
