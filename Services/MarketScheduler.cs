using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FyersCopyTrading.Services
{
    public class MarketScheduler : IHostedService, IDisposable
    {
        private readonly ILogger<MarketScheduler> _log;
        private readonly IConfiguration _cfg;
        private readonly HistoricalDataService _historical;
        private Timer? _timer;

        public MarketScheduler(ILogger<MarketScheduler> log, IConfiguration cfg, HistoricalDataService historical)
        {
            _log = log;
            _cfg = cfg;
            _historical = historical;
        }

        public Task StartAsync(CancellationToken ct)
        {
            _log.LogInformation("MarketScheduler started – will fetch historical data while market is open.");
            // Run immediately and then every hour
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromHours(1));
            return Task.CompletedTask;
        }

        private async void DoWork(object? _)
        {
            try
            {
                _log.LogInformation("MarketScheduler executing historical data refresh...");
                await _historical.InitializeAllHistoricalDataAsync();
                _log.LogInformation("Historical data refresh completed.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error in MarketScheduler");
            }
        }

        public Task StopAsync(CancellationToken ct)
        {
            _log.LogInformation("MarketScheduler stopping.");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose() => _timer?.Dispose();
    }
}
