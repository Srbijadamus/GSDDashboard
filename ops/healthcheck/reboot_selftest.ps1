Start-Sleep -Seconds 180
$out = "C:\HealthCheck\reboot_test_result.log"
"=== TEST $(Get-Date -f 'yyyy-MM-dd HH:mm:ss') (NIKO NIJE PRIJAVLJEN) ===" | Add-Content $out
"Prijavljeni korisnici: $((quser 2>&1) -join ' | ')" | Add-Content $out
"https://ssr7tm2l-8000.euw.devtunnels.ms/health",
"https://d2jn94qg-5000.euw.devtunnels.ms/health",
"https://295qv7hp-5016.euw.devtunnels.ms/health" | % {
    try { $r = Invoke-WebRequest $_ -UseBasicParsing -TimeoutSec 20 -EA Stop
          "OK  $_ -> $($r.StatusCode)" | Add-Content $out }
    catch { "FAIL $_ -> $($_.Exception.Message)" | Add-Content $out }
}
