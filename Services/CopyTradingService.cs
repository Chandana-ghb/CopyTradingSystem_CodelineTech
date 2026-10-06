using System.Collections.Concurrent;
using FyersCopyTrading.Data;
using FyersCopyTrading.Hubs;
using FyersCopyTrading.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FyersCopyTrading.Services
{
    public class CopyTradingService
    {
        public static readonly ConcurrentDictionary<string, bool> ActiveSymbolsForSlTarget = new();

        private readonly CopyTradingDbContext _db;
        private readonly IHubContext<MarketHub> _hubContext;
        private readonly ILogger<CopyTradingService> _logger;

        public CopyTradingService(CopyTradingDbContext db, IHubContext<MarketHub> hubContext, ILogger<CopyTradingService> logger)
        {
            _db = db;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<ParentOrderResult> PlaceParentOrderAsync(
            string parentAccountId, 
            string symbol, 
            string orderType, 
            decimal price, 
            int parentQty,
            decimal? stopLossPrice = null,
            decimal? targetPrice = null)
        {
            var pOrderType = orderType.ToUpperInvariant();
            decimal totalCost = price * parentQty;

            // SCENARIO 1: Parent Account Validation & Insufficient Funds Check
            var parentAccount = await _db.Accounts.FindAsync(parentAccountId);
            if (parentAccount == null)
            {
                throw new InvalidOperationException($"Parent Account '{parentAccountId}' does not exist.");
            }

            bool isParentInsufficientFunds = (pOrderType == "BUY" && parentAccount.Balance < totalCost);

            if (isParentInsufficientFunds)
            {
                _logger.LogWarning($"[PARENT ORDER REJECTED] Parent {parentAccountId} has insufficient balance. Required: ₹{totalCost:N2}, Available: ₹{parentAccount.Balance:N2}. Order rejected at execution time.");
            }
            else
            {
                // Deduct / Credit Parent Account Balance
                if (pOrderType == "BUY")
                {
                    parentAccount.Balance -= totalCost;
                }
                else
                {
                    parentAccount.Balance += totalCost;
                }
            }

            // 1. Create and Save Parent Order in ParentOrders table
            var parentOrder = new ParentOrder
            {
                ParentAccountId = parentAccountId,
                Symbol = symbol,
                OrderType = pOrderType,
                Price = price,
                Quantity = isParentInsufficientFunds ? 0 : parentQty,
                StopLossPrice = stopLossPrice,
                TargetPrice = targetPrice,
                OrderStatus = isParentInsufficientFunds ? "REJECTED (INSUFFICIENT FUNDS)" : "EXECUTED",
                PlacedAt = DateTime.Now
            };

            _db.ParentOrders.Add(parentOrder);
            await _db.SaveChangesAsync();

            if (!isParentInsufficientFunds && (stopLossPrice.HasValue || targetPrice.HasValue))
            {
                ActiveSymbolsForSlTarget[symbol] = true;
            }

            _logger.LogInformation($"[PARENT ORDER RECORDED] ID: {parentOrder.OrderId} for {symbol} Status: {parentOrder.OrderStatus} (Balance: ₹{parentAccount.Balance:N2})");

            // 2. Fetch Mapped Active Children from AccountMappings table
            var mappings = await _db.AccountMappings
                .Include(m => m.ChildAccount)
                .Where(m => m.ParentAccountId == parentAccountId)
                .ToListAsync();

            if (!mappings.Any())
            {
                mappings = await _db.Accounts
                    .Where(a => a.AccountType == "CHILD")
                    .Select(a => new AccountMapping { 
                        ChildAccountId = a.AccountId, 
                        ChildAccount = a, 
                        QtyMultiplier = 1.0m, 
                        IsActive = true 
                    })
                    .ToListAsync();
            }

            // If Parent Order was Rejected due to lack of funds, reject child replications with ₹0 deducted
            if (isParentInsufficientFunds)
            {
                var rejectedChildOrders = new List<ChildOrder>();
                foreach (var map in mappings)
                {
                    var childAcc = map.ChildAccount ?? await _db.Accounts.FindAsync(map.ChildAccountId);
                    var childOrder = new ChildOrder
                    {
                        ParentOrderId = parentOrder.OrderId,
                        ChildAccountId = map.ChildAccountId,
                        ChildAccountName = childAcc?.AccountName ?? map.ChildAccountId,
                        Symbol = symbol,
                        OrderType = pOrderType,
                        Price = price,
                        Quantity = 0,
                        StopLossPrice = stopLossPrice,
                        TargetPrice = targetPrice,
                        OrderStatus = "REJECTED (PARENT ORDER REJECTED)",
                        ReplicatedAt = DateTime.Now
                    };
                    _db.ChildOrders.Add(childOrder);
                    rejectedChildOrders.Add(childOrder);
                }
                await _db.SaveChangesAsync();

                var accountsList = await _db.Accounts.ToListAsync();
                await _hubContext.Clients.All.SendAsync("AccountsUpdated", accountsList);
                await _hubContext.Clients.All.SendAsync("OrderExecuted", new ParentOrderResult
                {
                    ParentOrder = parentOrder,
                    ChildOrders = rejectedChildOrders
                });

                return new ParentOrderResult
                {
                    ParentOrder = parentOrder,
                    ChildOrders = rejectedChildOrders
                };
            }

            // 3. Replicate Orders for Each Child Account with Business Scenarios
            var replicatedChildOrders = new List<ChildOrder>();
            foreach (var map in mappings)
            {
                var childAcc = map.ChildAccount ?? await _db.Accounts.FindAsync(map.ChildAccountId);
                int childQty = (int)Math.Max(1, Math.Round(parentQty * (map.QtyMultiplier > 0 ? map.QtyMultiplier : 1.0m)));
                decimal childCost = price * childQty;

                var childOrder = new ChildOrder
                {
                    ParentOrderId = parentOrder.OrderId,
                    ChildAccountId = map.ChildAccountId,
                    ChildAccountName = childAcc?.AccountName ?? map.ChildAccountId,
                    Symbol = symbol,
                    OrderType = pOrderType,
                    Price = price,
                    Quantity = childQty,
                    StopLossPrice = stopLossPrice,
                    TargetPrice = targetPrice,
                    ReplicatedAt = DateTime.Now
                };

                // SCENARIO 2: Inactive Child Account Check
                if (!map.IsActive)
                {
                    childOrder.OrderStatus = "SKIPPED (INACTIVE)";
                    _logger.LogInformation($"[CHILD SKIPPED] Account {map.ChildAccountId} is marked inactive.");
                }
                // SCENARIO 3: Insufficient Funds in Child Account Check
                else if (pOrderType == "BUY" && childAcc != null && childAcc.Balance < childCost)
                {
                    childOrder.OrderStatus = "REJECTED (INSUFFICIENT FUNDS)";
                    _logger.LogWarning($"[CHILD REJECTED] {map.ChildAccountId} insufficient funds. Required: ₹{childCost:N2}, Available: ₹{childAcc.Balance:N2}. Child order not placed.");
                    // DO NOT DEDUCT BALANCE FOR THIS CHILD
                }
                // SUCCESSFUL REPLICATION
                else
                {
                    childOrder.OrderStatus = "EXECUTED";
                    if (childAcc != null)
                    {
                        if (pOrderType == "BUY")
                        {
                            childAcc.Balance -= childCost;
                        }
                        else
                        {
                            childAcc.Balance += childCost;
                        }
                    }
                    _logger.LogInformation($"[CHILD EXECUTED] {map.ChildAccountId} replicated {childQty} @ ₹{price}");
                }

                replicatedChildOrders.Add(childOrder);
            }

            _db.ChildOrders.AddRange(replicatedChildOrders);
            await _db.SaveChangesAsync();

            var result = new ParentOrderResult
            {
                ParentOrder = parentOrder,
                ChildOrders = replicatedChildOrders
            };

            // 4. Push Real-time SignalR Event to Angular UI (Orders & Balances)
            await _hubContext.Clients.All.SendAsync("OrderExecuted", result);
            await BroadcastAccountBalancesAsync();

            return result;
        }

        public async Task CheckAndTriggerStopLossOrTargetAsync(string symbol, decimal currentPrice)
        {
            if (!ActiveSymbolsForSlTarget.ContainsKey(symbol)) return;

            try
            {
                // Find open executed parent orders for this symbol that have SL or Target set
                var activeParentOrders = await _db.ParentOrders
                    .Where(p => p.Symbol == symbol && p.OrderStatus == "EXECUTED" && (p.StopLossPrice != null || p.TargetPrice != null))
                    .ToListAsync();

                if (!activeParentOrders.Any())
                {
                    ActiveSymbolsForSlTarget.TryRemove(symbol, out _);
                    return;
                }

                foreach (var parentOrder in activeParentOrders)
                {
                    bool slHit = false;
                    bool targetHit = false;

                    if (parentOrder.OrderType == "BUY")
                    {
                        if (parentOrder.StopLossPrice.HasValue && currentPrice <= parentOrder.StopLossPrice.Value) slHit = true;
                        else if (parentOrder.TargetPrice.HasValue && currentPrice >= parentOrder.TargetPrice.Value) targetHit = true;
                    }
                    else if (parentOrder.OrderType == "SELL")
                    {
                        if (parentOrder.StopLossPrice.HasValue && currentPrice >= parentOrder.StopLossPrice.Value) slHit = true;
                        else if (parentOrder.TargetPrice.HasValue && currentPrice <= parentOrder.TargetPrice.Value) targetHit = true;
                    }

                    if (slHit || targetHit)
                    {
                        string triggerReason = slHit ? "CLOSED (SL HIT)" : "CLOSED (TARGET HIT)";
                        parentOrder.OrderStatus = triggerReason;

                        // Credit closing value back to parent
                        var parentAcc = await _db.Accounts.FindAsync(parentOrder.ParentAccountId);
                        if (parentAcc != null)
                        {
                            parentAcc.Balance += currentPrice * parentOrder.Quantity;
                        }

                        // Close associated executed child orders as well
                        var childOrders = await _db.ChildOrders
                            .Where(c => c.ParentOrderId == parentOrder.OrderId && c.OrderStatus == "EXECUTED")
                            .ToListAsync();

                        foreach (var cOrder in childOrders)
                        {
                            cOrder.OrderStatus = triggerReason;
                            var cAcc = await _db.Accounts.FindAsync(cOrder.ChildAccountId);
                            if (cAcc != null)
                            {
                                cAcc.Balance += currentPrice * cOrder.Quantity;
                            }
                        }

                        await _db.SaveChangesAsync();

                        _logger.LogInformation($"⚡ [{triggerReason}] Order #{parentOrder.OrderId} for {symbol} triggered at ₹{currentPrice}");

                        // Broadcast trigger update
                        await _hubContext.Clients.All.SendAsync("OrderExecuted", new ParentOrderResult
                        {
                            ParentOrder = parentOrder,
                            ChildOrders = childOrders
                        });
                        await BroadcastAccountBalancesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking Stop Loss / Target for {symbol}");
            }
        }

        public async Task BroadcastAccountBalancesAsync()
        {
            try
            {
                var accounts = await _db.Accounts.ToListAsync();
                await _hubContext.Clients.All.SendAsync("AccountsUpdated", accounts);
            }
            catch { }
        }
    }

    public class ParentOrderResult
    {
        public ParentOrder ParentOrder { get; set; } = null!;
        public List<ChildOrder> ChildOrders { get; set; } = new();
    }
}
