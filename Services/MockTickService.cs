using System.Globalization;
using System.IO;
using System.Linq;
using FyersCopyTrading.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FyersCopyTrading.Services
{
    public class MockTickService : BackgroundService
    {
        private readonly IHubContext<MarketHub> _hubContext;
        private readonly ILogger<MockTickService> _logger;
        private readonly Random _random = new();

        public static readonly Dictionary<string, StockState> Stocks = InitializeUniverse();

        public static void SyncWithHistoricalData(Dictionary<string, StockState>? targetMap = null)
        {
            var map = targetMap ?? Stocks;
            if (map == null || map.Count == 0) return;

            string baseDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            string mcxDir = Path.Combine(baseDir, "MCX");
            string niftyDir = Path.Combine(baseDir, "Nifty50");

            foreach (var kvp in map)
            {
                var stock = kvp.Value;
                string cleanName = stock.Name;

                string[] possibleFiles = {
                    Path.Combine(mcxDir, $"{cleanName}.txt"),
                    Path.Combine(mcxDir, $"{cleanName.Replace("&", "")}.txt"),
                    Path.Combine(niftyDir, $"{cleanName}.txt"),
                    Path.Combine(niftyDir, $"{cleanName.Replace("-", "")}.txt"),
                    Path.Combine(niftyDir, $"{cleanName.Replace("&", "")}.txt")
                };

                string foundFile = possibleFiles.FirstOrDefault(File.Exists) ?? "";
                if (!string.IsNullOrEmpty(foundFile))
                {
                    try
                    {
                        var lines = File.ReadLines(foundFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                        if (lines.Count > 1)
                        {
                            var lastLine = lines[^1].Split(',');
                            var prevLine = lines.Count > 2 ? lines[^2].Split(',') : lastLine;

                            // Format: Timestamp,Open,High,Low,Close,Volume
                            if (decimal.TryParse(lastLine[4], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal ltp))
                            {
                                stock.Price = ltp;
                                if (decimal.TryParse(lastLine[2], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal high))
                                    stock.High = high;
                                if (decimal.TryParse(lastLine[3], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal low))
                                    stock.Low = low;
                                if (decimal.TryParse(prevLine[4], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal prevClose))
                                    stock.PrevClose = prevClose;
                                else if (decimal.TryParse(lastLine[1], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal open))
                                    stock.PrevClose = open;

                                stock.Change = Math.Round(stock.Price - stock.PrevClose, 2);
                                stock.ChangePercent = stock.PrevClose > 0 ? Math.Round((stock.Change / stock.PrevClose) * 100, 2) : 0;

                                if (long.TryParse(lastLine[0], out long ts))
                                {
                                    stock.LastTime = DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime().ToString("HH:mm:ss");
                                }
                            }
                        }
                    }
                    catch
                    {
                        // File access safeguard
                    }
                }
            }
        }

        private static Dictionary<string, StockState> InitializeUniverse()
        {
            var initialPrices = new Dictionary<string, decimal>
            {
                // MCX Commodities
                { "MCX:GOLD", 75000.00m },
                { "MCX:SILVER", 88000.00m },
                { "MCX:NATGAS", 230.00m },
                { "MCX:CRUDEOIL", 6100.00m },
                { "MCX:COPPER", 810.00m },
                { "MCX:ZINC", 270.00m },
                { "MCX:LEAD", 180.00m },
                { "MCX:ALUMINIUM", 240.00m },
                { "MCX:NICKEL", 1400.00m },

                // Nifty 50 Equities
                { "NSE:TCS-EQ", 4250.50m },
                { "NSE:INFY-EQ", 1892.20m },
                { "NSE:WIPRO-EQ", 542.80m },
                { "NSE:RELIANCE-EQ", 2985.00m },
                { "NSE:HDFCBANK-EQ", 1672.40m },
                { "NSE:ICICIBANK-EQ", 1210.15m },
                { "NSE:BHARTIARTL-EQ", 1540.00m },
                { "NSE:SBIN-EQ", 785.60m },
                { "NSE:TATAMOTORS-EQ", 965.40m },
                { "NSE:AXISBANK-EQ", 1180.20m },
                { "NSE:LT-EQ", 3650.00m },
                { "NSE:KOTAKBANK-EQ", 1780.00m },
                { "NSE:BAJFINANCE-EQ", 6950.00m },
                { "NSE:MARUTI-EQ", 12450.00m },
                { "NSE:HINDUNILVR-EQ", 2680.00m },
                { "NSE:ASIANPAINT-EQ", 3120.00m },
                { "NSE:SUNPHARMA-EQ", 1710.00m },
                { "NSE:TITAN-EQ", 3450.00m },
                { "NSE:ULTRACEMCO-EQ", 11200.00m },
                { "NSE:NTPC-EQ", 415.00m },
                { "NSE:POWERGRID-EQ", 335.00m },
                { "NSE:ONGC-EQ", 295.00m },
                { "NSE:COALINDIA-EQ", 485.00m },
                { "NSE:TATASTEEL-EQ", 158.00m },
                { "NSE:JSWSTEEL-EQ", 945.00m },
                { "NSE:HCLTECH-EQ", 1780.00m },
                { "NSE:TECHM-EQ", 1520.00m },
                { "NSE:ADANIENT-EQ", 3150.00m },
                { "NSE:ADANIPORTS-EQ", 1460.00m },
                { "NSE:GRASIM-EQ", 2680.00m },
                { "NSE:BPCL-EQ", 355.00m },
                { "NSE:EICHERMOT-EQ", 4820.00m },
                { "NSE:HEROMOTOCO-EQ", 5350.00m },
                { "NSE:BAJAJ-AUTO-EQ", 9850.00m },
                { "NSE:CIPLA-EQ", 1620.00m },
                { "NSE:DRREDDY-EQ", 6680.00m },
                { "NSE:DIVISLAB-EQ", 4950.00m },
                { "NSE:APOLLOHOSP-EQ", 6850.00m },
                { "NSE:BRITANNIA-EQ", 5890.00m },
                { "NSE:NESTLEIND-EQ", 2480.00m },
                { "NSE:TATACONSUM-EQ", 1180.00m },
                { "NSE:INDUSINDBK-EQ", 1420.00m },
                { "NSE:BAJAJFINSV-EQ", 1850.00m },
                { "NSE:SBILIFE-EQ", 1820.00m },
                { "NSE:HDFCLIFE-EQ", 710.00m },
                { "NSE:SHRIRAMFIN-EQ", 3150.00m },
                { "NSE:BEL-EQ", 285.00m },
                { "NSE:TRENT-EQ", 7250.00m },
                { "NSE:M&M-EQ", 2780.00m }
            };

            var map = new Dictionary<string, StockState>();

            // Add MCX symbols
            string[] mcxList = { "MCX:GOLD", "MCX:SILVER", "MCX:NATGAS", "MCX:CRUDEOIL", "MCX:COPPER", "MCX:ZINC", "MCX:LEAD", "MCX:ALUMINIUM", "MCX:NICKEL" };
            foreach (var sym in mcxList)
            {
                string name = sym.Replace("MCX:", "");
                decimal price = initialPrices.TryGetValue(sym, out var p) ? p : 1000m;
                map[sym] = new StockState
                {
                    Symbol = sym,
                    Name = name,
                    Category = "MCX",
                    Price = price,
                    High = Math.Round(price * 1.01m, 2),
                    Low = Math.Round(price * 0.99m, 2),
                    PrevClose = Math.Round(price * 0.995m, 2)
                };
            }

            // Add Nifty50 symbols
            foreach (var sym in Nifty50Universe.GetSymbols())
            {
                if (sym == "NSE:NIFTY50-INDEX") continue;
                
                string cleanName = sym.Replace("NSE:", "").Replace("-EQ", "");
                decimal price = initialPrices.TryGetValue(sym, out var p) ? p : 1500.00m;
                
                map[sym] = new StockState
                {
                    Symbol = sym,
                    Name = cleanName,
                    Category = "NIFTY50",
                    Price = price,
                    High = Math.Round(price * 1.01m, 2),
                    Low = Math.Round(price * 0.99m, 2),
                    PrevClose = Math.Round(price * 0.995m, 2)
                };
            }

            // Synchronize starting LTP directly from the last historical candle in Data files
            SyncWithHistoricalData(map);

            return map;
        }

        public MockTickService(IHubContext<MarketHub> hubContext, ILogger<MockTickService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("=========================================================");
            _logger.LogInformation("  MOCK MARKET DATA ENGINE RUNNING (Nifty 50 Universe)");
            _logger.LogInformation("=========================================================");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    foreach (var key in Stocks.Keys.ToList())
                    {
                        var stock = Stocks[key];
                        // Generate realistic tick fluctuation (-0.15% to +0.15%)
                        decimal changePercent = (decimal)(_random.NextDouble() * 0.003 - 0.0015);
                        decimal delta = Math.Round(stock.Price * changePercent, 2);
                        if (delta == 0) delta = _random.Next(0, 2) == 0 ? 0.25m : -0.25m;

                        stock.Price = Math.Max(1.00m, Math.Round(stock.Price + delta, 2));
                        stock.High = Math.Max(stock.High, stock.Price);
                        stock.Low = Math.Min(stock.Low, stock.Price);
                        stock.Change = Math.Round(stock.Price - stock.PrevClose, 2);
                        stock.ChangePercent = Math.Round((stock.Change / stock.PrevClose) * 100, 2);
                        stock.LastTime = DateTime.Now.ToString("HH:mm:ss");

                        var tickData = new
                        {
                            symbol = stock.Symbol,
                            name = stock.Name,
                            category = stock.Category,
                            price = stock.Price,
                            change = stock.Change,
                            changePercent = stock.ChangePercent,
                            high = stock.High,
                            low = stock.Low,
                            prevClose = stock.PrevClose,
                            timestamp = stock.LastTime
                        };

                        await _hubContext.Clients.All.SendAsync("ReceiveTick", tickData, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error streaming tick data");
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    public class StockState
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "NIFTY50";
        public decimal Price { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal PrevClose { get; set; }
        public decimal Change { get; set; }
        public decimal ChangePercent { get; set; }
        public string LastTime { get; set; } = DateTime.Now.ToString("HH:mm:ss");
    }
}
