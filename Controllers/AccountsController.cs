using FyersCopyTrading.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyersCopyTrading.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountsController : ControllerBase
    {
        private readonly CopyTradingDbContext _db;

        public AccountsController(CopyTradingDbContext db)
        {
            _db = db;
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
    }
}
