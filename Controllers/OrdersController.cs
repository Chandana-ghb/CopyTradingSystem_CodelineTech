using FyersCopyTrading.Data;
using FyersCopyTrading.Models;
using FyersCopyTrading.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FyersCopyTrading.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly CopyTradingService _copyTradingService;
        private readonly CopyTradingDbContext _db;

        public OrdersController(CopyTradingService copyTradingService, CopyTradingDbContext db)
        {
            _copyTradingService = copyTradingService;
            _db = db;
        }

        // POST api/orders/parent (Place Parent Order -> Triggers Copy Trading Engine)
        [HttpPost("parent")]
        public async Task<IActionResult> PlaceParentOrder([FromBody] PlaceOrderRequest req)
        {
            if (req.Quantity <= 0) return BadRequest(new { message = "Quantity must be greater than 0." });
            if (req.Price <= 0) return BadRequest(new { message = "Price must be greater than 0." });

            var parentAccountId = string.IsNullOrEmpty(req.ParentAccountId) ? "P001" : req.ParentAccountId;

            try
            {
                var result = await _copyTradingService.PlaceParentOrderAsync(
                    parentAccountId, 
                    req.Symbol, 
                    req.OrderType, 
                    req.Price, 
                    req.Quantity,
                    req.StopLossPrice,
                    req.TargetPrice);

                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET api/orders/parent (Get All Parent Orders)
        [HttpGet("parent")]
        public async Task<IActionResult> GetParentOrders()
        {
            var orders = await _db.ParentOrders
                .OrderByDescending(o => o.PlacedAt)
                .ToListAsync();
            return Ok(orders);
        }

        // POST api/orders/parent/{orderId}/square-off (Manual Square Off / Exit Position)
        [HttpPost("parent/{orderId}/square-off")]
        public async Task<IActionResult> SquareOffOrder(int orderId)
        {
            var result = await _copyTradingService.ManualSquareOffOrderAsync(orderId);
            if (result == null) return NotFound(new { message = $"Order #{orderId} not found or already closed." });
            return Ok(result);
        }

        // POST api/orders/child/{childOrderId}/square-off (Individual Child Manual Exit Signal)
        [HttpPost("child/{childOrderId}/square-off")]
        public async Task<IActionResult> SquareOffChildOrder(int childOrderId)
        {
            var result = await _copyTradingService.ManualSquareOffChildOrderAsync(childOrderId);
            if (result == null) return NotFound(new { message = $"Child Order #{childOrderId} not found or already closed." });
            return Ok(result);
        }

        // GET api/orders/child (Get All Child Orders across accounts)
        [HttpGet("child")]
        public async Task<IActionResult> GetChildOrders([FromQuery] string? childAccountId)
        {
            var query = _db.ChildOrders.AsQueryable();
            if (!string.IsNullOrEmpty(childAccountId))
            {
                query = query.Where(c => c.ChildAccountId == childAccountId);
            }

            var orders = await query
                .OrderByDescending(c => c.ReplicatedAt)
                .ToListAsync();
            return Ok(orders);
        }
    }

    public class PlaceOrderRequest
    {
        public string ParentAccountId { get; set; } = "P001";
        public string Symbol { get; set; } = "NSE:TCS-EQ";
        public string OrderType { get; set; } = "BUY"; // BUY or SELL
        public decimal Price { get; set; }
        public int Quantity { get; set; } = 10;
        public decimal? StopLossPrice { get; set; }
        public decimal? TargetPrice { get; set; }
    }
}
