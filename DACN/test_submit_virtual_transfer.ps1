$Url = "http://127.0.0.1:5008"
$ErrorActionPreference = "Continue"

# Kill any existing DACN processes
Get-Process -Name "DACN" -ErrorAction SilentlyContinue | Stop-Process -Force

$workDir = $PSScriptRoot
if (-not $workDir) { $workDir = Get-Location }
$dllPath = Join-Path $workDir "bin\Debug\net8.0\DACN.dll"
$process = Start-Process -FilePath "dotnet" -ArgumentList "`"$dllPath`" --urls `"$Url`"" -WorkingDirectory $workDir -PassThru -NoNewWindow

Start-Sleep -Seconds 5

try {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

    # 1. Test GET /Booking/SubmitVirtualTransfer
    $getRes = Invoke-WebRequest -Uri "$Url/Booking/SubmitVirtualTransfer?bookingId=1" -WebSession $session -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Write-Host "1. GET /Booking/SubmitVirtualTransfer StatusCode: $($getRes.StatusCode)" -ForegroundColor Green
    if ($getRes.StatusCode -eq 302) {
        Write-Host "PASS: GET request redirected safely without 405 Method Not Allowed!" -ForegroundColor Green
    } else {
        Write-Host "FAIL: GET request status was $($getRes.StatusCode)" -ForegroundColor Red
    }

    # 2. Get Antiforgery token from Checkout page
    $checkoutRes = Invoke-WebRequest -Uri "$Url/Booking/Checkout?bookingId=1" -WebSession $session -UseBasicParsing
    $tokenMatch = [regex]::Match($checkoutRes.Content, 'name="__RequestVerificationToken" type="hidden" value="([^"]+)"')
    if (-not $tokenMatch.Success) {
        $tokenMatch = [regex]::Match($checkoutRes.Content, '__RequestVerificationToken.*value="([^"]+)"')
    }
    $token = if ($tokenMatch.Success) { $tokenMatch.Groups[1].Value } else { "" }
    Write-Host "2. Antiforgery Token from Checkout: $(if ($token) { 'Extracted' } else { 'Not Found' })" -ForegroundColor Yellow

    # 3. Test POST /Booking/SubmitVirtualTransfer with sample SVG receipt
    $postBody = @{
        "__RequestVerificationToken" = $token
        "bookingId" = "1"
        "transactionRef" = "VTB-TEST-12345"
        "senderAccount" = "Nguyen Van Test"
        "note" = "Thanh toan ngan hang ao test"
        "sampleProofBill" = "data:image/svg+xml;charset=utf-8,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20width%3D%22400%22%20height%3D%22520%22%3E%3Crect%20fill%3D%22%23000%22%2F%3E%3C%2Fsvg%3E"
    }

    $postRes = Invoke-WebRequest -Uri "$Url/Booking/SubmitVirtualTransfer" -Method Post -Body $postBody -WebSession $session -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Write-Host "3. POST /Booking/SubmitVirtualTransfer StatusCode: $($postRes.StatusCode)" -ForegroundColor Green
    $location = $postRes.Headers["Location"]
    Write-Host "4. Redirect Location: $location" -ForegroundColor Cyan

    if ($location -and $location.Contains("Confirmation")) {
        Write-Host "PASS: POST SubmitVirtualTransfer succeeded and redirected to Confirmation!" -ForegroundColor Green
    } else {
        Write-Host "FAIL: POST did not redirect to Confirmation. Location: $location. Content: $($postRes.Content)" -ForegroundColor Red
    }

    # 4. Verify Confirmation page renders the verified badge and proof
    $confirmRes = Invoke-WebRequest -Uri "$Url$location" -WebSession $session -UseBasicParsing
    Write-Host "5. Confirmation page StatusCode: $($confirmRes.StatusCode)" -ForegroundColor Green
    if ($confirmRes.Content.Contains("Đã nhận tiền thành công") -or $confirmRes.Content.Contains("VTB-TEST-12345")) {
        Write-Host "PASS: Confirmation page displays verified transaction ref and proof successfully!" -ForegroundColor Green
    }

} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
} finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
    Get-Process -Name "DACN" -ErrorAction SilentlyContinue | Stop-Process -Force
}

