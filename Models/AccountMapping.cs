using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FyersCopyTrading.Models
{
    public class AccountMapping
    {
        [Key]
        public int MappingId { get; set; }
        public string ParentAccountId { get; set; } = string.Empty;
        public string ChildAccountId { get; set; } = string.Empty;
        
        [Column(TypeName = "decimal(5,2)")]
        public decimal QtyMultiplier { get; set; } = 1.0m;
        public bool IsActive { get; set; } = true;

        public string AllocationMode { get; set; } = "RATIO"; // "RATIO" or "FIXED"
        public int FixedQuantity { get; set; } = 1;
        public string AllowedSymbols { get; set; } = "ALL"; // "ALL" or comma-separated symbols e.g. "TCS,GOLD"

        [ForeignKey("ParentAccountId")]
        public Account? ParentAccount { get; set; }

        [ForeignKey("ChildAccountId")]
        public Account? ChildAccount { get; set; }
    }
}
