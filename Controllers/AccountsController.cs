using FyersCopyTrading.Data;
using FyersCopyTrading.Hubs;
using FyersCopyTrading.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FyersCopyTrading.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountsController : ControllerBase
    {
        private readonly CopyTradingDbContext _db;
        private readonly IHubContext<MarketHub> _hubContext;
        private readonly ILogger<AccountsController> _logger;

        public AccountsController(CopyTradingDbContext db, IHubContext<MarketHub> hubContext, ILogger<AccountsController> logger)
        {
            _db = db;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetAccounts()
        {
            var accounts = await _db.Accounts.ToListAsync();
            return Ok(accounts);
        }

        [HttpGet("mappings")]
        public async Task<IActionResult> GetMappings()
        {
            var mappings = await _db.AccountMappings
                .Include(m => m.ParentAccount)
                .Include(m => m.ChildAccount)
                .ToListAsync();
            return Ok(mappings);
        }

        [HttpPost("toggle-child-active")]
        public async Task<IActionResult> ToggleChildActive([FromBody] ToggleActiveRequest req)
        {
            var mapping = await _db.AccountMappings
                .FirstOrDefaultAsync(m => m.ChildAccountId == req.ChildAccountId);
            
            if (mapping == null)
            {
                mapping = new AccountMapping
                {
                    ParentAccountId = "P001",
                    ChildAccountId = req.ChildAccountId,
                    QtyMultiplier = 1.0m,
                    IsActive = false, // Toggle from initial active state
                    AllocationMode = "RATIO",
                    FixedQuantity = 1
                };
                _db.AccountMappings.Add(mapping);
            }
            else
            {
                mapping.IsActive = !mapping.IsActive;
            }

            // Sync with Accounts table
            var childAcc = await _db.Accounts.FindAsync(req.ChildAccountId);
            if (childAcc != null)
            {
                childAcc.IsActive = mapping.IsActive;
            }

            await _db.SaveChangesAsync();

            var allMappings = await _db.AccountMappings
                .Include(m => m.ParentAccount)
                .Include(m => m.ChildAccount)
                .ToListAsync();
            await _hubContext.Clients.All.SendAsync("MappingsUpdated", allMappings);

            var allAccounts = await _db.Accounts.ToListAsync();
            await _hubContext.Clients.All.SendAsync("AccountsUpdated", allAccounts);

            return Ok(new { 
                message = $"Child account {req.ChildAccountId} is now {(mapping.IsActive ? "ACTIVE" : "DEACTIVATED")}.", 
                mapping,
                mappings = allMappings
            });
        }

        [HttpPost("update-child-sizing")]
        public async Task<IActionResult> UpdateChildSizing([FromBody] UpdateSizingRequest req)
        {
            var mapping = await _db.AccountMappings
                .FirstOrDefaultAsync(m => m.ChildAccountId == req.ChildAccountId);

            if (mapping == null)
            {
                mapping = new AccountMapping
                {
                    ParentAccountId = "P001",
                    ChildAccountId = req.ChildAccountId,
                    QtyMultiplier = req.Multiplier ?? 1.0m,
                    AllocationMode = req.AllocationMode?.ToUpperInvariant() ?? "RATIO",
                    FixedQuantity = req.FixedQuantity ?? 1,
                    IsActive = true
                };
                _db.AccountMappings.Add(mapping);
            }
            else
            {
                if (req.Multiplier.HasValue && req.Multiplier.Value > 0)
                {
                    mapping.QtyMultiplier = req.Multiplier.Value;
                }
                if (!string.IsNullOrEmpty(req.AllocationMode))
                {
                    mapping.AllocationMode = req.AllocationMode.ToUpperInvariant();
                }
                if (req.FixedQuantity.HasValue && req.FixedQuantity.Value > 0)
                {
                    mapping.FixedQuantity = req.FixedQuantity.Value;
                }
            }

            await _db.SaveChangesAsync();

            var allMappings = await _db.AccountMappings
                .Include(m => m.ParentAccount)
                .Include(m => m.ChildAccount)
                .ToListAsync();
            await _hubContext.Clients.All.SendAsync("MappingsUpdated", allMappings);

            return Ok(new { 
                message = $"Updated lot sizing for child {req.ChildAccountId}.", 
                mapping,
                mappings = allMappings
            });
        }

        [HttpPost("add-funds")]
        public async Task<IActionResult> AddFunds([FromBody] AddFundsRequest req)
        {
            if (req.Amount <= 0) return BadRequest(new { message = "Deposit amount must be greater than zero." });

            if (req.AccountId.Equals("ALL_CHILDREN", StringComparison.OrdinalIgnoreCase))
            {
                var childAccounts = await _db.Accounts.Where(a => a.AccountType == "CHILD").ToListAsync();
                foreach (var child in childAccounts)
                {
                    child.Balance += req.Amount;
                }
                await _db.SaveChangesAsync();

                var updatedAccountsList = await _db.Accounts.ToListAsync();
                await _hubContext.Clients.All.SendAsync("AccountsUpdated", updatedAccountsList);

                return Ok(new { 
                    message = $"Successfully deposited ₹{req.Amount:N2} to all {childAccounts.Count} child accounts.",
                    accounts = updatedAccountsList 
                });
            }

            var account = await _db.Accounts.FindAsync(req.AccountId);
            if (account == null) return NotFound(new { message = $"Account '{req.AccountId}' not found." });

            account.Balance += req.Amount;
            await _db.SaveChangesAsync();

            // Broadcast real-time balance update to all connected frontend clients
            var allAccounts = await _db.Accounts.ToListAsync();
            await _hubContext.Clients.All.SendAsync("AccountsUpdated", allAccounts);

            return Ok(new { 
                message = $"Successfully added ₹{req.Amount:N2} to {account.AccountName}.",
                account 
            });
        }

        [HttpPost("set-balance")]
        public async Task<IActionResult> SetBalance([FromBody] SetBalanceRequest req)
        {
            var account = await _db.Accounts.FindAsync(req.AccountId);
            if (account == null) return NotFound(new { message = $"Account '{req.AccountId}' not found." });

            account.Balance = Math.Max(0, req.Balance);
            await _db.SaveChangesAsync();

            var allAccounts = await _db.Accounts.ToListAsync();
            await _hubContext.Clients.All.SendAsync("AccountsUpdated", allAccounts);

            return Ok(new { 
                message = $"Updated {account.AccountName} balance to ₹{account.Balance:N2}.",
                account 
            });
        }

        [HttpPost("reset-default-funds")]
        public async Task<IActionResult> ResetDefaultFunds()
        {
            var accounts = await _db.Accounts.ToListAsync();
            foreach (var acc in accounts)
            {
                acc.Balance = acc.AccountType == "PARENT" ? 1000000.00m : 500000.00m;
            }
            await _db.SaveChangesAsync();

            await _hubContext.Clients.All.SendAsync("AccountsUpdated", accounts);

            return Ok(new { 
                message = "All accounts reset to default funds (Parent: ₹10,00,000, Children: ₹5,00,000 each).", 
                accounts 
            });
        }

        [HttpPost("create-client")]
        public async Task<IActionResult> CreateClient([FromBody] CreateClientRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return BadRequest(new { message = "Client name is required." });
            }

            // Find next available Child Account ID (e.g. C001, C002, ..., C005)
            var existingChildIds = await _db.Accounts
                .Where(a => a.AccountType == "CHILD")
                .Select(a => a.AccountId)
                .ToListAsync();

            int nextNum = 1;
            foreach (var id in existingChildIds)
            {
                if (id.StartsWith("C", StringComparison.OrdinalIgnoreCase) && int.TryParse(id.Substring(1), out int n))
                {
                    if (n >= nextNum) nextNum = n + 1;
                }
            }
            string newAccountId = $"C{nextNum:D3}";

            var newAccount = new Account
            {
                AccountId = newAccountId,
                AccountName = $"{req.Name.Trim()} (Child {nextNum})",
                AccountType = "CHILD",
                Balance = req.InitialBalance > 0 ? req.InitialBalance : 500000.00m,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            _db.Accounts.Add(newAccount);

            var newMapping = new AccountMapping
            {
                ParentAccountId = "P001",
                ChildAccountId = newAccountId,
                QtyMultiplier = req.Multiplier > 0 ? req.Multiplier : 1.0m,
                IsActive = true,
                AllocationMode = !string.IsNullOrWhiteSpace(req.AllocationMode) ? req.AllocationMode.ToUpperInvariant() : "RATIO",
                FixedQuantity = req.FixedQuantity > 0 ? req.FixedQuantity : 1,
                AllowedSymbols = !string.IsNullOrWhiteSpace(req.AllowedSymbols) ? req.AllowedSymbols.Trim() : "ALL"
            };
            _db.AccountMappings.Add(newMapping);

            await _db.SaveChangesAsync();

            var allAccounts = await _db.Accounts.ToListAsync();
            await _hubContext.Clients.All.SendAsync("AccountsUpdated", allAccounts);

            var allMappings = await _db.AccountMappings
                .Include(m => m.ParentAccount)
                .Include(m => m.ChildAccount)
                .ToListAsync();
            await _hubContext.Clients.All.SendAsync("MappingsUpdated", allMappings);

            return Ok(new
            {
                message = $"Client account '{newAccount.AccountName}' ({newAccountId}) created successfully!",
                account = newAccount,
                mapping = newMapping,
                accounts = allAccounts,
                mappings = allMappings
            });
        }

        [HttpDelete("delete-client/{accountId}")]
        [HttpPost("delete-client/{accountId}")]
        public async Task<IActionResult> DeleteClient(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return BadRequest(new { message = "Account ID is required." });
            }

            if (accountId.Equals("P001", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Cannot delete the master Parent account." });
            }

            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.AccountId == accountId);
            if (account == null)
            {
                return NotFound(new { message = $"Account '{accountId}' not found." });
            }

            // 1. Remove associated mappings
            var mappings = await _db.AccountMappings.Where(m => m.ChildAccountId == accountId).ToListAsync();
            _db.AccountMappings.RemoveRange(mappings);

            // 2. Remove associated child orders
            var orders = await _db.ChildOrders.Where(o => o.ChildAccountId == accountId).ToListAsync();
            _db.ChildOrders.RemoveRange(orders);

            // 3. Remove account
            _db.Accounts.Remove(account);

            await _db.SaveChangesAsync();

            // Broadcast real-time SignalR updates
            var allAccounts = await _db.Accounts.ToListAsync();
            await _hubContext.Clients.All.SendAsync("AccountsUpdated", allAccounts);

            var allMappings = await _db.AccountMappings
                .Include(m => m.ParentAccount)
                .Include(m => m.ChildAccount)
                .ToListAsync();
            await _hubContext.Clients.All.SendAsync("MappingsUpdated", allMappings);

            _logger.LogInformation($"[CLIENT DELETED] Account {accountId} ('{account.AccountName}') removed successfully.");

            return Ok(new
            {
                message = $"Client account '{account.AccountName}' ({accountId}) removed successfully.",
                deletedAccountId = accountId,
                accounts = allAccounts,
                mappings = allMappings
            });
        }

        [HttpPost("update-allowed-symbols")]
        public async Task<IActionResult> UpdateAllowedSymbols([FromBody] UpdateAllowedSymbolsRequest req)
        {
            var mapping = await _db.AccountMappings
                .FirstOrDefaultAsync(m => m.ChildAccountId == req.ChildAccountId);
            if (mapping == null) return NotFound(new { message = $"Mapping for child '{req.ChildAccountId}' not found." });

            mapping.AllowedSymbols = !string.IsNullOrWhiteSpace(req.AllowedSymbols) ? req.AllowedSymbols.Trim() : "ALL";
            await _db.SaveChangesAsync();

            var allMappings = await _db.AccountMappings
                .Include(m => m.ParentAccount)
                .Include(m => m.ChildAccount)
                .ToListAsync();
            await _hubContext.Clients.All.SendAsync("MappingsUpdated", allMappings);

            return Ok(new
            {
                message = $"Updated allowed symbols for child {req.ChildAccountId} to '{mapping.AllowedSymbols}'.",
                mapping,
                mappings = allMappings
            });
        }
    }

    public class CreateClientRequest
    {
        public string Name { get; set; } = string.Empty;
        public decimal InitialBalance { get; set; } = 500000m;
        public decimal Multiplier { get; set; } = 1.0m;
        public string AllocationMode { get; set; } = "RATIO";
        public int FixedQuantity { get; set; } = 1;
        public string AllowedSymbols { get; set; } = "ALL";
    }

    public class UpdateAllowedSymbolsRequest
    {
        public string ChildAccountId { get; set; } = string.Empty;
        public string AllowedSymbols { get; set; } = "ALL";
    }

    public class AddFundsRequest
    {
        public string AccountId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class SetBalanceRequest
    {
        public string AccountId { get; set; } = string.Empty;
        public decimal Balance { get; set; }
    }

    public class ToggleActiveRequest
    {
        public string ChildAccountId { get; set; } = string.Empty;
    }

    public class UpdateSizingRequest
    {
        public string ChildAccountId { get; set; } = string.Empty;
        public decimal? Multiplier { get; set; }
        public string? AllocationMode { get; set; }
        public int? FixedQuantity { get; set; }
        public string? AllowedSymbols { get; set; }
    }
}
