using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FyersCopyTrading.Services;
using Microsoft.AspNetCore.Mvc;

namespace FyersCopyTrading.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StocksController : ControllerBase
    {
        private readonly HistoricalDataService _historicalService;

        public StocksController(HistoricalDataService historicalService)
        {
            _historicalService = historicalService;
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
            // Sync with latest historical candle so LTP reflects the latest historical data
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
                timestamp = s.LastTime
            }).ToList();

            return Ok(stocksList);
        }

        [HttpGet("ltp/{symbol}")]
        public IActionResult GetStockLtp(string symbol)
        {
            var candles = ReadHistoricalCandles(symbol, limit: 1);
            if (candles.Count > 0)
            {
                var latest = candles[0];
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
            return NotFound(new { message = $"No historical candle found for {symbol}" });
        }

        [HttpGet("historical/{symbol}")]
        public IActionResult GetHistoricalData(string symbol, [FromQuery] int limit = 0)
        {
            var candles = ReadHistoricalCandles(symbol, limit);
            return Ok(candles);
        }

        [HttpGet("chart-history/{symbol}")]
        public IActionResult GetChartHistory(string symbol)
        {
            var candles = ReadHistoricalCandles(symbol, 0);
            
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

        private List<HistoricalCandleDto> ReadHistoricalCandles(string symbol, int limit = 0)
        {
            string cleanSymbol = Uri.UnescapeDataString(symbol).Trim();
            // Handle both "MCX:GOLD" and "GOLD", or "NSE:TCS-EQ" and "TCS"
            string simpleName = cleanSymbol
                .Replace("MCX:", "")
                .Replace("NSE:", "")
                .Replace("-EQ", "");

            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            string[] possibleNames = { simpleName, simpleName.Replace("-", ""), simpleName.Replace("&", "") };
            string targetFile = "";
            foreach (var name in possibleNames)
            {
                var m = Path.Combine(baseDir, "MCX", $"{name}.txt");
                if (System.IO.File.Exists(m)) { targetFile = m; break; }
                var n = Path.Combine(baseDir, "Nifty50", $"{name}.txt");
                if (System.IO.File.Exists(n)) { targetFile = n; break; }
            }

            var list = new List<HistoricalCandleDto>();
            if (!string.IsNullOrEmpty(targetFile) && System.IO.File.Exists(targetFile))
            {
                var lines = System.IO.File.ReadAllLines(targetFile);
                // Skip header (Timestamp,Open,High,Low,Close,Volume)
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
                        var dt = DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime();
                        list.Add(new HistoricalCandleDto
                        {
                            Timestamp = ts,
                            Datetime = dt.ToString("yyyy-MM-dd HH:mm"),
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
                return list.TakeLast(limit).ToList();
            }

            return list;
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
