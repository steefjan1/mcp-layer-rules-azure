# Walks the REST door end to end and proves the three kitchen checks:
#   1. a tampered quote is refused (provenance)
#   2. a retry with the same Idempotency-Key does not book twice (recovery)
#   3. a caller without the role gets 403 (authority), local mode only
#
#   .\scripts\smoke-test.ps1                                   # local: http://localhost:7071/api with X-Local-Roles
#   .\scripts\smoke-test.ps1 -BaseUrl https://func-stayapi-xxx.azurewebsites.net/api -Token (az account get-access-token --resource api://<appId> --query accessToken -o tsv)
#
# Windows PowerShell 5.1 safe.

[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:7071/api',
    [string] $Token = '',
    [string] $LocalRoles = 'Stays.Read,Bookings.Create,Bookings.Cancel'
)
$ErrorActionPreference = 'Stop'

function Headers([string] $roles, [string] $idem) {
    $h = @{ 'Content-Type' = 'application/json' }
    if ($Token) { $h['Authorization'] = "Bearer $Token" } else { $h['X-Local-Roles'] = $roles }
    if ($idem) { $h['Idempotency-Key'] = $idem }
    return $h
}

function Call([string] $method, [string] $path, $body, [string] $roles, [string] $idem) {
    $json = $null
    if ($null -ne $body) { $json = $body | ConvertTo-Json -Depth 10 -Compress }
    try {
        $r = Invoke-WebRequest -Method $method -Uri ($BaseUrl + $path) -Headers (Headers $roles $idem) -Body $json -UseBasicParsing
        return @{ status = [int]$r.StatusCode; body = ($r.Content | ConvertFrom-Json); headers = $r.Headers }
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { throw }
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $text = $reader.ReadToEnd()
        return @{ status = [int]$resp.StatusCode; body = ($text | ConvertFrom-Json); headers = @{} }
    }
}

function Check([bool] $ok, [string] $what) {
    if ($ok) { Write-Host "PASS  $what" -ForegroundColor Green } else { Write-Host "FAIL  $what" -ForegroundColor Red; $script:failed = $true }
}
$script:failed = $false

Write-Host "Stay API at $BaseUrl"

$listings = Call 'GET' '/listings?city=Amsterdam&guests=4' $null $LocalRoles ''
Check ($listings.status -eq 200 -and $listings.body.Count -ge 1) "GET /listings returns Amsterdam listings for 4 guests ($($listings.body.Count))"

$quote = Call 'POST' '/quotes' @{ listingId = 'ams-02'; checkIn = '2026-12-05'; checkOut = '2026-12-08'; guests = 4 } $LocalRoles ''
Check ($quote.status -eq 201 -and $quote.body.signature) "POST /quotes returns a signed quote (total $($quote.body.quote.total) $($quote.body.quote.currency))"
$signed = $quote.body

# 1. Provenance: change the price, keep the signature.
$tampered = $signed | ConvertTo-Json -Depth 10 | ConvertFrom-Json
$tampered.quote.total = 1
$t = Call 'POST' '/bookings' @{ signedQuote = $tampered; guestName = 'Mallory' } $LocalRoles 'smoke-tamper'
Check ($t.status -eq 422) "POST /bookings with a tampered total is refused with 422 ($($t.body.detail))"

# 2. Recovery: same key twice, one booking.
$idem = 'smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$b1 = Call 'POST' '/bookings' @{ signedQuote = $signed; guestName = 'Alice' } $LocalRoles $idem
Check ($b1.status -eq 201) "POST /bookings creates booking $($b1.body.bookingId)"
$b2 = Call 'POST' '/bookings' @{ signedQuote = $signed; guestName = 'Alice' } $LocalRoles $idem
Check ($b2.status -eq 200 -and $b2.body.bookingId -eq $b1.body.bookingId) "Retry with the same Idempotency-Key returns the same booking (200, no second write)"
$b3 = Call 'POST' '/bookings' @{ signedQuote = $signed; guestName = 'Alice' } $LocalRoles ''
Check ($b3.status -eq 400) "POST /bookings without Idempotency-Key is refused with 400"

# 3. Authority (local mode only; in Azure the role lives on the token).
if (-not $Token) {
    $f = Call 'DELETE' "/bookings/$($b1.body.bookingId)" $null 'Stays.Read,Bookings.Create' ''
    Check ($f.status -eq 403) "DELETE /bookings without Bookings.Cancel is refused with 403"
}
$c = Call 'DELETE' "/bookings/$($b1.body.bookingId)" $null $LocalRoles ''
Check ($c.status -eq 200 -and $c.body.status -eq 'cancelled') "DELETE /bookings with Bookings.Cancel cancels the booking"

Write-Host ''
if ($script:failed) { Write-Host 'Some checks failed.' -ForegroundColor Red; exit 1 }
Write-Host 'All checks passed.' -ForegroundColor Green
