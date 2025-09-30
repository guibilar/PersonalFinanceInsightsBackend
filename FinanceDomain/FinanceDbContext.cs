using FinanceDomain.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceDomain;

public class FinanceDbContext : DbContext
{
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options)
    {
    }

    public DbSet<Transaction> Transactions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id); // Primary key
            entity.Property(t => t.Date).IsRequired(); // Make Date a required field
        });
    }
}
