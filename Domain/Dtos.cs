using System.Security.Claims;

namespace EcnesoftFieldSales.Domain;

public sealed record LoginRequest(string Email, string Password, bool UseCookie = true);

public sealed record LoginResponse(
    int Id,
    string Email,
    string FullName,
    string Role,
    string Token,
    DateTimeOffset ExpiresAt,
    string CsrfToken);

public sealed record UserProfileResponse(int Id, string Email, string FullName, string Role);

public sealed record UserAccountDto(
    int Id,
    string Email,
    string FullName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt)
{
    public static UserAccountDto From(UserAccount user) =>
        new(user.Id, user.Email, user.FullName, user.Role.ToString(), user.IsActive, user.CreatedAt);
}

public sealed record CreateUserRequest(
    string Email,
    string Password,
    string FullName,
    string Role,
    bool IsActive = true);

public sealed record UpdateUserRequest(
    string? Email,
    string? Password,
    string? FullName,
    string? Role,
    bool? IsActive);

public sealed record GroupDto(int Id, string GroupName, string HexColor, string? Description)
{
    public static GroupDto From(ClientGroup group) =>
        new(group.Id, group.GroupName, group.HexColor, group.Description);
}

public sealed record CustomerDto(
    int Id,
    string CompanyName,
    string? ABN,
    string Address,
    string City,
    string State,
    string Postcode,
    string? Phone,
    string? Email,
    double Latitude,
    double Longitude,
    string CustomerType,
    string? ProspectStatus,
    string Type,
    DateOnly? TerminationDate,
    string? TerminationReason,
    string? Competitor,
    string? GeneralNote,
    int? GroupId,
    string? GroupName,
    string? GroupColor,
    int? AssignedUserId,
    string? AssignedUserName,
    DateTimeOffset CreatedAt)
{
    public static CustomerDto From(Customer customer, ClientGroup? group, UserAccount? assignedUser) =>
        new(
            customer.Id,
            customer.CompanyName,
            customer.ABN,
            customer.Address,
            customer.City,
            customer.State,
            customer.Postcode,
            customer.Phone,
            customer.Email,
            customer.Latitude,
            customer.Longitude,
            customer.CustomerType.ToString(),
            customer.ProspectStatus?.ToString(),
            customer.Type.ToString(),
            customer.TerminationDate,
            customer.TerminationReason,
            customer.Competitor?.ToString(),
            customer.GeneralNote,
            customer.GroupId,
            group?.GroupName,
            group?.HexColor,
            customer.AssignedUserId,
            assignedUser?.FullName,
            customer.CreatedAt);
}

public sealed record CreateCustomerRequest(
    string CompanyName,
    string? ABN,
    string Address,
    string City,
    string State,
    string Postcode,
    string? Phone,
    string? Email,
    double Latitude,
    double Longitude,
    string? Type,
    string? CustomerType,
    string? ProspectStatus,
    DateOnly? TerminationDate,
    string? TerminationReason,
    string? Competitor,
    string? GeneralNote,
    int? GroupId,
    int? AssignedUserId);

public sealed record CoordinateUpdateRequest(double Latitude, double Longitude);

public sealed record SalesNoteDto(
    int Id,
    int CustomerId,
    int UserId,
    string? CompetitorProduct,
    string Notes,
    string? ImagePath,
    DateTimeOffset VisitedDate)
{
    public static SalesNoteDto From(SalesNote note) =>
        new(note.Id, note.CustomerId, note.UserId, note.CompetitorProduct, note.Notes, note.ImagePath, note.VisitedDate);
}

public sealed record RecommendationDto(
    int Id,
    string CompanyName,
    string Address,
    string City,
    string Postcode,
    string? ProspectStatus,
    double Latitude,
    double Longitude,
    double DistanceKm);

public sealed record HappyVisitGroupDto(
    int Id,
    string GroupName,
    string Type,
    IReadOnlyList<int> CustomerIds,
    DateTimeOffset UpdatedAt)
{
    public static HappyVisitGroupDto From(HappyVisitGroup group) =>
        new(group.Id, group.GroupName, group.Type.ToString(), group.CustomerIds, group.UpdatedAt);
}

public sealed record SaveHappyVisitGroupRequest(
    string GroupName,
    string Type,
    IReadOnlyList<int> CustomerIds);

public sealed record DashboardPostDto(
    int Id,
    string PostName,
    string Editor,
    string Description,
    IReadOnlyList<string> ImagePaths,
    DateTimeOffset CreatedAt)
{
    public static DashboardPostDto From(DashboardPost post) =>
        new(post.Id, post.PostName, post.Editor, post.Description, post.ImagePaths, post.CreatedAt);
}

public sealed record PenetrationDashboardRow(
    string Postcode,
    string Suburb,
    int CurrentCustomers,
    int ProspectStores,
    int TotalStores,
    decimal PenetrationRate,
    double HeatmapWeight,
    double? CentroidLatitude,
    double? CentroidLongitude);

public sealed record BulkImportError(int RowNumber, string ABN, string Reason);

public sealed record BulkImportResponse(
    int TotalRows,
    int Inserted,
    int Updated,
    int Skipped,
    IReadOnlyList<BulkImportError> Errors);

public sealed record CustomerXmlImportRequest(IReadOnlyList<CreateCustomerRequest> Customers);

public sealed record CustomerXmlImportRowResult(
    int RowNumber,
    bool Success,
    int? CustomerId,
    string CompanyName,
    string? Error);

public sealed record CustomerXmlImportResponse(
    int TotalRows,
    int Deleted,
    int Inserted,
    bool Committed,
    IReadOnlyList<CustomerXmlImportRowResult> Rows);

public sealed class CustomerImportRow
{
    public int RowNumber { get; init; }
    public string ABN { get; init; } = "";
    public string? CompanyName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? Postcode { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? CustomerType { get; init; }
    public string? ProspectStatus { get; init; }
    public int? GroupId { get; init; }
    public int? AssignedUserId { get; init; }
}

public static class ClaimsPrincipalExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public static UserProfileResponse ToProfile(this ClaimsPrincipal principal) =>
        new(
            principal.GetUserId(),
            principal.FindFirstValue(ClaimTypes.Email) ?? "",
            principal.FindFirstValue(ClaimTypes.Name) ?? "",
            principal.FindFirstValue(ClaimTypes.Role) ?? "");
}
