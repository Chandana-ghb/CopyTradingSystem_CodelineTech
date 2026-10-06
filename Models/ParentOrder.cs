using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FyersCopyTrading.Models
{
    public class ParentOrder
    {
        [Key]
        public int OrderId { get; set; }
        public string ParentAccountId { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty; // e.g. "NSE:TCS-EQ"
        public string OrderType { get; set; } = "BUY"; // "BUY" or "SELL"
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }
        public int Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? StopLossPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? TargetPrice { get; set; }

        public string OrderStatus { get; set; } = "EXECUTED";
        public DateTime PlacedAt { get; set; } = DateTime.Now;
    }
}
