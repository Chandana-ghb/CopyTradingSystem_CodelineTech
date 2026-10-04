$token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOlsiZDoxIiwiZDoyIiwieDowIiwieDoxIl0sImF0X2hhc2giOiJnQUFBQUFCcXdNZzRKcUF2OWg2QXo0UXhMaU9oWXhkWlV6dDNnY204WU9sYUUzc09nQlFzLXE4TU1pWTU0MHhmTzFtbnluUUwxRFAyZl95eFduMndxVFRBdGx4eTd0ZHlfZFNzLUxWVTV1RHctbU9UcGJ2OHlmRT0iLCJkaXNwbGF5X25hbWUiOiIiLCJvbXMiOiJLMSIsImhzbV9rZXkiOiIyM2E2MmEzNDM1NTlkOGIzYWNiYmFmZjZjZTljYWMwMDc4NDFiODM2MGNmMmM4ZmY5ZDc3NjZhMCIsImlzRGRwaUVuYWJsZWQiOiJOIiwiaXNNdGZFbmFibGVkIjoiTiIsImZ5X2lkIjoiRkFLODg0NTUiLCJhcHBUeXBlIjoxMDAsImV4cCI6MTc5MTA3MzgwMCwiaWF0IjoxNzkxMDE5MDY0LCJpc3MiOiJhcGkuZnllcnMuaW4iLCJuYmYiOjE3OTEwMTkwNjQsInN1YiI6ImFjY2Vzc190b2tlbiJ9.zMuf9uomht8zhPOeNWJ7zOieUNC6ZKx4Y8V9_zeQ3SU"

$headers = @{ "Authorization" = $token }

# Try different possible URLs for Fyers v3 history
$urls = @(
    "https://api.fyers.in/data-rest/v3/history/?symbol=NSE:TCS-EQ&resolution=D&date_format=1&range_from=2026-09-01&range_to=2026-09-30&cont_flag=1",
    "https://api.fyers.in/api/v3/history/?symbol=NSE:TCS-EQ&resolution=D&date_format=1&range_from=2026-09-01&range_to=2026-09-30&cont_flag=1",
    "https://api-t1.fyers.in/data/history/?symbol=NSE:TCS-EQ&resolution=D&date_format=1&range_from=2026-09-01&range_to=2026-09-30&cont_flag=1",
    "https://api-t1.fyers.in/data-rest/v2/history/?symbol=NSE:TCS-EQ&resolution=D&date_format=1&range_from=2026-09-01&range_to=2026-09-30&cont_flag=1"
)

foreach ($url in $urls) {
    Write-Host "`nTesting: $url"
    try {
        $response = Invoke-RestMethod -Uri $url -Headers $headers -Method GET -TimeoutSec 10
        Write-Host "SUCCESS: " ($response | ConvertTo-Json -Depth 3)
    } catch {
        Write-Host "FAIL: $_"
    }
}
