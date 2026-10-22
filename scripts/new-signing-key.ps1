# Generates an ECDSA P-256 private key (base64 PKCS#8) for quote signing and stores it
# in the azd environment as QUOTE_SIGNING_KEY.
#
#   .\scripts\new-signing-key.ps1
#
# Windows PowerShell 5.1 runs on .NET Framework, which has no ECDsa.ExportPkcs8PrivateKey,
# so this uses CNG with an exportable key. Works in pwsh 7 on Windows too.
# Run before `azd up`. For production keep the key in Key Vault and reference it from
# the app setting; this script is for a sample.

$ErrorActionPreference = 'Stop'

$params = New-Object System.Security.Cryptography.CngKeyCreationParameters
$params.ExportPolicy = [System.Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
$params.KeyUsage = [System.Security.Cryptography.CngKeyUsages]::Signing
$key = [System.Security.Cryptography.CngKey]::Create([System.Security.Cryptography.CngAlgorithm]::ECDsaP256, $null, $params)
$pkcs8 = $key.Export([System.Security.Cryptography.CngKeyBlobFormat]::Pkcs8PrivateBlob)
$key.Dispose()
$b64 = [Convert]::ToBase64String($pkcs8)

azd env set QUOTE_SIGNING_KEY $b64
if ($LASTEXITCODE -ne 0) { throw "azd env set failed with exit code $LASTEXITCODE" }

Write-Host "QUOTE_SIGNING_KEY stored in the azd environment ($($pkcs8.Length) bytes PKCS#8)."
Write-Host "Optional for local runs: copy it into src/StayApi/local.settings.json as QUOTE_SIGNING_KEY; otherwise the app generates an ephemeral key at start."
