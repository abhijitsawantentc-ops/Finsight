using Microsoft.EntityFrameworkCore;
using Portfolio.Service.Entities;

namespace Portfolio.Service.Data;

public class PortfolioDbContext : DbContext
{
    public PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : base(options)
    {
    }

    public DbSet<Entities.Portfolio> Portfolios => Set<Entities.Portfolio>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<TradeExecution> Trades => Set<TradeExecution>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Entities.Portfolio>(b =>
        {
            b.ToTable("Portfolios");
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).HasMaxLength(150).IsRequired();
            b.Property(p => p.BaseCurrency).HasMaxLength(10).IsRequired();
            b.Property(p => p.CashBalance).HasPrecision(18, 4);

            b.HasMany(p => p.Positions)
             .WithOne()
             .HasForeignKey(pos => pos.PortfolioId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(p => p.Trades)
             .WithOne()
             .HasForeignKey(t => t.PortfolioId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Position>(b =>
        {
            b.ToTable("Positions");
            b.HasKey(p => p.Id);
            b.Property(p => p.Symbol).HasMaxLength(20).IsRequired();
            b.Property(p => p.Quantity).HasPrecision(18, 6);
            b.Property(p => p.AveragePurchasePrice).HasPrecision(18, 4);
            b.Property(p => p.CurrentMarketPrice).HasPrecision(18, 4);
            b.HasIndex(p => new { p.PortfolioId, p.Symbol }).IsUnique();
        });

        modelBuilder.Entity<TradeExecution>(b =>
        {
            b.ToTable("TradeExecutions");
            b.HasKey(t => t.Id);
            b.Property(t => t.Symbol).HasMaxLength(20).IsRequired();
            b.Property(t => t.Quantity).HasPrecision(18, 6);
            b.Property(t => t.Price).HasPrecision(18, 4);
            b.Property(t => t.Fee).HasPrecision(18, 4);
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(o => o.Id);
            b.Property(o => o.Type).HasMaxLength(256).IsRequired();
            b.Property(o => o.Content).IsRequired();
            b.HasIndex(o => o.ProcessedOnUtc);
        });
    }
}
