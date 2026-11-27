$Url = "http://127.0.0.1:5006"
$ErrorActionPreference = "Continue"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  DATSAN247 - END-TO-END AUTOMATED TEST SUITE" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Kill any previous DACN processes
Get-Process -Name "DACN" -ErrorAction SilentlyContinue | Stop-Process -Force

$workDir = $PSScriptRoot
if (-not $workDir) { $workDir = Get-Location }
$dllPath = Join-Path $workDir "bin\Debug\net8.0\DACN.dll"
$process = Start-Process -FilePath "dotnet" -ArgumentList "`"$dllPath`" --urls `"$Url`"" -WorkingDirectory $workDir -PassThru -NoNewWindow

Write-Host "Waiting 5 seconds for application to start..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

$endpoints = @(
    @{ Name = "Trang chu (Home)"; Path = "/" },
    @{ Name = "Tim kiem san (Booking Index)"; Path = "/Booking" },
    @{ Name = "Loc san theo mon (Filter Sport)"; Path = "/Booking?sportTypeId=1" },
    @{ Name = "Ma tran lich san (Matrix)"; Path = "/Booking/Matrix?courtId=1" },
    @{ Name = "Chi tiet cum san (Facility Detail)"; Path = "/Booking/FacilityDetail/1" },
    @{ Name = "Thanh toan VietQR (Checkout)"; Path = "/Booking/Checkout?bookingId=1" },
    @{ Name = "Ve dien tu (Confirmation)"; Path = "/Booking/Confirmation?bookingId=1" },
    @{ Name = "Ve cua toi (My Bookings)"; Path = "/Booking/MyBookings" },
    @{ Name = "Bang tin ghep doi (Teamwork)"; Path = "/Teamwork" },
    @{ Name = "Chi tiet keo (Teamwork Detail)"; Path = "/Teamwork/Detail/1" },
    @{ Name = "Dang keo moi (Teamwork Create)"; Path = "/Teamwork/Create" },
    @{ Name = "Keo cua toi (My Matches)"; Path = "/Teamwork/MyMatches" },
    @{ Name = "Ho so ca nhan (Account Profile)"; Path = "/Account/Profile" },
    @{ Name = "Dang nhap (Account Login)"; Path = "/Account/Login" },
    @{ Name = "Dang ky (Account Register)"; Path = "/Account/Register" },
    @{ Name = "Kenh Chu San - Danh sach (Owner Index)"; Path = "/Owner/Facility" },
    @{ Name = "Kenh Chu San - Tao cum san (Create Facility)"; Path = "/Owner/Facility/CreateFacility" },
    @{ Name = "Kenh Chu San - Sua cum san (Edit Facility)"; Path = "/Owner/Facility/EditFacility/1" },
    @{ Name = "Kenh Chu San - Quan ly don dat (Bookings)"; Path = "/Owner/Facility/Bookings" },
    @{ Name = "Kenh Chu San - Timeline Schedule"; Path = "/Owner/Facility/ScheduleBoard?facilityId=1" },
    @{ Name = "Kenh Chu San - Dich vu tien ich (Services)"; Path = "/Owner/Facility/ManageServices?facilityId=1" },
    @{ Name = "Kenh Chu San - Bang gia (TimeSlots)"; Path = "/Owner/Facility/ManageTimeSlots?courtId=1" },
    @{ Name = "Kenh Quan Tri - Quan ly tai khoan (Admin User)"; Path = "/Admin/User" },
    @{ Name = "Kenh Quan Tri - Ngan Hang Ao Admin (Bank Ledger)"; Path = "/Admin/Bank" }
)

$passed = 0
$failed = 0

foreach ($ep in $endpoints) {
    $targetUrl = $Url + $ep.Path
    $name = $ep.Name
    $path = $ep.Path
    try {
        $response = Invoke-WebRequest -Uri $targetUrl -UseBasicParsing -TimeoutSec 10
        if ($response.StatusCode -eq 200) {
            Write-Host "[PASS] 200 OK  $name ($path)" -ForegroundColor Green
            $passed++
        } else {
            Write-Host "[FAIL] $($response.StatusCode) $name ($path)" -ForegroundColor Red
            $failed++
        }
    } catch {
        Write-Host "[ERROR] $name ($path) : $_" -ForegroundColor Red
        $failed++
    }
}

# Test 1: AI Chat - Huong dan cach dung chi tiet
try {
    $aiBody1 = '{"message":"cach dung he thong"}'
    $aiRes1 = Invoke-RestMethod -Uri "$Url/api/ai/ask" -Method Post -ContentType "application/json" -Body $aiBody1 -TimeoutSec 10
    if ($aiRes1 -and $aiRes1.reply -and $aiRes1.reply.Length -gt 200 -and $aiRes1.actionUrl -eq "/Booking/Index") {
        Write-Host "[PASS] 200 OK  AI Chat: Huong dan cach dung chi tiet A-Z" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "[FAIL] AI Chat Huong dan failed" -ForegroundColor Red
        $failed++
    }
} catch {
    Write-Host "[ERROR] AI Chat Test 1 : $_" -ForegroundColor Red
    $failed++
}

# Test 2: AI Chat - Hoi duong chung chung -> AI hoi lai san nao
try {
    $aiBody2 = '{"message":"chi duong den san"}'
    $aiRes2 = Invoke-RestMethod -Uri "$Url/api/ai/ask" -Method Post -ContentType "application/json" -Body $aiBody2 -TimeoutSec 10
    if ($aiRes2 -and $aiRes2.reply -and $aiRes2.quickSuggestions -and $aiRes2.quickSuggestions.Count -gt 0 -and $aiRes2.actionUrl -eq "/Booking/Index") {
        Write-Host "[PASS] 200 OK  AI Chat: Hoi nguoc lai khach muon den san nao kem danh sach" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "[FAIL] AI Chat Hoi nguoc failed" -ForegroundColor Red
        $failed++
    }
} catch {
    Write-Host "[ERROR] AI Chat Test 2 : $_" -ForegroundColor Red
    $failed++
}

# Test 3: AI Chat - Neu ro ten san Chao Lua -> AI chi duong Google Maps cu the
try {
    $aiBody3 = '{"message":"chi duong den san chao lua"}'
    $aiRes3 = Invoke-RestMethod -Uri "$Url/api/ai/ask" -Method Post -ContentType "application/json" -Body $aiBody3 -TimeoutSec 10
    if ($aiRes3 -and $aiRes3.reply -and $aiRes3.actionUrl -and $aiRes3.actionUrl.Contains("google.com/maps")) {
        Write-Host "[PASS] 200 OK  AI Chat: Chi duong Google Maps truc tiep den dung san yeu cau" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "[FAIL] AI Chat Chi duong cu the failed" -ForegroundColor Red
        $failed++
    }
} catch {
    Write-Host "[ERROR] AI Chat Test 3 : $_" -ForegroundColor Red
    $failed++
}

# Test 4: Ngan Hang Ao 1-Click Sandbox API Simulation
try {
    $bankForm = @{
        bookingId = "1"
        senderName = "Nguyen Van A"
        senderBank = "VietBank Digital Sandbox"
        generatedBillImage = "data:image/svg+xml;base64,PHN2Zz48L3N2Zz4="
    }
    $bankRes = Invoke-RestMethod -Uri "$Url/Booking/ApiSimulateBankTransfer" -Method Post -Body $bankForm -TimeoutSec 10
    if ($bankRes -and $bankRes.success -eq $true -and $bankRes.redirectUrl.Contains("Confirmation")) {
        Write-Host "[PASS] 200 OK  Ngan Hang Ao: Giao dich chuyen tien 1-click & ghi nhan tien vao TK thanh cong" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "[FAIL] Ngan Hang Ao test failed: $($bankRes | ConvertTo-Json)" -ForegroundColor Red
        $failed++
    }
} catch {
    Write-Host "[ERROR] Ngan Hang Ao Test 4 : $_" -ForegroundColor Red
    $failed++
}

# Test 5: GET SubmitVirtualTransfer Redirect Safety
try {
    $getSafeRes = Invoke-WebRequest -Uri "$Url/Booking/SubmitVirtualTransfer?bookingId=1" -UseBasicParsing -TimeoutSec 10
    if ($getSafeRes.StatusCode -eq 200) {
        Write-Host "[PASS] 200 OK  SubmitVirtualTransfer: GET request duoc dieu huong an toan ve Checkout (Khong loi 405)" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "[FAIL] SubmitVirtualTransfer GET failed: StatusCode $($getSafeRes.StatusCode)" -ForegroundColor Red
        $failed++
    }
} catch {
    Write-Host "[ERROR] Test 5 : $_" -ForegroundColor Red
    $failed++
}

Write-Host "`n----------------------------------------------------------" -ForegroundColor Cyan
Write-Host "KET QUA KIEM THU: $passed PASSED, $failed FAILED (Tong so: $($endpoints.Count) endpoints)" -ForegroundColor Green
Write-Host "----------------------------------------------------------" -ForegroundColor Cyan

if ($process -and -not $process.HasExited) {
    Stop-Process -Id $process.Id -Force
}
Get-Process -Name "DACN" -ErrorAction SilentlyContinue | Stop-Process -Force

