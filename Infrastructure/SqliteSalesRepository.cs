using System.Security.Claims;
using EcnesoftFieldSales.Domain;
using EcnesoftFieldSales.Services;
using Microsoft.EntityFrameworkCore;

namespace EcnesoftFieldSales.Infrastructure;

public sealed class SqliteSalesRepository : ISalesRepository
{
    private readonly SalesDbContext _db;

    public SqliteSalesRepository(SalesDbContext db)
    {
        _db = db;
    }

    public UserAccount? FindUserByLogin(string email) =>
        _db.Users.FirstOrDefault(user => user.Email.ToLower() == email.ToLower());

    public UserAccount? FindUserById(int? id) =>
        id.HasValue ? _db.Users.Find(id.Value) : null;

    public IReadOnlyList<UserAccount> GetUsers() =>
        _db.Users.OrderBy(user => user.Email).ToList();

    public UserAccount AddUser(UserAccount user)
    {
        user.CreatedAt = DateTimeOffset.UtcNow;
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    public UserAccount? UpdateUser(int id, UserAccount user, string? passwordHash, string? salt)
    {
        var existing = _db.Users.Find(id);
        if (existing is null)
        {
            return null;
        }

        existing.Email = user.Email;
        existing.FullName = user.FullName;
        existing.Role = user.Role;
        existing.IsActive = user.IsActive;
        if (!string.IsNullOrWhiteSpace(passwordHash) && !string.IsNullOrWhiteSpace(salt))
        {
            existing.PasswordHash = passwordHash;
            existing.Salt = salt;
        }

        _db.SaveChanges();
        return existing;
    }

    public bool DeactivateUser(int id)
    {
        var existing = _db.Users.Find(id);
        if (existing is null)
        {
            return false;
        }

        existing.IsActive = false;
        _db.SaveChanges();
        return true;
    }

    public IReadOnlyList<ClientGroup> GetGroups() =>
        _db.ClientGroups.OrderBy(group => group.GroupName).ToList();

    public ClientGroup? FindGroup(int? id) =>
        id.HasValue ? _db.ClientGroups.Find(id.Value) : null;

    public IReadOnlyList<Customer> GetCustomers(ClaimsPrincipal principal, string? customerType, string? prospectStatus, int? groupId)
    {
        var query = ScopeCustomersForUser(principal, _db.Customers.AsQueryable());

        if (Enum.TryParse<CustomerType>(customerType, ignoreCase: true, out var parsedType))
        {
            query = query.Where(customer => customer.CustomerType == parsedType);
        }

        if (Enum.TryParse<ProspectStatus>(prospectStatus, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(customer => customer.ProspectStatus == parsedStatus);
        }

        if (groupId.HasValue)
        {
            query = query.Where(customer => customer.GroupId == groupId.Value);
        }

        return query
            .OrderBy(customer => customer.Postcode)
            .ThenBy(customer => customer.CompanyName)
            .ToList();
    }

    public Customer? FindCustomer(int id) =>
        _db.Customers.Find(id);

    public Customer AddCustomer(Customer customer)
    {
        SyncLegacyCustomerFields(customer);
        customer.CreatedAt = DateTimeOffset.UtcNow;
        _db.Customers.Add(customer);
        _db.SaveChanges();
        return customer;
    }

    public Customer? UpdateCustomer(int id, Customer customer, ClaimsPrincipal principal)
    {
        var existing = ScopeCustomersForUser(principal, _db.Customers).FirstOrDefault(item => item.Id == id);
        if (existing is null)
        {
            return null;
        }

        CopyCustomer(existing, customer);
        _db.SaveChanges();
        return existing;
    }

    public int ClearCustomers(ClaimsPrincipal principal)
    {
        using var transaction = _db.Database.BeginTransaction();
        var removableIds = ScopeCustomersForUser(principal, _db.Customers)
            .Select(customer => customer.Id)
            .ToList();

        RemoveCustomersById(removableIds);
        _db.SaveChanges();
        transaction.Commit();
        return removableIds.Count;
    }

    public CustomerXmlImportResponse ReplaceCustomers(ClaimsPrincipal principal, IReadOnlyList<Customer> customers)
    {
        using var transaction = _db.Database.BeginTransaction();
        var removableIds = ScopeCustomersForUser(principal, _db.Customers)
            .Select(customer => customer.Id)
            .ToList();

        RemoveCustomersById(removableIds);
        var results = new List<CustomerXmlImportRowResult>();
        for (var index = 0; index < customers.Count; index += 1)
        {
            var customer = customers[index];
            SyncLegacyCustomerFields(customer);
            customer.CreatedAt = DateTimeOffset.UtcNow;
            _db.Customers.Add(customer);
            results.Add(new CustomerXmlImportRowResult(index + 2, true, customer.Id, customer.CompanyName, null));
        }

        _db.SaveChanges();
        for (var index = 0; index < customers.Count; index += 1)
        {
            results[index] = results[index] with { CustomerId = customers[index].Id };
        }

        transaction.Commit();
        return new CustomerXmlImportResponse(customers.Count, removableIds.Count, customers.Count, true, results);
    }

    public (Customer Customer, bool WasUpdated) UpsertCustomerByAbn(Customer customer)
    {
        var normalizedAbn = AbnValidator.Normalize(customer.ABN);
        var existing = _db.Customers.FirstOrDefault(item =>
            item.ABN != null &&
            item.ABN.Replace(" ", "") == normalizedAbn);

        if (existing is null)
        {
            customer.ABN = normalizedAbn;
            return (AddCustomer(customer), false);
        }

        CopyCustomer(existing, customer);
        existing.ABN = normalizedAbn;
        _db.SaveChanges();
        return (existing, true);
    }

    public bool UpdateCoordinates(int id, double latitude, double longitude, ClaimsPrincipal principal)
    {
        var customer = ScopeCustomersForUser(principal, _db.Customers).FirstOrDefault(item => item.Id == id);
        if (customer is null)
        {
            return false;
        }

        customer.Latitude = latitude;
        customer.Longitude = longitude;
        _db.SaveChanges();
        return true;
    }

    public SalesNote AddSalesNote(SalesNote note)
    {
        note.VisitedDate = DateTimeOffset.UtcNow;
        _db.SalesNotes.Add(note);
        _db.SaveChanges();
        return note;
    }

    public IReadOnlyList<SalesNote> GetSalesNotes(int? customerId)
    {
        var query = _db.SalesNotes.AsQueryable();
        if (customerId.HasValue)
        {
            query = query.Where(note => note.CustomerId == customerId.Value);
        }

        return query.OrderByDescending(note => note.VisitedDate).ToList();
    }

    public IReadOnlyList<Customer> GetProspectsForUser(ClaimsPrincipal principal) =>
        ScopeCustomersForUser(principal, _db.Customers)
            .Where(customer => customer.Type == CustomerLifecycleType.PROSPECT)
            .ToList();

    public IReadOnlyList<PenetrationDashboardRow> GetPenetrationRows() =>
        _db.Customers
            .AsEnumerable()
            .GroupBy(customer => new { customer.Postcode, customer.City })
            .Select(group =>
            {
                var current = group.Count(customer => customer.Type == CustomerLifecycleType.ACTIVE);
                var prospects = group.Count(customer => customer.Type == CustomerLifecycleType.PROSPECT);
                var total = current + prospects;
                var rate = total == 0 ? 0 : Math.Round((decimal)current * 100 / total, 2);
                var weight = total == 0 ? 0 : Math.Round((double)prospects / total, 4);

                return new PenetrationDashboardRow(
                    group.Key.Postcode,
                    group.Key.City,
                    current,
                    prospects,
                    total,
                    rate,
                    weight,
                    Math.Round(group.Average(customer => customer.Latitude), 6),
                    Math.Round(group.Average(customer => customer.Longitude), 6));
            })
            .OrderByDescending(row => row.ProspectStores)
            .ThenBy(row => row.Postcode)
            .ToList();

    public IReadOnlyList<HappyVisitGroup> GetHappyVisitGroups() =>
        _db.HappyVisitGroups.OrderByDescending(group => group.UpdatedAt).ToList();

    public HappyVisitGroup SaveHappyVisitGroup(HappyVisitGroup group)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = group.Id > 0 ? _db.HappyVisitGroups.Find(group.Id) : null;
        if (existing is null)
        {
            group.CreatedAt = now;
            group.UpdatedAt = now;
            group.CustomerIds = group.CustomerIds.Distinct().ToList();
            _db.HappyVisitGroups.Add(group);
            _db.SaveChanges();
            return group;
        }

        existing.GroupName = group.GroupName;
        existing.Type = group.Type;
        existing.CustomerIds = group.CustomerIds.Distinct().ToList();
        existing.UpdatedAt = now;
        _db.SaveChanges();
        return existing;
    }

    public IReadOnlyList<DashboardPost> GetDashboardPosts() =>
        _db.DashboardPosts.OrderByDescending(post => post.CreatedAt).ToList();

    public DashboardPost? FindDashboardPost(int id) =>
        _db.DashboardPosts.Find(id);

    public DashboardPost AddDashboardPost(DashboardPost post)
    {
        post.CreatedAt = DateTimeOffset.UtcNow;
        _db.DashboardPosts.Add(post);
        _db.SaveChanges();
        return post;
    }

    public DashboardPost? UpdateDashboardPost(int id, DashboardPost post, IReadOnlyList<string>? replacementImagePaths)
    {
        var existing = _db.DashboardPosts.Find(id);
        if (existing is null)
        {
            return null;
        }

        existing.PostName = post.PostName;
        existing.Editor = post.Editor;
        existing.Description = post.Description;
        if (replacementImagePaths is not null)
        {
            existing.ImagePaths = replacementImagePaths.ToList();
        }

        _db.SaveChanges();
        return existing;
    }

    public bool DeleteDashboardPost(int id)
    {
        var existing = _db.DashboardPosts.Find(id);
        if (existing is null)
        {
            return false;
        }

        _db.DashboardPosts.Remove(existing);
        _db.SaveChanges();
        return true;
    }

    public void StoreRefreshToken(RefreshToken token)
    {
        token.CreatedAt = DateTimeOffset.UtcNow;
        _db.RefreshTokens.Add(token);
        _db.SaveChanges();
    }

    public RefreshToken? FindActiveRefreshToken(string tokenHash) =>
        _db.RefreshTokens
            .Where(token => token.TokenHash == tokenHash && token.RevokedAt == null)
            .AsEnumerable()
            .FirstOrDefault(token =>
                token.ExpiresAt > DateTimeOffset.UtcNow);

    public void RevokeRefreshToken(string tokenHash)
    {
        foreach (var token in _db.RefreshTokens.Where(token => token.TokenHash == tokenHash && token.RevokedAt == null))
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
        }

        _db.SaveChanges();
    }

    public void RevokeRefreshTokensForUser(int userId)
    {
        foreach (var token in _db.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAt == null))
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
        }

        _db.SaveChanges();
    }

    private void RemoveCustomersById(IReadOnlyCollection<int> removableIds)
    {
        if (removableIds.Count == 0)
        {
            return;
        }

        _db.SalesNotes.RemoveRange(_db.SalesNotes.Where(note => removableIds.Contains(note.CustomerId)));
        _db.Customers.RemoveRange(_db.Customers.Where(customer => removableIds.Contains(customer.Id)));

        var groups = _db.HappyVisitGroups.ToList();
        foreach (var group in groups)
        {
            group.CustomerIds.RemoveAll(removableIds.Contains);
        }
        _db.HappyVisitGroups.RemoveRange(groups.Where(group => group.CustomerIds.Count == 0));
    }

    private static IQueryable<Customer> ScopeCustomersForUser(ClaimsPrincipal principal, IQueryable<Customer> source)
    {
        if (principal.IsInRole(UserRole.ADMIN.ToString()))
        {
            return source;
        }

        var userId = principal.GetUserId();
        return source.Where(customer => customer.AssignedUserId == null || customer.AssignedUserId == userId);
    }

    private static void CopyCustomer(Customer existing, Customer incoming)
    {
        existing.CompanyName = incoming.CompanyName;
        existing.ABN = AbnValidator.Normalize(incoming.ABN);
        existing.Address = incoming.Address;
        existing.City = incoming.City;
        existing.State = incoming.State;
        existing.Postcode = incoming.Postcode;
        existing.Phone = incoming.Phone;
        existing.Email = incoming.Email;
        existing.Latitude = incoming.Latitude;
        existing.Longitude = incoming.Longitude;
        existing.Type = incoming.Type;
        existing.TerminationDate = incoming.TerminationDate;
        existing.TerminationReason = incoming.TerminationReason;
        existing.Competitor = incoming.Competitor;
        existing.GeneralNote = incoming.GeneralNote;
        existing.GroupId = incoming.GroupId;
        existing.AssignedUserId = incoming.AssignedUserId;
        SyncLegacyCustomerFields(existing);
    }

    private static void SyncLegacyCustomerFields(Customer customer)
    {
        customer.CustomerType = customer.Type is CustomerLifecycleType.ACTIVE or CustomerLifecycleType.OWNERSHIP
            ? CustomerType.CURRENT
            : CustomerType.PROSPECT;

        customer.ProspectStatus = customer.Type switch
        {
            CustomerLifecycleType.ACTIVE => ProspectStatus.ACTIVE,
            CustomerLifecycleType.TERMINATION => ProspectStatus.TERMINATION,
            CustomerLifecycleType.CLOSED => ProspectStatus.CLOSED,
            CustomerLifecycleType.PROSPECT => ProspectStatus.PROSPECT,
            CustomerLifecycleType.OWNERSHIP => ProspectStatus.OWNERSHIP,
            _ => ProspectStatus.PROSPECT
        };
    }
}
