using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FyersCopyTrading.Models
{
    public class ChildOrder
    {
        [Key]
        public int ChildOrderId { get; set; }
        public int ParentOrderId { get; set; }
        public string ChildAccountId { get; set; } = string.Empty;
        public string ChildAccountName { get; set; } = string.Empty; // e.g. "Ramu (Child 1)"
        public string Symbol { get; set; } = string.Empty;
        public string OrderType { get; set; } = "BUY";
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }
        public int Quantity { get; set; } // Replicated Qty = Parent Qty * Multiplier
        public string OrderStatus { get; set; } = "EXECUTED";
        public DateTime ReplicatedAt { get; set; } = DateTime.Now;
    }
}
