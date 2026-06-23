namespace EcnesoftFieldSales.Domain;

public enum UserRole
{
    ADMIN,
    SALES
}

public enum CustomerType
{
    CURRENT,
    PROSPECT
}

public enum ProspectStatus
{
    ACTIVE,
    OPEN,
    TERMINATION,
    CLOSED,
    PROSPECT,
    OWNERSHIP
}

public enum CustomerLifecycleType
{
    ACTIVE,
    TERMINATION,
    CLOSED,
    PROSPECT,
    OWNERSHIP
}

public enum CompetitorType
{
    KPOS,
    ORDERNOW,
    QONUS,
    SQUARE,
    ETC
}

public sealed class UserAccount
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public string FullName { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class ClientGroup
{
    public int Id { get; set; }
    public string GroupName { get; set; } = "";
    public string HexColor { get; set; } = "#00FF00";
    public string? Description { get; set; }
}

public sealed class Customer
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string? ABN { get; set; }
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "NSW";
    public string Postcode { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public CustomerType CustomerType { get; set; }
    public ProspectStatus? ProspectStatus { get; set; }
    public CustomerLifecycleType Type { get; set; } = CustomerLifecycleType.PROSPECT;
    public DateOnly? TerminationDate { get; set; }
    public string? TerminationReason { get; set; }
    public CompetitorType? Competitor { get; set; }
    public string? GeneralNote { get; set; }
    public int? GroupId { get; set; }
    public int? AssignedUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SalesNote
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int UserId { get; set; }
    public string? CompetitorProduct { get; set; }
    public string Notes { get; set; } = "";
    public string? ImagePath { get; set; }
    public DateTimeOffset VisitedDate { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HappyVisitGroup
{
    public int Id { get; set; }
    public string GroupName { get; set; } = "";
    public CustomerLifecycleType Type { get; set; } = CustomerLifecycleType.ACTIVE;
    public List<int> CustomerIds { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DashboardPost
{
    public int Id { get; set; }
    public string PostName { get; set; } = "";
    public string Editor { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> ImagePaths { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
