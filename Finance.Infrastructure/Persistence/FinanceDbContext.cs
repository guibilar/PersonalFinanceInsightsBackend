using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Finance.Contracts.Inputs;
using Finance.Domain.Entities;
using Finance.Domain.Enums;

namespace Finance.Infrastructure.Persistence;

/// <summary>
/// Finance data plus the sign-in tables: ASP.NET Core Identity users, and the keys that encrypt session
/// cookies (kept in the database so sessions survive the API container being recreated).
/// </summary>
public class FinanceDbContext : IdentityDbContext<IdentityUser>, IDataProtectionKeyContext
{
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options)
    {
    }

    public DbSet<Transaction> Transactions { get; set; } = null!;
    public DbSet<Budget> Budgets { get; set; } = null!;
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<CategoryRule> CategoryRules { get; set; } = null!;
    public DbSet<FlowRule> FlowRules { get; set; } = null!;
    public DbSet<SeedMarker> SeedMarkers { get; set; } = null!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<UserSession> UserSessions { get; set; } = null!;

    // Enums are stored by name, so reordering or inserting enum members never changes stored data.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Category>().HaveConversion<string>();
        configurationBuilder.Properties<TransactionType>().HaveConversion<string>();
        configurationBuilder.Properties<AccountType>().HaveConversion<string>();
        configurationBuilder.Properties<Status>().HaveConversion<string>();
        configurationBuilder.Properties<BudgetPeriod>().HaveConversion<string>();
        configurationBuilder.Properties<Flow>().HaveConversion<string>();
        configurationBuilder.Properties<StatementFormat>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Date).IsRequired();
            // The hash identifies a statement row, so the same purchase may exist once per account.
            entity.HasIndex(t => new { t.AccountId, t.Hash }).IsUnique();
            // An account with transactions can't be deleted: they would come back on the next import.
            entity.HasOne(t => t.Account).WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);
            // Every list, filter and report is by date range and/or sorted by date.
            entity.HasIndex(t => t.Date);
            // Hidden (deleted) transactions exist only for duplicate detection unless a query asks for them
            // with IgnoreQueryFilters.
            entity.HasQueryFilter(t => !t.Hidden);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).HasMaxLength(64);
            // Deleting the account ends its sessions.
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CategoryRule>(entity =>
        {
            entity.HasKey(r => r.Id);
            // Stored upper-case, so this is unique ignoring case.
            entity.Property(r => r.Keyword).IsRequired().HasMaxLength(TextLimits.CategoryRuleKeyword);
            entity.HasIndex(r => r.Keyword).IsUnique();
            // Not unique: renumbering after a move saves rows one by one, which would briefly repeat a position.
            entity.HasIndex(r => r.Position);
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Name).IsRequired().HasMaxLength(TextLimits.AccountName);
            // Case-sensitive here; the service also refuses names differing only in case.
            entity.HasIndex(a => a.Name).IsUnique();
            // Every database starts with the three accounts there were before accounts were data; a migration
            // moved the existing transactions onto them. They can be renamed, or deleted while empty.
            entity.HasData(
                new Account { Id = Account.DefaultChequingId, Name = "Chequing", Type = AccountType.Chequing, Format = StatementFormat.Cibc },
                new Account { Id = Account.DefaultSavingsId, Name = "Savings", Type = AccountType.Savings, Format = StatementFormat.Cibc },
                new Account { Id = Account.DefaultCreditCardId, Name = "Credit card", Type = AccountType.CreditCard, Format = StatementFormat.Cibc });
        });

        modelBuilder.Entity<FlowRule>(entity =>
        {
            entity.HasKey(r => r.Id);
            // Stored upper-case, so this is unique ignoring case.
            entity.Property(r => r.Keyword).IsRequired().HasMaxLength(TextLimits.FlowRuleKeyword);
            entity.HasIndex(r => r.Keyword).IsUnique();
        });

        modelBuilder.Entity<SeedMarker>(entity =>
        {
            entity.HasKey(m => m.Name);
            entity.Property(m => m.Name).HasMaxLength(64);
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
            entity.HasMany(b => b.Categories)
                .WithOne(c => c.Budget)
                .HasForeignKey(c => c.BudgetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // The category is the key: it is counted by one budget only.
        modelBuilder.Entity<BudgetCategory>(entity => entity.HasKey(c => c.Category));
    }
}
