using System.Text.Json;
using EcnesoftFieldSales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EcnesoftFieldSales.Infrastructure;

public sealed class SalesDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SalesDbContext(DbContextOptions<SalesDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ClientGroup> ClientGroups => Set<ClientGroup>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesNote> SalesNotes => Set<SalesNote>();
    public DbSet<HappyVisitGroup> HappyVisitGroups => Set<HappyVisitGroup>();
    public DbSet<DashboardPost> DashboardPosts => Set<DashboardPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("Users");
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.Email).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.Salt).IsRequired();
            entity.Property(user => user.FullName).IsRequired();
            entity.Property(user => user.Role).HasConversion<string>().IsRequired();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.UserId, token.ExpiresAt });
            entity.Property(token => token.TokenHash).IsRequired();
        });

        modelBuilder.Entity<ClientGroup>(entity =>
        {
            entity.ToTable("ClientGroups");
            entity.Property(group => group.GroupName).IsRequired();
            entity.Property(group => group.HexColor).HasDefaultValue("#00FF00");
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.Property(customer => customer.CompanyName).IsRequired();
            entity.Property(customer => customer.Address).IsRequired();
            entity.Property(customer => customer.City).IsRequired();
            entity.Property(customer => customer.State).HasDefaultValue("NSW");
            entity.Property(customer => customer.Postcode).IsRequired();
            entity.Property(customer => customer.CustomerType).HasConversion<string>().IsRequired();
            entity.Property(customer => customer.ProspectStatus).HasConversion<string>();
            entity.Property(customer => customer.Type).HasConversion<string>().IsRequired();
            entity.Property(customer => customer.Competitor).HasConversion<string>();
            entity.HasIndex(customer => new { customer.Type, customer.Postcode, customer.City });
            entity.HasIndex(customer => customer.AssignedUserId);
            entity.HasIndex(customer => customer.GroupId);
            entity.HasIndex(customer => new { customer.Latitude, customer.Longitude });
        });

        modelBuilder.Entity<SalesNote>(entity =>
        {
            entity.ToTable("SalesNotes");
            entity.HasIndex(note => new { note.CustomerId, note.VisitedDate });
            entity.Property(note => note.Notes).IsRequired();
        });

        modelBuilder.Entity<HappyVisitGroup>(entity =>
        {
            entity.ToTable("HappyVisitGroups");
            entity.Property(group => group.GroupName).IsRequired();
            entity.Property(group => group.Type).HasConversion<string>().IsRequired();
            entity.Property(group => group.CustomerIds)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, JsonOptions),
                    value => JsonSerializer.Deserialize<List<int>>(value, JsonOptions) ?? new List<int>())
                .Metadata.SetValueComparer(IntListComparer());
        });

        modelBuilder.Entity<DashboardPost>(entity =>
        {
            entity.ToTable("DashboardPosts");
            entity.Property(post => post.PostName).IsRequired();
            entity.Property(post => post.Editor).IsRequired();
            entity.Property(post => post.Description).IsRequired();
            entity.Property(post => post.ImagePaths)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, JsonOptions),
                    value => JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? new List<string>())
                .Metadata.SetValueComparer(StringListComparer());
        });
    }

    private static ValueComparer<List<int>> IntListComparer() =>
        new(
            (left, right) => (left ?? new List<int>()).SequenceEqual(right ?? new List<int>()),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
            value => value.ToList());

    private static ValueComparer<List<string>> StringListComparer() =>
        new(
            (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
            value => value.ToList());
}
