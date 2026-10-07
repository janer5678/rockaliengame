# Runs autotest modes against the built player (windowed, as the tests expect) and prints each one's failures.
# Usage (PowerShell): & Tools\run_autotests.ps1 inv,"upgrades:-solo -rules autowood","ball+client"
#   "name:args"   extra args for the host (give -solo / -fast yourself; -fast ends the match after 90 s, too soon for the long tests)
#   "name+client" also starts a second copy as a client 6 s later
#   "name+client2" ...and a third copy, another client, 14 s after that (the spectate test: it joins the running match)
param([string[]]$Modes = @('inv:-solo','upgrades:-solo -rules autowood','feel:-solo','ui:-solo','craftui:-solo','scenery:-solo','world:-solo','dome:-solo','nodes:-solo','victory:-solo','buildbias:-solo'), [int]$TimeoutSec = 480)
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'Builds/Windows/Alien Rock Game.exe'
New-Item -ItemType Directory -Force (Join-Path $root 'Logs/autotest') | Out-Null
$win = '-screen-fullscreen 0 -screen-width 1280 -screen-height 720'
function Report($tag, $log, $exit) {
    $lines = if (Test-Path $log) { Get-Content $log } else { @() }
    $fails = @($lines | Where-Object { $_ -match '\[AUTOTEST\].*FAIL' })
    $exc = @($lines | Where-Object { $_ -match 'Exception' } | Select-Object -Unique)
    $pass = @($lines | Where-Object { $_ -match '\[AUTOTEST\] PASS' }).Count
    "== $tag : exit=$exit pass=$pass fails=$($fails.Count) exceptions=$($exc.Count)"
    $fails | Select-Object -First 15
    $exc | Select-Object -First 6
    if ($exit -eq 'TIMEOUT') { $lines | Where-Object { $_ -match '\[AUTOTEST\]' } | Select-Object -Last 2 }
}
foreach ($m in $Modes) {
    $spec, $extra = $m -split ':', 2
    $client2 = $spec -match '\+client2$'
    $client = $client2 -or ($spec -match '\+client$')
    $name = $spec -replace '\+client2?$', ''
    $safe = $m -replace '[^\w]', '_'
    $log = Join-Path $root "Logs/autotest/$safe.log"
    $clog = Join-Path $root "Logs/autotest/${safe}_client.log"
    Remove-Item $log, $clog -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $exe -ArgumentList "$win -host -autotest $name $extra -logFile `"$log`"" -PassThru
    $c = $null
    if ($client) {
        Start-Sleep -Seconds 6
        $c = Start-Process -FilePath $exe -ArgumentList "$win -client 127.0.0.1 -autotest $name $extra -logFile `"$clog`"" -PassThru
    }
    $c2 = $null
    $c2log = Join-Path $root "Logs/autotest/${safe}_client2.log"
    if ($client2) {
        Remove-Item $c2log -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 14
        $c2 = Start-Process -FilePath $exe -ArgumentList "$win -client 127.0.0.1 -autotest $name $extra -logFile `"$c2log`"" -PassThru
    }
    $done = $p.WaitForExit($TimeoutSec * 1000)
    if (-not $done) { $p.Kill() }
    $cdone = $true
    if ($c) { $cdone = $c.WaitForExit(30000); if (-not $cdone) { $c.Kill() } }
    Report $m $log $(if ($done) { $p.ExitCode } else { 'TIMEOUT' })
    if ($c) { Report "$m (client)" $clog $(if ($cdone) { $c.ExitCode } else { 'TIMEOUT' }) }
    if ($c2) { $c2done = $c2.WaitForExit(30000); if (-not $c2done) { $c2.Kill() }; Report "$m (client 2)" $c2log $(if ($c2done) { $c2.ExitCode } else { 'TIMEOUT' }) }
}
