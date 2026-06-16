Invoke-RestMethod 'http://localhost:5000/api/shifts?from=2026-06-16&to=2026-06-16' | Select-Object -First 3 | ConvertTo-Json
