# Fast compile check of Assets/Scripts with Unity's bundled Roslyn (no editor launch, safe to run while others edit).
# Usage: powershell -NoProfile -File Tools/compile_check.ps1   -> prints "error CS..." lines, or COMPILE OK
$ed = @("C:/Program Files/Unity/Hub/Editor/6000.0.42f1/Editor/Data", "C:/Unity/6000.0.42f1/Editor/Data") | Where-Object { Test-Path $_ } | Select-Object -First 1
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$rsp = Get-Content "Library/Bee/artifacts/1900b0aE.dag/Assembly-CSharp.rsp"
$out = Join-Path $env:TEMP ("rockcheck_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $out | Out-Null
$keep = $rsp | Where-Object { $_ -notmatch '^-out:' -and $_ -notmatch '^-refout:' -and $_ -notmatch '^"?Assets/.*\.cs"?$' -and $_ -notmatch '^-analyzer:' -and $_ -notmatch '^-additionalfile:' -and $_ -notmatch '^-analyzerconfig:' -and $_ -notmatch '^-ruleset:' }
$src = Get-ChildItem Assets -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\Editor\\' } | ForEach-Object { '"' + $_.FullName.Substring($root.Length + 1).Replace('\','/') + '"' }
$new = Join-Path $out "check.rsp"
($keep + $src + ('-out:"' + (Join-Path $out 'check.dll') + '"')) | Set-Content -Encoding utf8 $new
$res = & "$ed/NetCoreRuntime/dotnet.exe" "$ed/DotNetSdkRoslyn/csc.dll" "@$new" 2>&1 | Out-String
$errs = $res -split "`n" | Where-Object { $_ -match 'error ' }
if ($errs) { $errs | Select-Object -First 60 } else { "COMPILE OK" }
