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

            if (stopLossPrice.HasValue || targetPrice.HasValue)
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

            // If Parent Order was Rejected due to lack of funds, we DO NOT reject children who have sufficient funds.
            // Execution proceeds to replicate and execute orders for any child account that has enough balance.

            // 3. Replicate Orders for Each Child Account with Business Scenarios
            var replicatedChildOrders = new List<ChildOrder>();
            foreach (var map in mappings)
            {
                var childAcc = map.ChildAccount ?? await _db.Accounts.FindAsync(map.ChildAccountId);
                
                // Calculate Child Quantity based on Allocation Mode (Fixed Lots vs Ratio Multiplier)
                int childQty;
                if (map.AllocationMode == "FIXED")
                {
                    childQty = map.FixedQuantity > 0 ? map.FixedQuantity : 1;
                }
                else
                {
                    decimal mult = map.QtyMultiplier > 0 ? map.QtyMultiplier : 1.0m;
                    childQty = (int)Math.Max(1, Math.Round(parentQty * mult));
                }

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
                    ReplicatedAt = DateTime.Now,
                    EntryTime = DateTime.Now
                };

                // SCENARIO: Deactivated Child Account Check
                if (!map.IsActive)
                {
                    childOrder.Quantity = 0;
                    childOrder.OrderStatus = "SKIPPED (INACTIVE)";
                    _logger.LogInformation($"[CHILD SKIPPED] Account {map.ChildAccountId} is marked deactivated. Order not placed.");
                }
                // SCENARIO: Symbol Whitelist & Blacklist Preference Check (e.g. only SILVER, or block GOLD)
                var symbolCheck = CheckSymbolAllowed(map.AllowedSymbols, symbol);
                if (!symbolCheck.IsAllowed)
                {
                    childOrder.Quantity = 0;
                    childOrder.OrderStatus = symbolCheck.Reason;
                    _logger.LogInformation($"[CHILD SKIPPED] Account {map.ChildAccountId} rule triggered for symbol {symbol} ({symbolCheck.Reason}). Filter: '{map.AllowedSymbols}'. Order not placed.");
                }
                // SCENARIO: Insufficient Funds in Child Account Check
                else if (pOrderType == "BUY" && childAcc != null && childAcc.Balance < childCost)
                {
                    childOrder.OrderStatus = "REJECTED (INSUFFICIENT FUNDS)";
                    _logger.LogWarning($"[CHILD REJECTED] {map.ChildAccountId} insufficient funds. Required: ₹{childCost:N2}, Available: ₹{childAcc.Balance:N2}. Child order not placed.");
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
                        DateTime exitTime = DateTime.Now;

                        parentOrder.OrderStatus = triggerReason;
                        parentOrder.ExitTime = exitTime;
                        parentOrder.ExitPrice = currentPrice;
                        parentOrder.RealizedPnL = parentOrder.OrderType == "BUY"
                            ? (currentPrice - parentOrder.Price) * parentOrder.Quantity
                            : (parentOrder.Price - currentPrice) * parentOrder.Quantity;

                        // Credit position settlement back to parent
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
                            cOrder.ExitTime = exitTime;
                            cOrder.ExitPrice = currentPrice;
                            cOrder.RealizedPnL = cOrder.OrderType == "BUY"
                                ? (currentPrice - cOrder.Price) * cOrder.Quantity
                                : (cOrder.Price - currentPrice) * cOrder.Quantity;

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

        // Manual square-off: exit parent position and all child positions immediately at current price
        public async Task<ParentOrderResult?> ManualSquareOffOrderAsync(int parentOrderId, decimal? exitPriceOverride = null)
        {
            var parentOrder = await _db.ParentOrders.FindAsync(parentOrderId);
            if (parentOrder == null || parentOrder.OrderStatus != "EXECUTED") return null;

            decimal exitPrice = exitPriceOverride ?? parentOrder.Price;
            if (MockTickService.Stocks.TryGetValue(parentOrder.Symbol, out var tick) && tick.Price > 0)
            {
                exitPrice = tick.Price;
            }

            var exitTime = DateTime.Now;
            string triggerReason = "CLOSED (MANUAL EXIT)";

            parentOrder.OrderStatus = triggerReason;
            parentOrder.ExitTime = exitTime;
            parentOrder.ExitPrice = exitPrice;
            parentOrder.RealizedPnL = parentOrder.OrderType == "BUY"
                ? (exitPrice - parentOrder.Price) * parentOrder.Quantity
                : (parentOrder.Price - exitPrice) * parentOrder.Quantity;

            var parentAcc = await _db.Accounts.FindAsync(parentOrder.ParentAccountId);
            if (parentAcc != null)
            {
                parentAcc.Balance += exitPrice * parentOrder.Quantity;
            }

            var childOrders = await _db.ChildOrders
                .Where(c => c.ParentOrderId == parentOrder.OrderId && c.OrderStatus == "EXECUTED")
                .ToListAsync();

            foreach (var cOrder in childOrders)
            {
                cOrder.OrderStatus = triggerReason;
                cOrder.ExitTime = exitTime;
                cOrder.ExitPrice = exitPrice;
                cOrder.RealizedPnL = cOrder.OrderType == "BUY"
                    ? (exitPrice - cOrder.Price) * cOrder.Quantity
                    : (cOrder.Price - exitPrice) * cOrder.Quantity;

                var cAcc = await _db.Accounts.FindAsync(cOrder.ChildAccountId);
                if (cAcc != null)
                {
                    cAcc.Balance += exitPrice * cOrder.Quantity;
                }
            }

            await _db.SaveChangesAsync();

            var result = new ParentOrderResult
            {
                ParentOrder = parentOrder,
                ChildOrders = childOrders
            };

            await _hubContext.Clients.All.SendAsync("OrderExecuted", result);
            await BroadcastAccountBalancesAsync();

            return result;
        }

        // Manual square-off for a SINGLE child account order (Child Exit Signal)
        public async Task<ChildOrder?> ManualSquareOffChildOrderAsync(int childOrderId, decimal? exitPriceOverride = null)
        {
            var childOrder = await _db.ChildOrders.FindAsync(childOrderId);
            if (childOrder == null || childOrder.OrderStatus != "EXECUTED") return null;

            decimal exitPrice = exitPriceOverride ?? childOrder.Price;
            if (MockTickService.Stocks.TryGetValue(childOrder.Symbol, out var tick) && tick.Price > 0)
            {
                exitPrice = tick.Price;
            }

            var exitTime = DateTime.Now;
            string triggerReason = "CLOSED (MANUAL EXIT)";

            childOrder.OrderStatus = triggerReason;
            childOrder.ExitTime = exitTime;
            childOrder.ExitPrice = exitPrice;
            childOrder.RealizedPnL = childOrder.OrderType == "BUY"
                ? (exitPrice - childOrder.Price) * childOrder.Quantity
                : (childOrder.Price - exitPrice) * childOrder.Quantity;

            var childAcc = await _db.Accounts.FindAsync(childOrder.ChildAccountId);
            if (childAcc != null)
            {
                childAcc.Balance += exitPrice * childOrder.Quantity;
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation($"[CHILD MANUAL EXIT] ChildOrder #{childOrder.ChildOrderId} ({childOrder.ChildAccountId}) exited at ₹{exitPrice:N2}, Realized PnL: ₹{childOrder.RealizedPnL:N2}");

            var parentOrder = await _db.ParentOrders.FindAsync(childOrder.ParentOrderId);
            if (parentOrder != null)
            {
                await _hubContext.Clients.All.SendAsync("OrderExecuted", new ParentOrderResult
                {
                    ParentOrder = parentOrder,
                    ChildOrders = new List<ChildOrder> { childOrder }
                });
            }

            await BroadcastAccountBalancesAsync();

            return childOrder;
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

        private static (bool IsAllowed, string Reason) CheckSymbolAllowed(string? allowedSymbols, string currentSymbol)
        {
            if (string.IsNullOrWhiteSpace(allowedSymbols) || allowedSymbols.Trim().Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                return (true, "OK");
            }

            var cleanCurrent = currentSymbol
                .Replace("NSE:", "", StringComparison.OrdinalIgnoreCase)
                .Replace("MCX:", "", StringComparison.OrdinalIgnoreCase)
                .Replace("-EQ", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            var tokens = allowedSymbols.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // 1. Check if explicitly BLOCKED first (e.g. "!GOLD", "BLOCK:GOLD", "NOT:GOLD")
            foreach (var token in tokens)
            {
                var trimmed = token.Trim();
                if (trimmed.StartsWith("!", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("NOT:", StringComparison.OrdinalIgnoreCase))
                {
                    var blockedSymbol = trimmed.TrimStart('!')
                        .Replace("BLOCK:", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("NOT:", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("NSE:", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("MCX:", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("-EQ", "", StringComparison.OrdinalIgnoreCase)
                        .Trim();

                    if (cleanCurrent.Equals(blockedSymbol, StringComparison.OrdinalIgnoreCase) ||
                        currentSymbol.Contains(blockedSymbol, StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, "SKIPPED (SYMBOL BLOCKED)");
                    }
                }
            }

            // 2. Filter positive allowed tokens
            var allowTokens = tokens
                .Where(t => !t.StartsWith("!", StringComparison.OrdinalIgnoreCase) &&
                            !t.StartsWith("BLOCK:", StringComparison.OrdinalIgnoreCase) &&
                            !t.StartsWith("NOT:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // If user only configured block rules (e.g. "BLOCK:GOLD"), all non-blocked symbols are allowed!
            if (!allowTokens.Any())
            {
                return (true, "OK");
            }

            foreach (var token in allowTokens)
            {
                if (token.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                    return (true, "OK");

                var cleanToken = token
                    .Replace("NSE:", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("MCX:", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("-EQ", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();

                if (cleanCurrent.Equals(cleanToken, StringComparison.OrdinalIgnoreCase) ||
                    currentSymbol.Contains(cleanToken, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, "OK");
                }
            }

            return (false, "SKIPPED (SYMBOL NOT ALLOWED)");
        }
    }

    public class ParentOrderResult
    {
        public ParentOrder ParentOrder { get; set; } = null!;
        public List<ChildOrder> ChildOrders { get; set; } = new();
    }
}
