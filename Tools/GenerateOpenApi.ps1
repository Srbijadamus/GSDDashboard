# Tools/GenerateOpenApi.ps1
#
# Regenerates the OpenAPI-derived TypeScript types (Frontend/src/api/schema.d.ts)
# with openapi-typescript.
#
# Backend/openapi.yaml is a committed SURFACE inventory (paths, methods, tags) of
# the backend routes. To regenerate the FULL document with request/response
# schemas instead:
#   1. Start the backend (requires SQL Server + the external kiosk runtime config
#      loaded by PulseRuntimeConfiguration).
#   2. As a DEV user, download /swagger/v1/swagger.json — the /swagger route is
#      DEV-gated (404 for everyone else). Save it, e.g. .tmp-swagger.json.
#   3. Optionally convert to YAML, e.g. `yq -P .tmp-swagger.json > Backend/openapi.yaml`.
#   4. Run this script with `-Spec .tmp-swagger.json` (openapi-typescript accepts
#      both JSON and YAML).
#
# Without a running backend, run this script with the committed openapi.yaml to
# refresh schema.d.ts from the surface inventory.
param(
    [string]$Spec = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Backend\openapi.yaml')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $Spec)) { throw "Spec not found: $Spec" }
$out = Join-Path $root 'Frontend\src\api\schema.d.ts'

Write-Host "openapi-typescript: $Spec -> $out"
npx --yes openapi-typescript $Spec -o $out
Write-Host 'Done.'
