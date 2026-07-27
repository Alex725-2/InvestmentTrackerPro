using InvestmentTracker.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public DbSet<Broker> Brokers { get; set; }
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<AssetType> AssetTypes { get; set; }
        public DbSet<Security> Securities { get; set; }
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<PortfolioItem> PortfolioItems { get; set; }
        public DbSet<Quote> Quotes { get; set; }
        public DbSet<PaymentEvent> PaymentEvents { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<TestRecord> TestRecords { get; set; }
        public DbSet<TestRecord2> TestRecord2s { get; set; }
        public DbSet<DbBackup> DbBackups { get; set; }

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // ---------- Identity – заменяем nvarchar(max) на TEXT ТОЛЬКО для SQLite ----------
            if (Database.IsSqlite())
            {
                builder.Entity<ApplicationUser>(entity =>
                {
                    entity.Property(e => e.ConcurrencyStamp).HasColumnType("TEXT");
                    entity.Property(e => e.SecurityStamp).HasColumnType("TEXT");
                });
                builder.Entity<IdentityRole>(entity =>
                {
                    entity.Property(e => e.ConcurrencyStamp).HasColumnType("TEXT");
                });
                builder.Entity<IdentityUserLogin<string>>(entity =>
                {
                    entity.Property(e => e.ProviderKey).HasColumnType("TEXT");
                    entity.Property(e => e.LoginProvider).HasColumnType("TEXT");
                });
                builder.Entity<IdentityUserToken<string>>(entity =>
                {
                    entity.Property(e => e.Name).HasColumnType("TEXT");
                    entity.Property(e => e.LoginProvider).HasColumnType("TEXT");
                });
            }

            // ---------- decimal precision ----------
            builder.Entity<Broker>()
                .Property(b => b.DefaultCommissionRate).HasColumnType("decimal(18,4)");
            builder.Entity<Account>()
                .Property(a => a.CommissionRate).HasColumnType("decimal(18,4)");
            builder.Entity<PortfolioItem>()
                .Property(p => p.Quantity).HasColumnType("decimal(18,6)");
            builder.Entity<PortfolioItem>()
                .Property(p => p.AveragePurchasePrice).HasColumnType("decimal(18,4)");
            builder.Entity<Transaction>()
                .Property(t => t.Price).HasColumnType("decimal(18,4)");
            builder.Entity<Transaction>()
                .Property(t => t.Commission).HasColumnType("decimal(18,4)");
            builder.Entity<Quote>()
                .Property(q => q.Price).HasColumnType("decimal(18,4)");
            builder.Entity<PaymentEvent>()
                .Property(p => p.AmountPerUnit).HasColumnType("decimal(18,4)");

            // ---------- Каскадное удаление ----------
            builder.Entity<PortfolioItem>()
                .HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.Entity<Transaction>()
                .HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // ---------- Имя таблицы для PortfolioItem ----------
            builder.Entity<PortfolioItem>().ToTable("PortfolioItem");

            base.OnModelCreating(builder);
        }
    }
}