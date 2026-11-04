# The exhibition build's whole round through real Unity Relay: four copies on this PC, one hosting, three joining with its code.
# Usage: & Tools\run_relay_test.ps1 [-ShotDir DIR] [-Extra "-relaywss"]   (build first: Rock Game > Build Exhibition Player)
param([string]$ShotDir = "$env:TEMP\rock_relay_shots", [string]$Extra = "", [int]$TimeoutSec = 480)
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'Builds/Exhibition/Alien Rock Game Exhibition.exe'
$logs = Join-Path $root 'Logs/autotest'
New-Item -ItemType Directory -Force $logs, $ShotDir | Out-Null
$code = Join-Path $logs 'relay_code.txt'
Remove-Item $code -ErrorAction SilentlyContinue
$common = "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -autotest exhibition -shotdir `"$ShotDir`" -relayfile `"$code`" $Extra"
$procs = @()
foreach ($i in 0..3) {
    $role = if ($i -eq 0) { 'host' } else { 'join' }
    $log = Join-Path $logs "relay_$i.log"
    Remove-Item $log -ErrorAction SilentlyContinue
    $procs += @{ P = (Start-Process -FilePath $exe -ArgumentList "$common -relayrole $role -logFile `"$log`"" -PassThru -WindowStyle Minimized); Log = $log; I = $i }
    Start-Sleep -Seconds 3
}
$deadline = (Get-Date).AddSeconds($TimeoutSec)
foreach ($x in $procs) {
    $left = [int][Math]::Max(1, ($deadline - (Get-Date)).TotalMilliseconds)
    if (-not $x.P.WaitForExit($left)) { $x.P.Kill(); "copy $($x.I): TIMEOUT" }
    $lines = Get-Content $x.Log
    "== copy $($x.I): pass=$(@($lines | Where-Object { $_ -match '\[AUTOTEST\] PASS' }).Count) fails=$(@($lines | Where-Object { $_ -match '\[AUTOTEST\] FAIL' }).Count) exceptions=$(@($lines | Where-Object { $_ -match 'Exception' } | Select-Object -Unique).Count)"
    $lines | Where-Object { $_ -match '\[AUTOTEST\].*(FAIL|join code|through Relay)|Relay:|\[RockGame\] disconnected' } | Select-Object -First 16 | ForEach-Object { $_.Substring(0, [Math]::Min(170, $_.Length)) }
    $lines | Where-Object { $_ -match 'Exception' } | Select-Object -Unique -First 4 | ForEach-Object { $_.Substring(0, [Math]::Min(170, $_.Length)) }
}
