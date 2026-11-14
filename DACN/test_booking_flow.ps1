$Url = "http://127.0.0.1:5007"
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
    
    # 1. GET Matrix page
    $tomorrow = (Get-Date).AddDays(1).ToString("yyyy-MM-dd")
    $matrixUrl = "$Url/Booking/Matrix?courtId=1&date=$tomorrow"
    $matrixRes = Invoke-WebRequest -Uri $matrixUrl -WebSession $session -UseBasicParsing
    
    Write-Host "1. GET Matrix status: $($matrixRes.StatusCode)" -ForegroundColor Green

    # Extract Antiforgery token
    $tokenMatch = [regex]::Match($matrixRes.Content, 'name="__RequestVerificationToken" type="hidden" value="([^"]+)"')
    if (-not $tokenMatch.Success) {
        $tokenMatch = [regex]::Match($matrixRes.Content, '__RequestVerificationToken.*value="([^"]+)"')
    }
    $token = if ($tokenMatch.Success) { $tokenMatch.Groups[1].Value } else { "" }
    Write-Host "2. Antiforgery Token extracted: $(if ($token) { 'Yes' } else { 'No' })" -ForegroundColor Yellow

    # 2. POST to Create Booking (Booking holding flow)
    $createUrl = "$Url/Booking/Create"
    $postBody = @{
        "__RequestVerificationToken" = $token
        "CourtId" = "1"
        "BookingDate" = $tomorrow
        "StartTime" = "19:00"
        "EndTime" = "20:00"
        "CustomerId" = "demo-customer-id"
        "Note" = "Kiem thu dat san giu cho"
    }

    $createRes = Invoke-WebRequest -Uri $createUrl -Method Post -Body $postBody -WebSession $session -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Write-Host "3. POST Create Booking Status: $($createRes.StatusCode)" -ForegroundColor Green
    
    $location = $createRes.Headers["Location"]
    Write-Host "4. Redirect Location: $location" -ForegroundColor Cyan

    if ($location -and $location.Contains("Checkout")) {
        Write-Host "SUCCESS! Booking reserved and redirected to Checkout: $location" -ForegroundColor Green
        
        # 3. GET Checkout Page
        $checkoutUrl = $Url + $location
        $checkoutRes = Invoke-WebRequest -Uri $checkoutUrl -WebSession $session -UseBasicParsing
        Write-Host "5. GET Checkout Status: $($checkoutRes.StatusCode)" -ForegroundColor Green
        if ($checkoutRes.Content.Contains("VietBank Digital Sandbox") -and $checkoutRes.Content.Contains("SubmitVirtualTransfer")) {
            Write-Host "SUCCESS! Checkout page loaded with Virtual Bank & Upload Proof!" -ForegroundColor Green
        }
    } else {
        Write-Host "FAILED! Booking was not redirected to Checkout. Content snippet: $($createRes.Content)" -ForegroundColor Red
    }

} catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
} finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
    Get-Process -Name "DACN" -ErrorAction SilentlyContinue | Stop-Process -Force
}

