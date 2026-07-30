using EventDriven.Publisher.Api.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace EventDriven.Publisher.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder
            .Entity<Order>()
            .Property(o => o.TotalAmount)
            .HasPrecision(18, 4);

        // las tablas internas que MassTransit necesita para el Outbox Pattern.
        modelBuilder.AddTransactionalOutboxEntities();
    }
}