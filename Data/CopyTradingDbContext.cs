using Microsoft.EntityFrameworkCore;
using FyersCopyTrading.Models;

namespace FyersCopyTrading.Data
{
    public class CopyTradingDbContext : DbContext
    {
        public CopyTradingDbContext(DbContextOptions<CopyTradingDbContext> options) : base(options) { }

        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<AccountMapping> AccountMappings => Set<AccountMapping>();
        public DbSet<ParentOrder> ParentOrders => Set<ParentOrder>();
        public DbSet<ChildOrder> ChildOrders => Set<ChildOrder>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Seed Initial Accounts (Chandana + 4 Children)
            modelBuilder.Entity<Account>().HasData(
                new Account { AccountId = "P001", AccountName = "Chandana (Parent)", AccountType = "PARENT", Balance = 1000000.00m },
                new Account { AccountId = "C001", AccountName = "Ramu (Child 1)", AccountType = "CHILD", Balance = 500000.00m },
                new Account { AccountId = "C002", AccountName = "Seenu (Child 2)", AccountType = "CHILD", Balance = 500000.00m },
                new Account { AccountId = "C003", AccountName = "Priya (Child 3)", AccountType = "CHILD", Balance = 500000.00m },
                new Account { AccountId = "C004", AccountName = "Arjun (Child 4)", AccountType = "CHILD", Balance = 500000.00m }
            );

            // Configure AccountMapping relationships to avoid cascade delete cycles
            modelBuilder.Entity<AccountMapping>()
                .HasOne(am => am.ParentAccount)
                .WithMany()
                .HasForeignKey(am => am.ParentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AccountMapping>()
                .HasOne(am => am.ChildAccount)
                .WithMany()
                .HasForeignKey(am => am.ChildAccountId)
                .OnDelete(DeleteBehavior.Restrict);
            // Seed Parent‑to‑Child Mappings with Quantity Multipliers (used for order replication)
            modelBuilder.Entity<AccountMapping>().HasData(
                new AccountMapping { MappingId = 1, ParentAccountId = "P001", ChildAccountId = "C001", QtyMultiplier = 1.0m, IsActive = true },
                new AccountMapping { MappingId = 2, ParentAccountId = "P001", ChildAccountId = "C002", QtyMultiplier = 0.5m, IsActive = true },
                new AccountMapping { MappingId = 3, ParentAccountId = "P001", ChildAccountId = "C003", QtyMultiplier = 2.0m, IsActive = true },
                new AccountMapping { MappingId = 4, ParentAccountId = "P001", ChildAccountId = "C004", QtyMultiplier = 1.5m, IsActive = true }
            );
    }
}
}
