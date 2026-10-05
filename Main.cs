using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FyersCSharpSDK;
using Newtonsoft.Json.Linq;
using System.Threading;

namespace FyersCopyTrading
{
    class Program
    {
        private const string AppId = "ZZQW1QXQFO-100";
        private const string AppSecret = "NUIT5XL6IP";
        private const string AccessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJhdWQiOlsiZDoxIiwiZDoyIiwieDowIiwieDoxIl0sImF0X2hhc2giOiJnQUFBQUFCcXd4b21nb1d0REM2WnpqSy0zVmtlT09fQV96QVdvbXhMR2xZLTBiSFVOSERhZ0RfMzdvTGZiV1ZuZXlwTGg5MjNBRE1TUWxWYzhJQ3F2WmphWnV5VnEyZkNDLVRQNksyRFFwU25RcTRFOGRqMThoOD0iLCJkaXNwbGF5X25hbWUiOiIiLCJvbXMiOiJLMSIsImhzbV9rZXkiOiIyM2E2MmEzNDM1NTlkOGIzYWNiYmFmZjZjZTljYWMwMDc4NDFiODM2MGNmMmM4ZmY5ZDc3NjZhMCIsImlzRGRwaUVuYWJsZWQiOiJOIiwiaXNNdGZFbmFibGVkIjoiTiIsImZ5X2lkIjoiRkFLODg0NTUiLCJhcHBUeXBlIjoxMDAsImV4cCI6MTc5MTI0NjYwMCwiaWF0IjoxNzkxMTcxMTEwLCJpc3MiOiJhcGkuZnllcnMuaW4iLCJuYmYiOjE3OTExNzExMTAsInN1YiI6ImFjY2Vzc190b2tlbiJ9.pj6ceuoPzskOh4V824zpn282EV3tWQfB14OLPy68uX8";

        public static async Task RunFyersConsoleAsync(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("===============================================================");
            Console.WriteLine("    FYERS API v3 - LIVE WEBSOCKET CLIENT (Official SDK)        ");
            Console.WriteLine("===============================================================");
            Console.ResetColor();

            Console.WriteLine("\nSelect feed to run:");
            Console.WriteLine("  1. NIFTY 50 (NSE Equity)");
            Console.WriteLine("  2. MCX GOLD (Commodity)");
            Console.Write("\nEnter choice [1/2]: ");

            string choice = Console.ReadLine()?.Trim() ?? "1";

            FyersClass fyersModel = FyersClass.Instance;
            fyersModel.ClientId = AppId;
            fyersModel.AccessToken = AccessToken;

            if (choice == "2")
            {
                Console.Title = "Fyers v3 - MCX Gold Live Feed";
                McxGoldDataFetcher mcxFeed = new McxGoldDataFetcher();
                await mcxFeed.RunAsync(AppId, AccessToken);
            }
            else
            {
                Console.Title = "Fyers v3 - NIFTY 50 Live Feed";
                LiveFeedHandler feedHandler = new LiveFeedHandler();
                await feedHandler.StartDataWebSocket();

                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    try { cts.Cancel(); } catch { }
                };

                try
                {
                    await Task.Delay(-1, cts.Token);
                }
                catch (TaskCanceledException) { }

                cts.Dispose();
                Console.WriteLine("[System] Session ended. Goodbye.");
            }
        }
    }

    public class LiveFeedHandler : FyersSocketDelegate
    {
        private FyersSocket _client;
        private readonly string _logFilePath;
        private readonly object _fileLock = new();

        public LiveFeedHandler()
        {
            // Create Data folder if it doesn't exist
            string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Data");
            Directory.CreateDirectory(dataDir);
            _logFilePath = Path.Combine(dataDir, $"Nifty50_Ticks_{DateTime.Now:yyyy-MM-dd}.txt");
            LogToFile($"=== Nifty 50 Live Feed Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            Console.WriteLine($"[System] Tick data will be saved to: {Path.GetFullPath(_logFilePath)}");
        }

        private void LogToFile(string line)
        {
            lock (_fileLock)
            {
                File.AppendAllText(_logFilePath, $"{DateTime.Now:HH:mm:ss.fff} | {line}{Environment.NewLine}");
            }
        }

        public async Task StartDataWebSocket()
        {
            Console.WriteLine("[System] Preparing to connect to TBT WebSocket via official SDK...");
            List<string> symbolsToSubscribe = Nifty50Universe.GetSymbols();
            Console.WriteLine($"[System] Got {symbolsToSubscribe.Count} symbols to subscribe.");

            _client = new FyersSocket();
            _client.webSocketDelegate = this;
            
            Console.WriteLine("[System] Initiating HSM Market Data connection...");
            _client.ConnectHSM(HyperSyncLib.ChannelModes.FULL, HyperSyncLib.UserTypes.Normal, false);

            Console.WriteLine("[System] Sending subscription list for Market Data...");
            _client.SubscribeData(symbolsToSubscribe, false);
            Console.WriteLine("[System] Market data subscription sent.");
        }

        public void OnClose(string status)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[WebSocket] Connection is closed: " + status);
            Console.ResetColor();
            LogToFile($"[DISCONNECTED] {status}");
        }

        public void OnOpen(string status)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[WebSocket] Connection is open: " + status);
            Console.ResetColor();
            LogToFile($"[CONNECTED] {status}");
        }

        public void OnOrder(JObject orders)
        {
            string data = orders.ToString(Newtonsoft.Json.Formatting.None);
            Console.WriteLine("[Order] " + data);
            LogToFile($"[ORDER] {data}");
        }

        public void OnTrade(JObject trades)
        {
            string data = trades.ToString(Newtonsoft.Json.Formatting.None);
            Console.WriteLine("[Trade] " + data);
            LogToFile($"[TRADE] {data}");
        }

        public void OnPosition(JObject positions)
        {
            string data = positions.ToString(Newtonsoft.Json.Formatting.None);
            Console.WriteLine("[Position] " + data);
            LogToFile($"[POSITION] {data}");
        }

        public void OnIndex(JObject index)
        {
            string data = index["data"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("[IndexTick] " + data);
            Console.ResetColor();
            LogToFile($"[INDEX] {data}");
        }

        public void OnScrips(JObject scrips)
        {
            string data = scrips["data"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[ScripTick] " + data);
            Console.ResetColor();
            LogToFile($"[SCRIP] {data}");
        }

        public void OnDepth(JObject depths)
        {
            string data = depths["data"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            Console.WriteLine("[DepthTick] " + data);
            LogToFile($"[DEPTH] {data}");
        }

        public void OnError(JObject error)
        {
            string data = error.ToString(Newtonsoft.Json.Formatting.None);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[Error]: " + data);
            Console.ResetColor();
            LogToFile($"[ERROR] {data}");
        }

        public void OnMessage(JObject response)
        {
            string data = response.ToString(Newtonsoft.Json.Formatting.None);
            Console.WriteLine("[Message] " + data);
            LogToFile($"[MSG] {data}");
        }
    }

    public static class Nifty50Universe
    {
        public static List<string> GetSymbols() => new List<string> {
            "NSE:NIFTY50-INDEX", "NSE:ADANIENT-EQ", "NSE:ADANIPORTS-EQ", "NSE:APOLLOHOSP-EQ",
            "NSE:ASIANPAINT-EQ", "NSE:AXISBANK-EQ", "NSE:BAJAJ-AUTO-EQ", "NSE:BAJFINANCE-EQ",
            "NSE:BAJAJFINSV-EQ", "NSE:BEL-EQ", "NSE:BHARTIARTL-EQ", "NSE:BPCL-EQ",
            "NSE:BRITANNIA-EQ", "NSE:CIPLA-EQ", "NSE:COALINDIA-EQ", "NSE:DIVISLAB-EQ", "NSE:DRREDDY-EQ",
            "NSE:EICHERMOT-EQ", "NSE:GRASIM-EQ", "NSE:HCLTECH-EQ", "NSE:HDFCBANK-EQ",
            "NSE:HDFCLIFE-EQ", "NSE:HEROMOTOCO-EQ", "NSE:HINDALCO-EQ", "NSE:HINDUNILVR-EQ",
            "NSE:ICICIBANK-EQ", "NSE:INDUSINDBK-EQ", "NSE:INFY-EQ", "NSE:ITC-EQ",
            "NSE:JSWSTEEL-EQ", "NSE:KOTAKBANK-EQ", "NSE:LT-EQ", "NSE:M&M-EQ",
            "NSE:MARUTI-EQ", "NSE:NESTLEIND-EQ", "NSE:NTPC-EQ", "NSE:ONGC-EQ",
            "NSE:PIDILITIND-EQ", "NSE:POWERGRID-EQ", "NSE:RELIANCE-EQ", "NSE:SBILIFE-EQ", "NSE:SBIN-EQ",
            "NSE:SHREECEM-EQ", "NSE:SHRIRAMFIN-EQ", "NSE:SIEMENS-EQ", "NSE:SUNPHARMA-EQ", "NSE:TATACONSUM-EQ", "NSE:TATAMOTORS-EQ",
            "NSE:TATASTEEL-EQ", "NSE:TCS-EQ", "NSE:TECHM-EQ", "NSE:TITAN-EQ",
            "NSE:TRENT-EQ", "NSE:ULTRACEMCO-EQ", "NSE:WIPRO-EQ"
        };
    }
}
