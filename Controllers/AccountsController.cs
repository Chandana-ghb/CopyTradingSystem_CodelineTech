using FyersCopyTrading.Data;
using FyersCopyTrading.Hubs;
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

        public AccountsController(CopyTradingDbContext db, IHubContext<MarketHub> hubContext)
        {
            _db = db;
            _hubContext = hubContext;
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
}
