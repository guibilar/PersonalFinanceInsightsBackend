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
    public DbSet<AccountBalance> AccountBalances { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Tag> Tags { get; set; } = null!;
    public DbSet<CategoryRule> CategoryRules { get; set; } = null!;
    public DbSet<FlowRule> FlowRules { get; set; } = null!;
    public DbSet<SeedMarker> SeedMarkers { get; set; } = null!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<UserSession> UserSessions { get; set; } = null!;

    // Enums are stored by name, so reordering or inserting enum members never changes stored data.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<TransactionType>().HaveConversion<string>();
        configurationBuilder.Properties<AccountType>().HaveConversion<string>();
        configurationBuilder.Properties<RegisteredPlan>().HaveConversion<string>();
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
            // Deleting a category moves its transactions to another one first (see CategoryRepository).
            entity.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
            // Every list, filter and report is by date range and/or sorted by date.
            entity.HasIndex(t => t.Date);
            // Hidden (deleted) transactions exist only for duplicate detection unless a query asks for them
            // with IgnoreQueryFilters.
            entity.HasQueryFilter(t => !t.Hidden);
            entity.Property(t => t.Note).HasMaxLength(TextLimits.TransactionNote);
            // Deleting a tag (or a transaction) removes just the pairing.
            entity.HasMany(t => t.Tags).WithMany(t => t.Transactions).UsingEntity("TransactionTags");
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(TextLimits.TagName);
            // Case-sensitive here; the service also refuses names differing only in case.
            entity.HasIndex(t => t.Name).IsUnique();
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
            entity.HasOne(r => r.Category).WithMany().HasForeignKey(r => r.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(TextLimits.CategoryName);
            // Case-sensitive here; the service also refuses names differing only in case.
            entity.HasIndex(c => c.Name).IsUnique();
            entity.Property(c => c.Color).IsRequired().HasMaxLength(7);
            entity.Ignore(c => c.IsBuiltIn);
            // The categories there were before categories were data; a migration moved the stored transactions,
            // rules and budgets onto them.
            entity.HasData(DefaultCategories.Create());
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Name).IsRequired().HasMaxLength(TextLimits.AccountName);
            entity.Property(a => a.Plan).HasMaxLength(10).HasDefaultValue(RegisteredPlan.None).HasSentinel(RegisteredPlan.None);
            // Case-sensitive here; the service also refuses names differing only in case.
            entity.HasIndex(a => a.Name).IsUnique();
            // Every database starts with the three accounts there were before accounts were data; a migration
            // moved the existing transactions onto them. They can be renamed, or deleted while empty.
            entity.HasData(
                new Account { Id = Account.DefaultChequingId, Name = "Chequing", Type = AccountType.Chequing, Format = StatementFormat.Cibc },
                new Account { Id = Account.DefaultSavingsId, Name = "Savings", Type = AccountType.Savings, Format = StatementFormat.Cibc },
                new Account { Id = Account.DefaultCreditCardId, Name = "Credit card", Type = AccountType.CreditCard, Format = StatementFormat.Cibc });
        });

        modelBuilder.Entity<AccountBalance>(entity =>
        {
            entity.HasKey(b => b.Id);
            // One balance a day per account: entering another for the day replaces it.
            entity.HasIndex(b => new { b.AccountId, b.Date }).IsUnique();
            // An account can only be deleted while it has no transactions; its balances go with it.
            entity.HasOne(b => b.Account).WithMany().HasForeignKey(b => b.AccountId).OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(b => b.Merchant).HasMaxLength(TextLimits.BudgetMerchant);
            entity.HasMany(b => b.Transactions)
                .WithOne(t => t.Budget)
                .HasForeignKey(t => t.BudgetId)
                .IsRequired(false);
            entity.HasMany(b => b.Categories)
                .WithOne(c => c.Budget)
                .HasForeignKey(c => c.BudgetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BudgetCategory>(entity =>
        {
            // The category is the key: it is counted by one budget only.
            entity.HasKey(c => c.CategoryId);
            // A deleted category is no longer counted by its budget.
            entity.HasOne(c => c.Category).WithMany().HasForeignKey(c => c.CategoryId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
