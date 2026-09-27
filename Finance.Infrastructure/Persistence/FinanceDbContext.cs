using Microsoft.EntityFrameworkCore;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Infrastructure.Persistence;

public class FinanceDbContext : DbContext
{
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options)
    {
    }

    public DbSet<Transaction> Transactions { get; set; } = null!;
    public DbSet<Budget> Budgets { get; set; } = null!;

    // Enums are stored by name, so reordering or inserting enum members never changes stored data.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Category>().HaveConversion<string>();
        configurationBuilder.Properties<TransactionType>().HaveConversion<string>();
        configurationBuilder.Properties<AccountType>().HaveConversion<string>();
        configurationBuilder.Properties<Status>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Date).IsRequired();
            // The hash identifies a statement row, so the same purchase may exist once per account.
            entity.HasIndex(t => new { t.Account, t.Hash }).IsUnique();
            // Every list, filter and report is by date range and/or sorted by date.
            entity.HasIndex(t => t.Date);
        });

        modelBuilder.Entity<Budget>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Name).IsRequired();
            entity.Property(b => b.Description).IsRequired();
            entity.Property(b => b.Status).IsRequired();
            entity.HasMany(b => b.Transactions)
                .WithOne(t => t.Budget)
                .HasForeignKey(t => t.BudgetId)
                .IsRequired(false);
        });
    }
}
