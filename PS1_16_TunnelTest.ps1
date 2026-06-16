# PS1_16_TunnelTest.ps1
# Tests the two new endpoints through the dev tunnel.
# No here-strings. No Unicode.

$tunnelBase = "https://n8jlr9dr-5000.euw.devtunnels.ms"

function TestEndpoint {
    param([string]$url, [string]$label)
    Write-Host ""
    Write-Host "--- $label ---" -ForegroundColor Cyan
    Write-Host "URL: $url"
    try {
        $resp = Invoke-WebRequest -Uri $url -Method GET -TimeoutSec 30 -UseBasicParsing
        $status = $resp.StatusCode
        $body   = $resp.Content
        $preview = if ($body.Length -gt 300) { $body.Substring(0, 300) + "..." } else { $body }
        Write-Host "HTTP: $status" -ForegroundColor Green
        Write-Host "Body (first 300 chars):"
        Write-Host $preview -ForegroundColor DarkCyan
        return $status -eq 200
    } catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        Write-Host ("HTTP: " + $(if ($statusCode) { $statusCode } else { "ERROR" })) -ForegroundColor Red
        Write-Host "Error: $_" -ForegroundColor Red
        return $false
    }
}

$ok1 = TestEndpoint "$tunnelBase/api/wic/forecast?horizon=3"  "GET /api/wic/forecast?horizon=3"
$ok2 = TestEndpoint "$tunnelBase/api/wic/briefing"            "GET /api/wic/briefing"

Write-Host ""
Write-Host "=== Tunnel test results ===" -ForegroundColor Yellow
Write-Host ("  /api/wic/forecast : " + $(if ($ok1) { "200 OK" } else { "FAILED" })) -ForegroundColor $(if ($ok1) { "Green" } else { "Red" })
Write-Host ("  /api/wic/briefing : " + $(if ($ok2) { "200 OK" } else { "FAILED" })) -ForegroundColor $(if ($ok2) { "Green" } else { "Red" })

if ($ok1 -and $ok2) {
    Write-Host ""
    Write-Host "Both tunnel endpoints: PASSED" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== PS1_16 complete ===" -ForegroundColor Green
