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

        [ForeignKey("ParentAccountId")]
        public Account? ParentAccount { get; set; }

        [ForeignKey("ChildAccountId")]
        public Account? ChildAccount { get; set; }
    }
}
