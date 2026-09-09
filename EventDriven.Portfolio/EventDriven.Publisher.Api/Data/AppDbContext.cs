using EventDriven.Publisher.Api.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace EventDriven.Publisher.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    // 🏛️ Estado de la Saga persistido en SQL Server
    public DbSet<OrderState> OrderStates { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder
            .Entity<Order>()
            .Property(o => o.TotalAmount)
            .HasPrecision(18, 4);

        modelBuilder.Entity<OrderState>(entity =>
        {
            entity.HasKey(x => x.CorrelationId);
            entity.Property(x => x.CurrentState).HasMaxLength(64);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
        });

        // las tablas internas que MassTransit necesita para el Outbox Pattern.
        modelBuilder.AddTransactionalOutboxEntities();
    }
}