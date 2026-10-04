using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FyersCopyTrading.Models
{
    public class Account
    {
        [Key]
        public string AccountId { get; set; } = string.Empty; // e.g. "P001", "C001"
        public string AccountName { get; set; } = string.Empty; // e.g. "Chandana (Parent)", "Ramu (Child 1)"
        public string AccountType { get; set; } = "CHILD"; // "PARENT" or "CHILD"

        [Column(TypeName = "decimal(18,2)")]
        public decimal Balance { get; set; } = 500000.00m;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
