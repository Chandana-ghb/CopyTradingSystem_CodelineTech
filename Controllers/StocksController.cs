using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FyersCopyTrading.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace FyersCopyTrading.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StocksController : ControllerBase
    {
        private readonly HistoricalDataService _historicalService;
        private readonly IConfiguration _config;
        private static readonly HttpClient _httpClient = CreateFyersHttpClient();

        private static HttpClient CreateFyersHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            return client;
        }

        public StocksController(HistoricalDataService historicalService, IConfiguration config)
        {
            _historicalService = historicalService;
            _config = config;
        }

        [HttpPost("regenerate-1min")]
        public async Task<IActionResult> Regenerate1MinData()
        {
            await _historicalService.InitializeAllHistoricalDataAsync();
            return Ok(new { message = "1-minute historical data successfully generated for all MCX and Nifty 50 stocks." });
        }

        [HttpGet]
        public IActionResult GetStocks([FromQuery] string? category)
        {
            // Sync with latest candle so LTP reflects the latest live data
            MockTickService.SyncWithHistoricalData();

            var query = MockTickService.Stocks.Values.AsEnumerable();

            if (!string.IsNullOrEmpty(category))
            {
                query = query.Where(s => s.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
            }

            var stocksList = query.Select(s => new
            {
                symbol = s.Symbol,
                name = s.Name,
                category = s.Category,
                price = s.Price,
                high = s.High,
                low = s.Low,
                prevClose = s.PrevClose,
                change = s.Change,
                changePercent = s.ChangePercent,
                timestamp = string.IsNullOrEmpty(s.LastTime) ? DateTime.Now.ToString("HH:mm:ss") : s.LastTime
            }).ToList();

            return Ok(stocksList);
        }

        [HttpGet("ltp/{symbol}")]
        public async Task<IActionResult> GetStockLtp(string symbol)
        {
            var candles = await ReadHistoricalCandlesAsync(symbol, limit: 1);
            if (candles.Count > 0)
            {
                var latest = candles[^1];
                return Ok(new
                {
                    symbol,
                    price = latest.Close,
                    open = latest.Open,
                    high = latest.High,
                    low = latest.Low,
                    close = latest.Close,
                    volume = latest.Volume,
                    timestamp = latest.Timestamp,
                    datetime = latest.Datetime
                });
            }
            return NotFound(new { message = $"No candle found for {symbol}" });
        }

        [HttpGet("historical/{symbol}")]
        public async Task<IActionResult> GetHistoricalData(string symbol, [FromQuery] int limit = 0)
        {
            var candles = await ReadHistoricalCandlesAsync(symbol, limit);
            return Ok(candles);
        }

        [HttpGet("chart-history/{symbol}")]
        public async Task<IActionResult> GetChartHistory(string symbol, [FromQuery] int limit = 0)
        {
            var candles = await ReadHistoricalCandlesAsync(symbol, limit);
            
            // Format for TradingView lightweight-charts (time, open, high, low, close)
            var chartData = candles.Select(c => new
            {
                time = c.Timestamp,
                open = c.Open,
                high = c.High,
                low = c.Low,
                close = c.Close,
                volume = c.Volume
            }).ToList();

            return Ok(chartData);
        }

        private async Task<List<HistoricalCandleDto>> ReadHistoricalCandlesAsync(string symbol, int limit = 0)
        {
            string cleanSymbol = Uri.UnescapeDataString(symbol).Trim();
            // Handle both "MCX:GOLD" and "GOLD", or "NSE:TCS-EQ" and "TCS"
            string simpleName = cleanSymbol
                .Replace("MCX:", "")
                .Replace("NSE:", "")
                .Replace("-EQ", "");

            if (simpleName.Equals("NATURALGAS", StringComparison.OrdinalIgnoreCase))
            {
                simpleName = "NATGAS";
            }

            bool isMcx = cleanSymbol.StartsWith("MCX:", StringComparison.OrdinalIgnoreCase) || symbol.StartsWith("MCX:", StringComparison.OrdinalIgnoreCase);

            var nowUtc = DateTimeOffset.UtcNow;
            var nowIst = nowUtc.ToOffset(TimeSpan.FromHours(5.5));
            var todayIst = nowIst.Date;
            var openTimeIst = isMcx ? todayIst.AddHours(9) : todayIst.AddHours(9).AddMinutes(15);
            long todayOpenEpoch = new DateTimeOffset(openTimeIst, TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();

            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            string targetDir = isMcx ? Path.Combine(baseDir, "MCX") : Path.Combine(baseDir, "Nifty50");
            Directory.CreateDirectory(targetDir);

            string[] possibleNames = { simpleName, simpleName.Replace("-", ""), simpleName.Replace("&", "") };
            string targetFile = Path.Combine(targetDir, $"{simpleName}.txt");
            foreach (var name in possibleNames)
            {
                var candidate = Path.Combine(targetDir, $"{name}.txt");
                if (System.IO.File.Exists(candidate)) { targetFile = candidate; break; }
            }

            var list = new List<HistoricalCandleDto>();

            // 1. FIRST: Load all historical data available in the .txt file (preserves all previous days/history)
            if (System.IO.File.Exists(targetFile))
            {
                var lines = System.IO.File.ReadAllLines(targetFile);
                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(',');
                    if (parts.Length >= 6)
                    {
                        long ts = 0;
                        string dtStr = "";
                        if (long.TryParse(parts[0], out long parsedTs))
                        {
                            ts = parsedTs;
                            var dt = DateTimeOffset.FromUnixTimeSeconds(ts).ToOffset(TimeSpan.FromHours(5.5));
                            dtStr = dt.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                        else if (DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDt))
                        {
                            var dto = new DateTimeOffset(parsedDt, TimeSpan.FromHours(5.5));
                            ts = dto.ToUnixTimeSeconds();
                            dtStr = parsedDt.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                        else if (DateTime.TryParse(parts[0], out DateTime fallbackDt))
                        {
                            var dto = new DateTimeOffset(fallbackDt, TimeSpan.FromHours(5.5));
                            ts = dto.ToUnixTimeSeconds();
                            dtStr = fallbackDt.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                        else
                        {
                            continue;
                        }

                        if (decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal open) &&
                            decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal high) &&
                            decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal low) &&
                            decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal close) &&
                            long.TryParse(parts[5], out long vol))
                        {
                            list.Add(new HistoricalCandleDto
                            {
                                Timestamp = ts,
                                Datetime = dtStr,
                                Open = open,
                                High = high,
                                Low = low,
                                Close = close,
                                Volume = vol
                            });
                        }
                    }
                }

                if (limit > 0 && list.Count > limit)
                {
                    list = list.TakeLast(limit).ToList();
                }
            }

            // 2. SECOND: Fetch today's live continuous candles from Fyers API and merge without overwriting past history
            bool useLiveFyers = _config.GetValue<bool>("MarketData:UseLiveFyers", true);
            if (useLiveFyers)
            {
                var fetched = await FetchTodayFyersCandlesAsync(simpleName, isMcx, todayIst, todayOpenEpoch);
                if (fetched.Count > 0)
                {
                    var existingMap = list.GroupBy(c => c.Timestamp).ToDictionary(g => g.Key, g => g.Last());
                    var newCandlesToAppend = new List<HistoricalCandleDto>();

                    foreach (var f in fetched)
                    {
                        if (existingMap.TryGetValue(f.Timestamp, out var match))
                        {
                            match.Open = f.Open;
                            match.High = f.High;
                            match.Low = f.Low;
                            match.Close = f.Close;
                            match.Volume = f.Volume;
                        }
                        else
                        {
                            list.Add(f);
                            newCandlesToAppend.Add(f);
                        }
                    }

                    // Append any new completed candles to the .txt file so past history is NEVER lost
                    if (newCandlesToAppend.Count > 0 && System.IO.File.Exists(targetFile))
                    {
                        try
                        {
                            var appendLines = newCandlesToAppend.Select(c =>
                                $"{c.Datetime},{c.Open.ToString(CultureInfo.InvariantCulture)},{c.High.ToString(CultureInfo.InvariantCulture)},{c.Low.ToString(CultureInfo.InvariantCulture)},{c.Close.ToString(CultureInfo.InvariantCulture)},{c.Volume}"
                            );
                            System.IO.File.AppendAllLines(targetFile, appendLines);
                        }
                        catch { }
                    }
                    else if (!System.IO.File.Exists(targetFile))
                    {
                        try
                        {
                            var outLines = new List<string> { "Timestamp,Open,High,Low,Close,Volume" };
                            foreach (var c in list)
                            {
                                outLines.Add($"{c.Datetime},{c.Open.ToString(CultureInfo.InvariantCulture)},{c.High.ToString(CultureInfo.InvariantCulture)},{c.Low.ToString(CultureInfo.InvariantCulture)},{c.Close.ToString(CultureInfo.InvariantCulture)},{c.Volume}");
                            }
                            System.IO.File.WriteAllLines(targetFile, outLines);
                        }
                        catch { }
                    }
                }
            }

            // 3. THIRD: Merge in-memory active live forming candle from Fyers live market feed
            var activeCandle = FyersLiveMarketService.GetActiveCandle(symbol);
            if (activeCandle != null)
            {
                var match = list.FirstOrDefault(c => c.Timestamp == activeCandle.MinuteTimestamp);
                if (match != null)
                {
                    match.High = Math.Max(match.High, activeCandle.High);
                    match.Low = Math.Min(match.Low, activeCandle.Low);
                    match.Close = activeCandle.Close;
                    match.Volume = Math.Max(match.Volume, activeCandle.Volume);
                }
                else
                {
                    var dt = DateTimeOffset.FromUnixTimeSeconds(activeCandle.MinuteTimestamp).ToOffset(TimeSpan.FromHours(5.5));
                    list.Add(new HistoricalCandleDto
                    {
                        Timestamp = activeCandle.MinuteTimestamp,
                        Datetime = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                        Open = activeCandle.Open,
                        High = activeCandle.High,
                        Low = activeCandle.Low,
                        Close = activeCandle.Close,
                        Volume = activeCandle.Volume
                    });
                }
            }

            // Deduplicate by timestamp and sort ascending by time
            list = list.GroupBy(c => c.Timestamp)
                       .Select(g => g.Last())
                       .OrderBy(c => c.Timestamp)
                       .ToList();

            // Ensure continuity: fill any missing intraday minutes so the chart has zero gaps between candles
            list = EnsureIntradayContinuity(list, isMcx);

            return list;
        }

        // Fills any minute gaps during active market trading session with flat continuation (standard for live financial charting)
        private static List<HistoricalCandleDto> EnsureIntradayContinuity(List<HistoricalCandleDto> candles, bool isMcx = false)
        {
            if (candles.Count == 0) return candles;
            var result = new List<HistoricalCandleDto>();

            // MCX market opens at 09:00 AM IST; NSE market opens at 09:15 AM IST
            int marketOpenHour = isMcx ? 9 : 9;
            int marketOpenMinute = isMcx ? 0 : 15;

            for (int i = 0; i < candles.Count; i++)
            {
                var curr = candles[i];
                var currIst = DateTimeOffset.FromUnixTimeSeconds(curr.Timestamp).ToOffset(TimeSpan.FromHours(5.5));

                if (result.Count == 0)
                {
                    // Check if the very first candle of the first day is after market open
                    var firstDayOpenIst = new DateTimeOffset(currIst.Year, currIst.Month, currIst.Day, marketOpenHour, marketOpenMinute, 0, TimeSpan.FromHours(5.5));
                    long firstDayOpenEpoch = firstDayOpenIst.ToUnixTimeSeconds();
                    if (curr.Timestamp > firstDayOpenEpoch && (curr.Timestamp - firstDayOpenEpoch) <= 28800) // within 8 hours
                    {
                        long fillTime = firstDayOpenEpoch;
                        while (fillTime < curr.Timestamp)
                        {
                            var dt = DateTimeOffset.FromUnixTimeSeconds(fillTime).ToOffset(TimeSpan.FromHours(5.5));
                            result.Add(new HistoricalCandleDto
                            {
                                Timestamp = fillTime,
                                Datetime = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                                Open = curr.Open,
                                High = curr.Open,
                                Low = curr.Open,
                                Close = curr.Open,
                                Volume = 0
                            });
                            fillTime += 60;
                        }
                    }
                }
                else
                {
                    var prev = result[^1];
                    var prevIst = DateTimeOffset.FromUnixTimeSeconds(prev.Timestamp).ToOffset(TimeSpan.FromHours(5.5));

                    if (prevIst.Date == currIst.Date)
                    {
                        // Same trading day: fill ANY missing minute gap within this day's session
                        long diff = curr.Timestamp - prev.Timestamp;
                        if (diff > 60)
                        {
                            long fillTime = prev.Timestamp + 60;
                            while (fillTime < curr.Timestamp)
                            {
                                var dt = DateTimeOffset.FromUnixTimeSeconds(fillTime).ToOffset(TimeSpan.FromHours(5.5));
                                result.Add(new HistoricalCandleDto
                                {
                                    Timestamp = fillTime,
                                    Datetime = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                                    Open = prev.Close,
                                    High = prev.Close,
                                    Low = prev.Close,
                                    Close = prev.Close,
                                    Volume = 0
                                });
                                fillTime += 60;
                            }
                        }
                    }
                    else
                    {
                        // Overnight transition: market opened anew on currIst.Date
                        var dayOpenIst = new DateTimeOffset(currIst.Year, currIst.Month, currIst.Day, marketOpenHour, marketOpenMinute, 0, TimeSpan.FromHours(5.5));
                        long dayOpenEpoch = dayOpenIst.ToUnixTimeSeconds();
                        if (curr.Timestamp > dayOpenEpoch)
                        {
                            long fillTime = dayOpenEpoch;
                            while (fillTime < curr.Timestamp)
                            {
                                var dt = DateTimeOffset.FromUnixTimeSeconds(fillTime).ToOffset(TimeSpan.FromHours(5.5));
                                result.Add(new HistoricalCandleDto
                                {
                                    Timestamp = fillTime,
                                    Datetime = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                                    Open = curr.Open,
                                    High = curr.Open,
                                    Low = curr.Open,
                                    Close = curr.Open,
                                    Volume = 0
                                });
                                fillTime += 60;
                            }
                        }
                    }
                }

                result.Add(curr);
            }

            // Extend up to current minute if market is currently active today
            if (result.Count > 0)
            {
                var last = result[^1];
                var nowIst = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5));
                var lastIst = DateTimeOffset.FromUnixTimeSeconds(last.Timestamp).ToOffset(TimeSpan.FromHours(5.5));

                if (lastIst.Date == nowIst.Date)
                {
                    long currentMinuteEpoch = (nowIst.ToUnixTimeSeconds() / 60) * 60;
                    if (currentMinuteEpoch > last.Timestamp)
                    {
                        long fillTime = last.Timestamp + 60;
                        while (fillTime <= currentMinuteEpoch)
                        {
                            var dt = DateTimeOffset.FromUnixTimeSeconds(fillTime).ToOffset(TimeSpan.FromHours(5.5));
                            result.Add(new HistoricalCandleDto
                            {
                                Timestamp = fillTime,
                                Datetime = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                                Open = last.Close,
                                High = last.Close,
                                Low = last.Close,
                                Close = last.Close,
                                Volume = 0
                            });
                            fillTime += 60;
                        }
                    }
                }
            }

            return result;
        }

        private async Task<List<HistoricalCandleDto>> FetchTodayFyersCandlesAsync(string cleanName, bool isMcx, DateTime todayIst, long todayOpenEpoch)
        {
            var result = new List<HistoricalCandleDto>();
            try
            {
                string appId = _config["Fyers:AppId"] ?? "ZZQW1QXQFO-100";
                string token = _config["Fyers:AccessToken"] ?? "";
                if (string.IsNullOrEmpty(token)) return result;

                string fyersSymbol;
                if (isMcx)
                {
                    fyersSymbol = cleanName.ToUpperInvariant() switch
                    {
                        "GOLD" => "MCX:GOLD26DECFUT",
                        "SILVER" => "MCX:SILVER26DECFUT",
                        "CRUDEOIL" => "MCX:CRUDEOIL26OCTFUT",
                        "NATGAS" or "NATURALGAS" => "MCX:NATURALGAS26OCTFUT",
                        "COPPER" => "MCX:COPPER26OCTFUT",
                        "ZINC" => "MCX:ZINC26OCTFUT",
                        "LEAD" => "MCX:LEAD26OCTFUT",
                        "ALUMINIUM" => "MCX:ALUMINIUM26OCTFUT",
                        "NICKEL" => "MCX:NICKEL26OCTFUT",
                        _ => $"MCX:{cleanName}26OCTFUT"
                    };
                }
                else
                {
                    fyersSymbol = cleanName.ToUpperInvariant() switch
                    {
                        "MM" or "M&M" => "NSE:M&M-EQ",
                        "BAJAJAUTO" or "BAJAJ-AUTO" => "NSE:BAJAJ-AUTO-EQ",
                        _ => $"NSE:{cleanName}-EQ"
                    };
                }

                string fromDate = todayIst.ToString("yyyy-MM-dd");
                string toDate = todayIst.ToString("yyyy-MM-dd");
                string url = $"https://api-t1.fyers.in/data/history?symbol={Uri.EscapeDataString(fyersSymbol)}&resolution=1&date_format=1&range_from={fromDate}&range_to={toDate}&cont_flag=1";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Authorization", $"{appId}:{token}");
                var resp = await _httpClient.SendAsync(req);
                Console.WriteLine($"[Fyers History] {fyersSymbol} -> Status: {resp.StatusCode}");

                if (!resp.IsSuccessStatusCode)
                {
                    string errBody = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"[Fyers History Failed] {errBody}");
                    fromDate = todayIst.AddDays(-4).ToString("yyyy-MM-dd");
                    url = $"https://api-t1.fyers.in/data/history?symbol={Uri.EscapeDataString(fyersSymbol)}&resolution=1&date_format=1&range_from={fromDate}&range_to={toDate}&cont_flag=1";
                    req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.TryAddWithoutValidation("Authorization", $"{appId}:{token}");
                    resp = await _httpClient.SendAsync(req);
                    Console.WriteLine($"[Fyers History Retry] {fyersSymbol} -> Status: {resp.StatusCode}");
                }

                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("s", out var s) && s.GetString() == "ok" && root.TryGetProperty("candles", out var candles))
                    {
                        foreach (var arr in candles.EnumerateArray())
                        {
                            long ts = arr[0].GetInt64();
                            decimal open = arr[1].GetDecimal();
                            decimal high = arr[2].GetDecimal();
                            decimal low = arr[3].GetDecimal();
                            decimal close = arr[4].GetDecimal();
                            long vol = arr[5].GetInt64();
                            var dt = DateTimeOffset.FromUnixTimeSeconds(ts).ToOffset(TimeSpan.FromHours(5.5));
                            result.Add(new HistoricalCandleDto
                            {
                                Timestamp = ts,
                                Datetime = dt.ToString("yyyy-MM-dd HH:mm:ss"),
                                Open = open,
                                High = high,
                                Low = low,
                                Close = close,
                                Volume = vol
                            });
                        }
                    }
                    Console.WriteLine($"[Fyers History Success] {fyersSymbol} -> Parsed {result.Count} candles");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fyers History Exception] {ex.Message}");
            }
            return result;
        }
    }

    public class HistoricalCandleDto
    {
        public long Timestamp { get; set; }
        public string Datetime { get; set; } = string.Empty;
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
    }
}
