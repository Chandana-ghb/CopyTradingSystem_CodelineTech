using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FyersCopyTrading.Hubs;
using FyersCSharpSDK;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FyersCopyTrading.Services
{
    public class FyersLiveMarketService : BackgroundService, FyersSocketDelegate
    {
        private readonly ILogger<FyersLiveMarketService> _logger;
        private readonly IHubContext<MarketHub> _hubContext;
        private readonly IConfiguration _config;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly HttpClient _httpClient;

        private FyersSocket? _socketClient;
        private readonly string _appId;
        private readonly string _accessToken;
        private readonly string _dataBaseDir;
        private readonly string _mcxDir;
        private readonly string _niftyDir;

        public static readonly ConcurrentDictionary<string, LiveCandleBar> ActiveCandles = new();
        private readonly ConcurrentDictionary<string, object> _fileLocks = new();
        private bool _isConnected = false;

        public static LiveCandleBar? GetActiveCandle(string symbol)
        {
            var (category, cleanName, _) = ResolveStockInfo(symbol);
            string barKey = $"{category}:{cleanName}";
            return ActiveCandles.TryGetValue(barKey, out var bar) ? bar : null;
        }

        // Active MCX futures symbols in Fyers
        public static readonly List<string> McxActiveSymbols = new()
        {
            "MCX:GOLD26DECFUT",
            "MCX:SILVER26DECFUT",
            "MCX:CRUDEOIL26OCTFUT",
            "MCX:NATURALGAS26OCTFUT",
            "MCX:COPPER26OCTFUT",
            "MCX:ZINC26OCTFUT",
            "MCX:LEAD26OCTFUT",
            "MCX:ALUMINIUM26OCTFUT",
            "MCX:NICKEL26OCTFUT"
        };

        public FyersLiveMarketService(
            ILogger<FyersLiveMarketService> logger,
            IHubContext<MarketHub> hubContext,
            IConfiguration config,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _hubContext = hubContext;
            _config = config;
            _scopeFactory = scopeFactory;

            _appId = _config["Fyers:AppId"] ?? "ZZQW1QXQFO-100";
            _accessToken = _config["Fyers:AccessToken"] ?? "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOlsiZDoxIiwiZDoyIiwieDowIiwieDoxIl0sImF0X2hhc2giOiJnQUFBQUFCcXhHd0I3X0JGMlo4c2RTXzNNWGswTUhRWk5tbUx3ZmJyblBIXzZndGVaSEVxUEs2RDJlSmZSTjhhUkZVb0UtZWpzeWZsemM0Q0l5OE5NOTAyZGxTYW5MRnhleWVyeDZ3Y0ltQTNzZE5fdTM0eTJ3UT0iLCJkaXNwbGF5X25hbWUiOiIiLCJvbXMiOiJLMSIsImhzbV9rZXkiOiIyM2E2MmEzNDM1NTlkOGIzYWNiYmFmZjZjZTljYWMwMDc4NDFiODM2MGNmMmM4ZmY5ZDc3NjZhMCIsImlzRGRwaUVuYWJsZWQiOiJOIiwiaXNNdGZFbmFibGVkIjoiTiIsImZ5X2lkIjoiRkFLODg0NTUiLCJhcHBUeXBlIjoxMDAsImV4cCI6MTc5MTMzMzAwMCwiaWF0IjoxNzkxMjU3NjAxLCJpc3MiOiJhcGkuZnllcnMuaW4iLCJuYmYiOjE3OTEyNTc2MDEsInN1YiI6ImFjY2Vzc190b2tlbiJ9.MPt81X-otIXhaDhg8DyMgLB5U4aDC60iLEXYz-ph3kE";

            _dataBaseDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            _mcxDir = Path.Combine(_dataBaseDir, "MCX");
            _niftyDir = Path.Combine(_dataBaseDir, "Nifty50");

            Directory.CreateDirectory(_mcxDir);
            Directory.CreateDirectory(_niftyDir);

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"{_appId}:{_accessToken}");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("===============================================================");
            _logger.LogInformation("⚡ STARTING FYERS WEBSOCKET LIVE MARKET STREAMING ENGINE");
            _logger.LogInformation("📍 App ID: {AppId}", _appId);
            _logger.LogInformation("📍 Storing live data in: {BaseDir}/MCX and {BaseDir}/Nifty50", _dataBaseDir, _dataBaseDir);
            _logger.LogInformation("===============================================================");

            // 1. Initial snapshot fetch to ensure all stock files & in-memory prices are immediately live
            await FetchInitialQuotesSnapshotAsync();

            // 2. Start WebSocket HSM Connection
            StartWebSocketFeed();

            // 3. Keep alive and background sync loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Check if socket needs reconnection
                    if (!_isConnected)
                    {
                        _logger.LogInformation("[Fyers Live Feed] Attempting WebSocket reconnect...");
                        StartWebSocketFeed();
                    }

                    // Periodic REST quotes poll to ensure live updates continue uninterrupted even during quiet/intermittent market periods
                    await PollLiveQuotesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Fyers Live Feed] Error in live stream maintenance loop");
                }

                await Task.Delay(3000, stoppingToken);
            }

            _logger.LogInformation("[Fyers Live Feed] Shutting down live market service.");
        }

        private void StartWebSocketFeed()
        {
            try
            {
                FyersClass fyersModel = FyersClass.Instance;
                fyersModel.ClientId = _appId;
                fyersModel.AccessToken = _accessToken;

                _socketClient = new FyersSocket();
                _socketClient.webSocketDelegate = this;

                _logger.LogInformation("[Fyers WebSocket] Initiating HSM Market Data connection...");
                _socketClient.ConnectHSM(HyperSyncLib.ChannelModes.FULL, HyperSyncLib.UserTypes.Normal, false);

                // Build subscription list
                var symbolsToSubscribe = new List<string>(McxActiveSymbols);

                // Add Nifty 50 symbols (filtering out invalid symbols like TATAMOTORS-EQ)
                foreach (var sym in Nifty50Universe.GetSymbols())
                {
                    if (sym == "NSE:NIFTY50-INDEX" || sym == "NSE:TATAMOTORS-EQ") continue;
                    symbolsToSubscribe.Add(sym);
                }

                _logger.LogInformation("[Fyers WebSocket] Subscribing to {Count} market symbols...", symbolsToSubscribe.Count);
                _socketClient.SubscribeData(symbolsToSubscribe, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Fyers WebSocket] Failed to initialize WebSocket connection");
                _isConnected = false;
            }
        }

        // --- FyersSocketDelegate Callbacks ---

        public void OnOpen(string status)
        {
            _isConnected = true;
            _logger.LogInformation("🟢 [Fyers WebSocket Connected]: {Status}", status);
        }

        public void OnClose(string status)
        {
            _isConnected = false;
            _logger.LogWarning("🟡 [Fyers WebSocket Closed]: {Status}", status);
        }

        public void OnScrips(JObject scrips)
        {
            try
            {
                JToken? dataToken = scrips["data"] ?? scrips;
                if (dataToken == null) return;

                JObject? tickObj = null;
                if (dataToken.Type == JTokenType.String)
                {
                    string rawStr = dataToken.ToString();
                    if (!string.IsNullOrWhiteSpace(rawStr) && rawStr.StartsWith("{"))
                    {
                        tickObj = JObject.Parse(rawStr);
                    }
                }
                else if (dataToken is JObject jo)
                {
                    tickObj = jo;
                }

                if (tickObj != null)
                {
                    ProcessLiveTick(tickObj);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Fyers WebSocket] Error processing tick");
            }
        }

        public void OnIndex(JObject index) { }
        public void OnDepth(JObject depths) { }
        public void OnOrder(JObject orders) { }
        public void OnTrade(JObject trades) { }
        public void OnPosition(JObject positions) { }

        public void OnError(JObject error)
        {
            _logger.LogWarning("🔴 [Fyers WebSocket Error]: {Error}", error.ToString(Newtonsoft.Json.Formatting.None));
        }

        public void OnMessage(JObject response)
        {
            string msg = response.ToString(Newtonsoft.Json.Formatting.None);
            if (msg.Contains("Authentication done") || msg.Contains("Successfully subscribed"))
            {
                _isConnected = true;
                _logger.LogInformation("ℹ️ [Fyers WebSocket Msg]: {Message}", msg);
            }
        }

        // --- Live Tick Processing & File Storage ---

        private void ProcessLiveTick(JObject tickObj)
        {
            string? rawSymbol = tickObj["symbol"]?.ToString() ?? tickObj["n"]?.ToString();
            if (string.IsNullOrWhiteSpace(rawSymbol)) return;

            decimal ltp = tickObj["ltp"]?.Value<decimal>() ?? tickObj["lp"]?.Value<decimal>() ?? 0m;
            if (ltp <= 0) return;

            decimal high = tickObj["high_price"]?.Value<decimal>() ?? 0m;
            decimal low = tickObj["low_price"]?.Value<decimal>() ?? 0m;
            decimal open = tickObj["open_price"]?.Value<decimal>() ?? 0m;
            decimal prevClose = tickObj["prev_close_price"]?.Value<decimal>() ?? 0m;
            decimal ch = tickObj["ch"]?.Value<decimal>() ?? 0m;
            decimal chp = tickObj["chp"]?.Value<decimal>() ?? 0m;
            long volume = tickObj["vol_traded_today"]?.Value<long>() ?? tickObj["volume"]?.Value<long>() ?? 0;

            long nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long tickTime = tickObj["last_traded_time"]?.Value<long>() ?? tickObj["exch_feed_time"]?.Value<long>() ?? nowSec;
            if (tickTime <= 0 || tickTime < (nowSec - 86400) || tickTime > (nowSec + 3600))
            {
                tickTime = nowSec;
            }

            var (category, cleanName, systemSymbol) = ResolveStockInfo(rawSymbol);

            // 1. Update in-memory state for API endpoints
            UpdateInMemoryStock(systemSymbol, cleanName, category, ltp, high, low, open, prevClose, ch, chp);

            // 2. Continuously record live data into specific stock files
            var activeBar = RecordCandleToFile(category, cleanName, tickTime, ltp, volume);

            // 3. Broadcast real-time tick to Angular frontend via SignalR with live candle data
            BroadcastTick(systemSymbol, cleanName, category, ltp, high, low, prevClose, ch, chp, activeBar);

            // 4. Check for Stop-Loss or Target Price execution triggers
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var copyService = scope.ServiceProvider.GetRequiredService<CopyTradingService>();
                    await copyService.CheckAndTriggerStopLossOrTargetAsync(systemSymbol, ltp);
                }
                catch { }
            });
        }

        public static (string Category, string CleanName, string SystemSymbol) ResolveStockInfo(string rawSymbol)
        {
            string upper = rawSymbol.Trim().ToUpperInvariant();
            if (upper.StartsWith("MCX:"))
            {
                string rem = upper.Substring(4);
                if (rem.StartsWith("GOLD")) return ("MCX", "GOLD", "MCX:GOLD");
                if (rem.StartsWith("SILVER")) return ("MCX", "SILVER", "MCX:SILVER");
                if (rem.StartsWith("CRUDEOIL")) return ("MCX", "CRUDEOIL", "MCX:CRUDEOIL");
                if (rem.StartsWith("NATURALGAS") || rem.StartsWith("NATGAS")) return ("MCX", "NATGAS", "MCX:NATGAS");
                if (rem.StartsWith("COPPER")) return ("MCX", "COPPER", "MCX:COPPER");
                if (rem.StartsWith("ZINC")) return ("MCX", "ZINC", "MCX:ZINC");
                if (rem.StartsWith("LEAD")) return ("MCX", "LEAD", "MCX:LEAD");
                if (rem.StartsWith("ALUMINIUM")) return ("MCX", "ALUMINIUM", "MCX:ALUMINIUM");
                if (rem.StartsWith("NICKEL")) return ("MCX", "NICKEL", "MCX:NICKEL");
                return ("MCX", rem, upper);
            }
            else
            {
                string clean = upper.Replace("NSE:", "").Replace("-EQ", "");
                return ("NIFTY50", clean, $"NSE:{clean}-EQ");
            }
        }

        private void UpdateInMemoryStock(
            string systemSymbol,
            string cleanName,
            string category,
            decimal ltp,
            decimal high,
            decimal low,
            decimal open,
            decimal prevClose,
            decimal ch,
            decimal chp)
        {
            if (MockTickService.Stocks.TryGetValue(systemSymbol, out var stock))
            {
                stock.Price = ltp;
                if (high > 0) stock.High = high;
                else stock.High = Math.Max(stock.High, ltp);

                if (low > 0) stock.Low = low;
                else stock.Low = stock.Low > 0 ? Math.Min(stock.Low, ltp) : ltp;

                if (prevClose > 0) stock.PrevClose = prevClose;
                else if (open > 0) stock.PrevClose = open;

                stock.Change = ch != 0 ? ch : Math.Round(stock.Price - stock.PrevClose, 2);
                stock.ChangePercent = chp != 0 ? chp : (stock.PrevClose > 0 ? Math.Round((stock.Change / stock.PrevClose) * 100, 2) : 0);
                stock.LastTime = DateTime.Now.ToString("HH:mm:ss");
            }
            else
            {
                MockTickService.Stocks[systemSymbol] = new StockState
                {
                    Symbol = systemSymbol,
                    Name = cleanName,
                    Category = category,
                    Price = ltp,
                    High = high > 0 ? high : ltp,
                    Low = low > 0 ? low : ltp,
                    PrevClose = prevClose > 0 ? prevClose : (open > 0 ? open : ltp),
                    Change = ch != 0 ? ch : 0,
                    ChangePercent = chp != 0 ? chp : 0,
                    LastTime = DateTime.Now.ToString("HH:mm:ss")
                };
            }
        }

        private void BroadcastTick(
            string systemSymbol,
            string cleanName,
            string category,
            decimal ltp,
            decimal high,
            decimal low,
            decimal prevClose,
            decimal ch,
            decimal chp,
            LiveCandleBar? candleBar = null)
        {
            var stock = MockTickService.Stocks.TryGetValue(systemSymbol, out var s) ? s : null;
            var tickPayload = new
            {
                symbol = systemSymbol,
                name = cleanName,
                category = category,
                price = ltp,
                change = stock?.Change ?? ch,
                changePercent = stock?.ChangePercent ?? chp,
                high = stock?.High ?? (high > 0 ? high : ltp),
                low = stock?.Low ?? (low > 0 ? low : ltp),
                prevClose = stock?.PrevClose ?? (prevClose > 0 ? prevClose : ltp),
                timestamp = stock?.LastTime ?? DateTime.Now.ToString("HH:mm:ss"),
                candleTime = candleBar?.MinuteTimestamp ?? ((DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60) * 60),
                candleOpen = candleBar?.Open ?? ltp,
                candleHigh = candleBar?.High ?? (high > 0 ? high : ltp),
                candleLow = candleBar?.Low ?? (low > 0 ? low : ltp),
                candleClose = candleBar?.Close ?? ltp,
                candleVolume = candleBar?.Volume ?? 1
            };

            _ = _hubContext.Clients.All.SendAsync("ReceiveTick", tickPayload);
        }

        private LiveCandleBar RecordCandleToFile(string category, string cleanName, long tickEpochSec, decimal ltp, long volume)
        {
            long minuteTs = (tickEpochSec / 60) * 60;
            string barKey = $"{category}:{cleanName}";

            var bar = ActiveCandles.GetOrAdd(barKey, _ => new LiveCandleBar
            {
                MinuteTimestamp = minuteTs,
                Open = ltp,
                High = ltp,
                Low = ltp,
                Close = ltp,
                Volume = volume
            });

            lock (bar)
            {
                if (minuteTs > bar.MinuteTimestamp)
                {
                    // Previous minute bar completed -> flush completed bar
                    WriteCandleToDisk(category, cleanName, bar.MinuteTimestamp, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);

                    // Start new 1-minute bar
                    bar.MinuteTimestamp = minuteTs;
                    bar.Open = ltp;
                    bar.High = ltp;
                    bar.Low = ltp;
                    bar.Close = ltp;
                    bar.Volume = volume;
                }
                else
                {
                    // Update active candle in current minute
                    bar.High = Math.Max(bar.High, ltp);
                    bar.Low = Math.Min(bar.Low, ltp);
                    bar.Close = ltp;
                    bar.Volume = volume > 0 ? volume : bar.Volume + 1;
                }

                // Write/update current candle in file
                WriteCandleToDisk(category, cleanName, bar.MinuteTimestamp, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);
            }
            return bar;
        }

        private void WriteCandleToDisk(string category, string cleanName, long minuteTs, decimal open, decimal high, decimal low, decimal close, long volume)
        {
            string targetFolder = category.Equals("MCX", StringComparison.OrdinalIgnoreCase) ? _mcxDir : _niftyDir;
            string primaryFile = Path.Combine(targetFolder, $"{cleanName}.txt");

            // Also handle any alias files (e.g., BAJAJAUTO.txt & BAJAJ-AUTO.txt, MM.txt & M&M.txt)
            var fileList = new List<string> { primaryFile };
            if (cleanName.Contains("-") || cleanName.Contains("&"))
            {
                fileList.Add(Path.Combine(targetFolder, $"{cleanName.Replace("-", "").Replace("&", "")}.txt"));
            }

            foreach (var filePath in fileList)
            {
                var fileLock = _fileLocks.GetOrAdd(filePath, _ => new object());
                lock (fileLock)
                {
                    try
                    {
                        var dtIst = DateTimeOffset.FromUnixTimeSeconds(minuteTs).ToOffset(TimeSpan.FromHours(5.5));
                        string timeStr = dtIst.ToString("yyyy-MM-dd HH:mm:ss");
                        string lineStr = $"{timeStr},{open.ToString(CultureInfo.InvariantCulture)},{high.ToString(CultureInfo.InvariantCulture)},{low.ToString(CultureInfo.InvariantCulture)},{close.ToString(CultureInfo.InvariantCulture)},{volume}";

                        if (!File.Exists(filePath))
                        {
                            File.WriteAllText(filePath, "Timestamp,Open,High,Low,Close,Volume" + Environment.NewLine + lineStr + Environment.NewLine);
                            return;
                        }

                        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                        long endPos = fs.Length;
                        if (endPos == 0)
                        {
                            using var sw = new StreamWriter(fs);
                            sw.WriteLine("Timestamp,Open,High,Low,Close,Volume");
                            sw.WriteLine(lineStr);
                            return;
                        }

                        // Seek backwards to find the beginning of the last line
                        long pos = endPos - 1;
                        while (pos > 0)
                        {
                            fs.Seek(pos, SeekOrigin.Begin);
                            int b = fs.ReadByte();
                            if (b != '\n' && b != '\r') break;
                            pos--;
                        }

                        while (pos > 0)
                        {
                            fs.Seek(pos, SeekOrigin.Begin);
                            int b = fs.ReadByte();
                            if (b == '\n') { pos++; break; }
                            pos--;
                        }

                        fs.Seek(pos, SeekOrigin.Begin);
                        using var reader = new StreamReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);
                        string? lastLine = reader.ReadLine();

                        if (!string.IsNullOrWhiteSpace(lastLine) && !lastLine.StartsWith("Timestamp"))
                        {
                            var parts = lastLine.Split(',');
                            if (parts.Length >= 6)
                            {
                                long lastTs = 0;
                                if (long.TryParse(parts[0], out long parsedTs))
                                {
                                    lastTs = parsedTs;
                                }
                                else if (DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDt))
                                {
                                    lastTs = new DateTimeOffset(parsedDt, TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();
                                }
                                else if (DateTime.TryParse(parts[0], out DateTime fallbackDt))
                                {
                                    lastTs = new DateTimeOffset(fallbackDt, TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();
                                }

                                if (lastTs == minuteTs)
                                {
                                    // Overwrite last line with updated candle
                                    fs.SetLength(pos);
                                    fs.Seek(pos, SeekOrigin.Begin);
                                    using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8, leaveOpen: true);
                                    writer.WriteLine(lineStr);
                                    return;
                                }
                            }
                        }

                        // Otherwise append new candle line
                        fs.Seek(0, SeekOrigin.End);
                        using (var writer = new StreamWriter(fs, System.Text.Encoding.UTF8, leaveOpen: true))
                        {
                            fs.Seek(endPos - 1, SeekOrigin.Begin);
                            int lastByte = fs.ReadByte();
                            if (lastByte != '\n') writer.WriteLine();
                            writer.WriteLine(lineStr);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace("Error writing candle to {FilePath}: {Message}", filePath, ex.Message);
                    }
                }
            }
        }

        // --- Initial & Periodic Live Quotes Poll (Zero-Downtime Live Feed) ---

        private async Task FetchInitialQuotesSnapshotAsync()
        {
            try
            {
                _logger.LogInformation("[Fyers Snapshot] Fetching live quotes snapshot from Fyers API...");
                await PollLiveQuotesInternalAsync();
                _logger.LogInformation("✅ [Fyers Snapshot] Live prices populated for all MCX and Nifty 50 stocks.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Fyers Snapshot] Initial quotes snapshot fetch encountered an issue.");
            }
        }

        private async Task PollLiveQuotesAsync()
        {
            await PollLiveQuotesInternalAsync();
        }

        private async Task PollLiveQuotesInternalAsync()
        {
            // 1. Dedicated safe poll for MCX commodities
            foreach (var sym in McxActiveSymbols)
            {
                try
                {
                    string url = $"https://api-t1.fyers.in/data/quotes?symbols={Uri.EscapeDataString(sym)}";
                    using var resp = await _httpClient.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("d", out var dArray) && dArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in dArray.EnumerateArray())
                            {
                                ProcessQuoteItem(item);
                            }
                        }
                    }
                }
                catch { }
            }

            // 2. Nifty 50 stocks in chunks of 25
            var niftySymbols = Nifty50Universe.GetSymbols()
                .Where(s => s != "NSE:NIFTY50-INDEX" && s != "NSE:TATAMOTORS-EQ")
                .ToList();

            for (int i = 0; i < niftySymbols.Count; i += 25)
            {
                var chunk = niftySymbols.Skip(i).Take(25).ToList();
                string joined = string.Join(",", chunk.Select(Uri.EscapeDataString));
                string url = $"https://api-t1.fyers.in/data/quotes?symbols={joined}";

                try
                {
                    using var resp = await _httpClient.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        string json = await resp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("d", out var dArray) && dArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in dArray.EnumerateArray())
                            {
                                ProcessQuoteItem(item);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogTrace("[Fyers Quotes Poll] Nifty batch poll issue: {Message}", ex.Message);
                }
            }
        }

        private void ProcessQuoteItem(JsonElement item)
        {
            if (item.TryGetProperty("n", out var nProp) && item.TryGetProperty("v", out var vProp))
            {
                string sym = nProp.GetString() ?? "";
                if (vProp.TryGetProperty("lp", out var lpProp))
                {
                    decimal ltp = lpProp.GetDecimal();
                    if (ltp <= 0) return;
                    decimal high = vProp.TryGetProperty("high_price", out var hp) ? hp.GetDecimal() : ltp;
                    decimal low = vProp.TryGetProperty("low_price", out var lop) ? lop.GetDecimal() : ltp;
                    decimal open = vProp.TryGetProperty("open_price", out var op) ? op.GetDecimal() : ltp;
                    decimal prevClose = vProp.TryGetProperty("prev_close_price", out var pcp) ? pcp.GetDecimal() : ltp;
                    decimal ch = vProp.TryGetProperty("ch", out var chProp) ? chProp.GetDecimal() : 0m;
                    decimal chp = vProp.TryGetProperty("chp", out var chpProp) ? chpProp.GetDecimal() : 0m;
                    long volume = vProp.TryGetProperty("volume", out var volProp) ? volProp.GetInt64() : 0;
                    long tt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                    var (category, cleanName, systemSymbol) = ResolveStockInfo(sym);
                    UpdateInMemoryStock(systemSymbol, cleanName, category, ltp, high, low, open, prevClose, ch, chp);
                    var activeBar = RecordCandleToFile(category, cleanName, tt, ltp, volume);
                    BroadcastTick(systemSymbol, cleanName, category, ltp, high, low, prevClose, ch, chp, activeBar);
                }
            }
        }
    }

    public class LiveCandleBar
    {
        public long MinuteTimestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public long Volume { get; set; }
    }
}
