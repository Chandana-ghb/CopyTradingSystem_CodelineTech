using FyersCopyTrading.Data;
using FyersCopyTrading.Hubs;
using FyersCopyTrading.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FyersCopyTrading.Services
{
    public class CopyTradingService
    {
        private readonly CopyTradingDbContext _db;
        private readonly IHubContext<MarketHub> _hubContext;
        private readonly ILogger<CopyTradingService> _logger;

        public CopyTradingService(CopyTradingDbContext db, IHubContext<MarketHub> hubContext, ILogger<CopyTradingService> logger)
        {
            _db = db;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<ParentOrderResult> PlaceParentOrderAsync(string parentAccountId, string symbol, string orderType, decimal price, int parentQty)
        {
            // 1. Create and Save Parent Order in ParentOrders table
            var parentOrder = new ParentOrder
            {
                ParentAccountId = parentAccountId,
                Symbol = symbol,
                OrderType = orderType.ToUpper(),
                Price = price,
                Quantity = parentQty,
                OrderStatus = "EXECUTED",
                PlacedAt = DateTime.Now
            };

            _db.ParentOrders.Add(parentOrder);

            // Update Parent Account Balance
            var parentAccount = await _db.Accounts.FindAsync(parentAccountId);
            if (parentAccount != null)
            {
                decimal totalCost = price * parentQty;
                if (orderType.Equals("BUY", StringComparison.OrdinalIgnoreCase))
                {
                    parentAccount.Balance = Math.Max(0, parentAccount.Balance - totalCost);
                }
                else
                {
                    parentAccount.Balance += totalCost;
                }
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation($"[PARENT ORDER PLACED] Order #{parentOrder.OrderId} for {symbol} Qty: {parentQty} @ {price}");

            // 2. Fetch Mapped Active Children from AccountMappings table
            var mappings = await _db.AccountMappings
                .Include(m => m.ChildAccount)
                .Where(m => m.ParentAccountId == parentAccountId && m.IsActive)
                .ToListAsync();

            if (!mappings.Any())
            {
                mappings = await _db.Accounts
                    .Where(a => a.AccountType == "CHILD")
                    .Select(a => new AccountMapping { ChildAccountId = a.AccountId, ChildAccount = a, QtyMultiplier = 1.0m, IsActive = true })
                    .ToListAsync();
            }

            // 3. Replicate Orders for Each Child Account and save to ChildOrders table
            var replicatedChildOrders = new List<ChildOrder>();
            foreach (var map in mappings)
            {
                int childQty = (int)Math.Max(1, Math.Round(parentQty * map.QtyMultiplier));
                var childOrder = new ChildOrder
                {
                    ParentOrderId = parentOrder.OrderId,
                    ChildAccountId = map.ChildAccountId,
                    ChildAccountName = map.ChildAccount?.AccountName ?? map.ChildAccountId,
                    Symbol = symbol,
                    OrderType = orderType.ToUpper(),
                    Price = price,
                    Quantity = childQty,
                    OrderStatus = "EXECUTED",
                    ReplicatedAt = DateTime.Now
                };
                replicatedChildOrders.Add(childOrder);

                // Update Child Account Balance in database
                if (map.ChildAccount != null)
                {
                    decimal childCost = price * childQty;
                    if (orderType.Equals("BUY", StringComparison.OrdinalIgnoreCase))
                    {
                        map.ChildAccount.Balance = Math.Max(0, map.ChildAccount.Balance - childCost);
                    }
                    else
                    {
                        map.ChildAccount.Balance += childCost;
                    }
                }
            }

            _db.ChildOrders.AddRange(replicatedChildOrders);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"[COPY TRADING] Successfully replicated {replicatedChildOrders.Count} child orders for Parent Order #{parentOrder.OrderId}");

            var result = new ParentOrderResult
            {
                ParentOrder = parentOrder,
                ChildOrders = replicatedChildOrders
            };

            // 4. Push Real-time SignalR Event to Angular UI
            await _hubContext.Clients.All.SendAsync("OrderExecuted", result);

            return result;
        }
    }

    public class ParentOrderResult
    {
        public ParentOrder ParentOrder { get; set; } = null!;
        public List<ChildOrder> ChildOrders { get; set; } = new();
    }
}
