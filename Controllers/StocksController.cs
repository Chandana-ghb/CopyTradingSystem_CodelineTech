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
        public async Task<IActionResult> GetChartHistory(string symbol, [FromQuery] int limit = 500)
        {
            var candles = await ReadHistoricalCandlesAsync(symbol, limit <= 0 ? 500 : limit);
            
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
            if (System.IO.File.Exists(targetFile))
            {
                var lines = System.IO.File.ReadAllLines(targetFile);
                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(',');
                    if (parts.Length >= 6 &&
                        long.TryParse(parts[0], out long ts) &&
                        decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal open) &&
                        decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal high) &&
                        decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal low) &&
                        decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal close) &&
                        long.TryParse(parts[5], out long vol))
                    {
                        // STRICTLY ONLY CANDLES FROM TODAY'S MARKET OPEN (09:15 / 09:00 IST) ONWARDS
                        if (ts >= todayOpenEpoch)
                        {
                            var dt = DateTimeOffset.FromUnixTimeSeconds(ts).ToOffset(TimeSpan.FromHours(5.5));
                            list.Add(new HistoricalCandleDto
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
                }
            }

            // If file doesn't have today's real candles yet, fetch directly from Fyers API for today
            if (list.Count == 0)
            {
                var fetched = await FetchTodayFyersCandlesAsync(simpleName, isMcx, todayIst, todayOpenEpoch);
                if (fetched.Count > 0)
                {
                    list.AddRange(fetched);
                    try
                    {
                        var outLines = new List<string> { "Timestamp,Open,High,Low,Close,Volume" };
                        foreach (var c in fetched)
                        {
                            outLines.Add($"{c.Timestamp},{c.Open.ToString(CultureInfo.InvariantCulture)},{c.High.ToString(CultureInfo.InvariantCulture)},{c.Low.ToString(CultureInfo.InvariantCulture)},{c.Close.ToString(CultureInfo.InvariantCulture)},{c.Volume}");
                        }
                        System.IO.File.WriteAllLines(targetFile, outLines);
                    }
                    catch { }
                }
            }

            // Merge in-memory active live candle from Fyers live market feed if present
            var activeCandle = FyersLiveMarketService.GetActiveCandle(symbol);
            if (activeCandle != null && activeCandle.MinuteTimestamp >= todayOpenEpoch)
            {
                if (list.Count > 0 && list[^1].Timestamp == activeCandle.MinuteTimestamp)
                {
                    list[^1].High = Math.Max(list[^1].High, activeCandle.High);
                    list[^1].Low = Math.Min(list[^1].Low, activeCandle.Low);
                    list[^1].Close = activeCandle.Close;
                    list[^1].Volume = Math.Max(list[^1].Volume, activeCandle.Volume);
                }
                else if (list.Count == 0 || activeCandle.MinuteTimestamp > list[^1].Timestamp)
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

            // Strictly filter and sort ascending by time
            list = list.Where(c => c.Timestamp >= todayOpenEpoch)
                       .OrderBy(c => c.Timestamp)
                       .ToList();

            return list;
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
                        "GOLD" => "MCX:GOLD26OCTFUT",
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

                string url = $"https://api-t1.fyers.in/data/history?symbol={Uri.EscapeDataString(fyersSymbol)}&resolution=1&date_format=1&range_from={todayIst:yyyy-MM-dd}&range_to={todayIst:yyyy-MM-dd}&cont_flag=1";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Authorization", $"{appId}:{token}");
                var resp = await _httpClient.SendAsync(req);
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
                            if (ts >= todayOpenEpoch)
                            {
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
                    }
                }
            }
            catch { }
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
