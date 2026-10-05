param([Parameter(Mandatory)][string]$ReadyFile)
$ErrorActionPreference = 'Stop'
# Isolated token fixture only; never starts or restarts a live service.
$PID | Set-Content -LiteralPath $ReadyFile
Start-Sleep -Seconds 900
