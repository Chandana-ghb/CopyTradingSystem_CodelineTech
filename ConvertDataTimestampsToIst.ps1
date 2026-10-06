$targetFolders = @(
    "E:\CodeLine Tech\CopyTrading\Data\Nifty50",
    "E:\CodeLine Tech\CopyTrading\Data\MCX"
)

$istOffset = [TimeSpan]::FromHours(5.5)
$totalFiles = 0
$totalConvertedRows = 0

foreach ($folder in $targetFolders) {
    if (-not (Test-Path $folder)) { continue }
    $files = Get-ChildItem -Path $folder -Filter "*.txt"
    foreach ($file in $files) {
        $lines = [System.IO.File]::ReadAllLines($file.FullName)
        if ($lines.Length -le 1) { continue }

        $newLines = New-Object System.Collections.Generic.List[string]
        $newLines.Add($lines[0]) # Header

        $fileConverted = 0
        for ($i = 1; $i -lt $lines.Length; $i++) {
            $line = $lines[$i]
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $parts = $line.Split(',')
            if ($parts.Length -ge 6) {
                $rawTs = $parts[0]
                $ts = 0L
                if ([long]::TryParse($rawTs, [ref]$ts)) {
                    $dtStr = [DateTimeOffset]::FromUnixTimeSeconds($ts).ToOffset($istOffset).ToString("yyyy-MM-dd HH:mm:ss")
                    $parts[0] = $dtStr
                    $newLines.Add([string]::Join(',', $parts))
                    $fileConverted++
                } else {
                    $newLines.Add($line)
                }
            } else {
                $newLines.Add($line)
            }
        }

        if ($fileConverted -gt 0) {
            [System.IO.File]::WriteAllLines($file.FullName, $newLines)
            $totalFiles++
            $totalConvertedRows += $fileConverted
            Write-Host "Converted $($file.Name): $fileConverted rows to Indian Time (IST)"
        }
    }
}

Write-Host "=========================================="
Write-Host "Conversion Completed!"
Write-Host "Total Files Converted: $totalFiles"
Write-Host "Total Rows Converted: $totalConvertedRows"
Write-Host "=========================================="
