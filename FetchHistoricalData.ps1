# ============================================================
# FetchHistoricalData.ps1
# Fetches historical OHLCV candles from Fyers API v3
# Saves to Data/MCX/*.txt and Data/Nifty50/*.txt
# ============================================================

$AppId       = "ZZQW1QXQFO-100"
$AppSecret   = "NUIT5XL6IP"
$AccessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOlsiZDoxIiwiZDoyIiwieDowIiwieDoxIl0sImF0X2hhc2giOiJnQUFBQUFCcXd4b21nb1d0REM2WnpqSy0zVmtlT09fQV96QVdvbXhMR2xZLTBiSFVOSERhZ0RfMzdvTGZiV1ZuZXlwTGg5MjNBRE1TUWxWYzhJQ3F2WmphWnV5VnEyZkNDLVRQNksyRFFwU25RcTRFOGRqMThoOD0iLCJkaXNwbGF5X25hbWUiOiIiLCJvbXMiOiJLMSIsImhzbV9rZXkiOiIyM2E2MmEzNDM1NTlkOGIzYWNiYmFmZjZjZTljYWMwMDc4NDFiODM2MGNmMmM4ZmY5ZDc3NjZhMCIsImlzRGRwaUVuYWJsZWQiOiJOIiwiaXNNdGZFbmFibGVkIjoiTiIsImZ5X2lkIjoiRkFLODg0NTUiLCJhcHBUeXBlIjoxMDAsImV4cCI6MTc5MTI0NjYwMCwiaWF0IjoxNzkxMTcxMTEwLCJpc3MiOiJhcGkuZnllcnMuaW4iLCJuYmYiOjE3OTExNzExMTAsInN1YiI6ImFjY2Vzc190b2tlbiJ9.pj6ceuoPzskOh4V824zpn282EV3tWQfB14OLPy68uX8"

# Fyers auth header = "AppId:AccessToken"
$AuthHeader  = "$AppId" + ":" + "$AccessToken"

$BaseUrl     = "https://api.fyers.in/data-rest/v3/history/"
$DateFrom    = "2026-09-01"
$DateTo      = "2026-10-01"
$Resolution  = "60"    # 60-minute candles

$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$McxDir      = Join-Path $ScriptDir "Data\MCX"
$Nifty50Dir  = Join-Path $ScriptDir "Data\Nifty50"

New-Item -ItemType Directory -Force -Path $McxDir    | Out-Null
New-Item -ItemType Directory -Force -Path $Nifty50Dir | Out-Null

# ------------------------------------------------------------------
# MCX Futures symbols
# ------------------------------------------------------------------
$McxSymbols = [ordered]@{
    "GOLD"      = "MCX:GOLD25OCTFUT"
    "SILVER"    = "MCX:SILVER25DECFUT"
    "NATGAS"    = "MCX:NATURALGAS25OCTFUT"
    "CRUDEOIL"  = "MCX:CRUDEOIL25OCTFUT"
    "COPPER"    = "MCX:COPPER25OCTFUT"
    "ZINC"      = "MCX:ZINC25OCTFUT"
    "LEAD"      = "MCX:LEAD25OCTFUT"
    "ALUMINIUM" = "MCX:ALUMINIUM25OCTFUT"
    "NICKEL"    = "MCX:NICKEL25OCTFUT"
}

# ------------------------------------------------------------------
# Nifty 50 Equities
# ------------------------------------------------------------------
$Nifty50Symbols = [ordered]@{
    "RELIANCE"   = "NSE:RELIANCE-EQ"
    "TCS"        = "NSE:TCS-EQ"
    "HDFCBANK"   = "NSE:HDFCBANK-EQ"
    "INFY"       = "NSE:INFY-EQ"
    "ICICIBANK"  = "NSE:ICICIBANK-EQ"
    "KOTAKBANK"  = "NSE:KOTAKBANK-EQ"
    "SBIN"       = "NSE:SBIN-EQ"
    "AXISBANK"   = "NSE:AXISBANK-EQ"
    "ITC"        = "NSE:ITC-EQ"
    "LT"         = "NSE:LT-EQ"
    "WIPRO"      = "NSE:WIPRO-EQ"
    "HCLTECH"    = "NSE:HCLTECH-EQ"
    "BAJFINANCE" = "NSE:BAJFINANCE-EQ"
    "MARUTI"     = "NSE:MARUTI-EQ"
    "ASIANPAINT" = "NSE:ASIANPAINT-EQ"
    "TITAN"      = "NSE:TITAN-EQ"
    "SUNPHARMA"  = "NSE:SUNPHARMA-EQ"
    "ULTRACEMCO" = "NSE:ULTRACEMCO-EQ"
    "ONGC"       = "NSE:ONGC-EQ"
    "NTPC"       = "NSE:NTPC-EQ"
    "POWERGRID"  = "NSE:POWERGRID-EQ"
    "MM"         = "NSE:M&M-EQ"
    "BHARTIARTL" = "NSE:BHARTIARTL-EQ"
    "TATASTEEL"  = "NSE:TATASTEEL-EQ"
    "TATAMOTORS" = "NSE:TATAMOTORS-EQ"
    "TATACONSUM" = "NSE:TATACONSUM-EQ"
    "NESTLEIND"  = "NSE:NESTLEIND-EQ"
    "CIPLA"      = "NSE:CIPLA-EQ"
    "DRREDDY"    = "NSE:DRREDDY-EQ"
    "DIVISLAB"   = "NSE:DIVISLAB-EQ"
    "EICHERMOT"  = "NSE:EICHERMOT-EQ"
    "HEROMOTOCO" = "NSE:HEROMOTOCO-EQ"
    "BAJAJAUTO"  = "NSE:BAJAJ-AUTO-EQ"
    "BAJAJFINSV" = "NSE:BAJAJFINSV-EQ"
    "COALINDIA"  = "NSE:COALINDIA-EQ"
    "ADANIENT"   = "NSE:ADANIENT-EQ"
    "ADANIPORTS" = "NSE:ADANIPORTS-EQ"
    "JSWSTEEL"   = "NSE:JSWSTEEL-EQ"
    "HINDALCO"   = "NSE:HINDALCO-EQ"
    "GRASIM"     = "NSE:GRASIM-EQ"
    "BPCL"       = "NSE:BPCL-EQ"
    "TECHM"      = "NSE:TECHM-EQ"
    "APOLLOHOSP" = "NSE:APOLLOHOSP-EQ"
    "BRITANNIA"  = "NSE:BRITANNIA-EQ"
    "INDUSINDBK" = "NSE:INDUSINDBK-EQ"
    "PIDILITIND" = "NSE:PIDILITIND-EQ"
    "SIEMENS"    = "NSE:SIEMENS-EQ"
    "SHREECEM"   = "NSE:SHREECEM-EQ"
    "HDFCLIFE"   = "NSE:HDFCLIFE-EQ"
    "SBILIFE"    = "NSE:SBILIFE-EQ"
}

# ------------------------------------------------------------------
# Helper function
# ------------------------------------------------------------------
function Fetch-AndSave {
    param (
        [string]$FileName,
        [string]$Symbol,
        [string]$OutputDir
    )

    $Url = $BaseUrl + "?symbol=" + [System.Web.HttpUtility]::UrlEncode($Symbol) `
         + "&resolution=" + $Resolution `
         + "&date_format=1" `
         + "&range_from=" + $DateFrom `
         + "&range_to=" + $DateTo `
         + "&cont_flag=1"

    Write-Host "  Fetching $Symbol ..." -ForegroundColor Cyan

    try {
        $Headers  = @{
            "Authorization" = $AuthHeader
            "Content-Type"  = "application/json"
        }
        $Response = Invoke-RestMethod -Uri $Url -Headers $Headers -Method GET -ErrorAction Stop

        if ($Response.s -ne "ok") {
            Write-Warning "  [!] API error for $Symbol : code=$($Response.code) msg=$($Response.message)"
            return
        }

        $Candles = $Response.candles
        if (-not $Candles -or $Candles.Count -eq 0) {
            Write-Warning "  [!] No candles returned for $Symbol"
            return
        }

        $FilePath = Join-Path $OutputDir "$FileName.txt"
        $Lines    = @("Timestamp,Open,High,Low,Close,Volume")
        foreach ($c in $Candles) {
            $Lines += "$($c[0]),$($c[1]),$($c[2]),$($c[3]),$($c[4]),$($c[5])"
        }
        $Lines | Set-Content -Path $FilePath -Encoding UTF8
        Write-Host "  [OK]  $($Candles.Count) candles -> $FilePath" -ForegroundColor Green
    }
    catch {
        Write-Warning "  [ERROR] $Symbol : $_"
    }
}

Add-Type -AssemblyName System.Web

Write-Host "`n===== Fetching MCX Symbols =====" -ForegroundColor Yellow
foreach ($entry in $McxSymbols.GetEnumerator()) {
    Fetch-AndSave -FileName $entry.Key -Symbol $entry.Value -OutputDir $McxDir
    Start-Sleep -Milliseconds 400
}

Write-Host "`n===== Fetching Nifty50 Symbols =====" -ForegroundColor Yellow
foreach ($entry in $Nifty50Symbols.GetEnumerator()) {
    Fetch-AndSave -FileName $entry.Key -Symbol $entry.Value -OutputDir $Nifty50Dir
    Start-Sleep -Milliseconds 400
}

Write-Host "`n===== All Done =====" -ForegroundColor Magenta
