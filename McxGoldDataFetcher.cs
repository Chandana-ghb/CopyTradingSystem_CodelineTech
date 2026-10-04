using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FyersCSharpSDK;
using Newtonsoft.Json.Linq;

namespace FyersCopyTrading
{
    public class McxGoldDataFetcher : FyersSocketDelegate
    {
        private FyersSocket _client;
        private readonly string _logFilePath;
        private readonly object _fileLock = new();

        // Fyers symbol format for MCX futures typically looks like:
        // MCX:GOLD24DECFUT (Gold near month)
        // MCX:GOLDM24DECFUT (Gold Mini)
        // MCX:SILVER24DECFUT (Silver)
        // Active MCX Gold futures contracts (October & December 2026)
        private readonly List<string> _mcxSymbols = new List<string>
        {
            "MCX:GOLD26OCTFUT",
            "MCX:GOLD26DECFUT",
            "MCX:GOLDM26OCTFUT",
            "MCX:GOLDM26NOVFUT",
            "MCX:GOLDM26DECFUT"
        };

        public McxGoldDataFetcher()
        {
            string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Data");
            Directory.CreateDirectory(dataDir);
            _logFilePath = Path.Combine(dataDir, $"MCX_Gold_Ticks_{DateTime.Now:yyyy-MM-dd}.txt");
        }

        private void LogToFile(string line)
        {
            lock (_fileLock)
            {
                File.AppendAllText(_logFilePath, $"{DateTime.Now:HH:mm:ss.fff} | {line}{Environment.NewLine}");
            }
        }

        public async Task RunAsync(string appId, string accessToken)
        {
            Console.Title = "Fyers v3 WebSocket - MCX Gold Live Data";
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("===============================================================");
            Console.WriteLine("       FYERS API v3 - MCX GOLD LIVE WEBSOCKET                  ");
            Console.WriteLine("===============================================================");
            Console.ResetColor();

            // Set up global Fyers SDK instance
            FyersClass fyersModel = FyersClass.Instance;
            fyersModel.ClientId = appId;
            fyersModel.AccessToken = accessToken;

            Console.WriteLine("[System] Preparing to connect to TBT WebSocket for MCX...");
            Console.WriteLine($"[System] Subscribing to: {string.Join(", ", _mcxSymbols)}");
            LogToFile($"=== MCX Gold Live Feed Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            Console.WriteLine($"[System] Tick data will be saved to: {Path.GetFullPath(_logFilePath)}");

            _client = new FyersSocket();
            _client.webSocketDelegate = this;
            
            Console.WriteLine("[System] Initiating HSM Market Data connection...");
            _client.ConnectHSM(HyperSyncLib.ChannelModes.FULL, HyperSyncLib.UserTypes.Normal, false);

            Console.WriteLine("[System] Sending subscription list for Market Data...");
            _client.SubscribeData(_mcxSymbols, false);
            Console.WriteLine("[System] Market data subscription sent.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[System] Press Ctrl+C to stop the MCX feed...\n");
            Console.ResetColor();

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
            catch (TaskCanceledException)
            {
                // Expected when Ctrl+C is pressed
            }

            Console.WriteLine("[System] MCX Session ended. Goodbye.");
        }

        // --- FyersSocketDelegate Implementation ---

        public void OnClose(string status)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[WebSocket] MCX Connection is closed: " + status);
            Console.ResetColor();
            LogToFile($"[DISCONNECTED] {status}");
        }

        public void OnOpen(string status)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[WebSocket] MCX Connection is open: " + status);
            Console.ResetColor();
            LogToFile($"[CONNECTED] {status}");
        }

        public void OnOrder(JObject orders) { }
        public void OnTrade(JObject trades) { }
        public void OnPosition(JObject positions) { }
        public void OnIndex(JObject index) { }

        public void OnScrips(JObject scrips)
        {
            string data = scrips["data"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("[MCX Gold Tick] " + data);
            Console.ResetColor();
            LogToFile($"[GOLD] {data}");
        }

        public void OnDepth(JObject depths)
        {
            string data = depths["data"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            Console.WriteLine("[MCX Depth] " + data);
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
}
