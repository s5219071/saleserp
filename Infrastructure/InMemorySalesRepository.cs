using System.Security.Claims;
using EcnesoftFieldSales.Domain;
using EcnesoftFieldSales.Services;

namespace EcnesoftFieldSales.Infrastructure;

public interface ISalesRepository
{
    UserAccount? FindUserByLogin(string email);
    UserAccount? FindUserById(int? id);
    IReadOnlyList<UserAccount> GetUsers();
    IReadOnlyList<ClientGroup> GetGroups();
    ClientGroup? FindGroup(int? id);
    IReadOnlyList<Customer> GetCustomers(ClaimsPrincipal principal, string? customerType, string? prospectStatus, int? groupId);
    Customer? FindCustomer(int id);
    Customer AddCustomer(Customer customer);
    Customer? UpdateCustomer(int id, Customer customer, ClaimsPrincipal principal);
    (Customer Customer, bool WasUpdated) UpsertCustomerByAbn(Customer customer);
    bool UpdateCoordinates(int id, double latitude, double longitude, ClaimsPrincipal principal);
    SalesNote AddSalesNote(SalesNote note);
    IReadOnlyList<SalesNote> GetSalesNotes(int? customerId);
    IReadOnlyList<Customer> GetProspectsForUser(ClaimsPrincipal principal);
    IReadOnlyList<PenetrationDashboardRow> GetPenetrationRows();
    IReadOnlyList<HappyVisitGroup> GetHappyVisitGroups();
    HappyVisitGroup SaveHappyVisitGroup(HappyVisitGroup group);
    IReadOnlyList<DashboardPost> GetDashboardPosts();
    DashboardPost? FindDashboardPost(int id);
    DashboardPost AddDashboardPost(DashboardPost post);
    DashboardPost? UpdateDashboardPost(int id, DashboardPost post, IReadOnlyList<string>? replacementImagePaths);
    bool DeleteDashboardPost(int id);
}

public sealed class InMemorySalesRepository : ISalesRepository
{
    private readonly object _sync = new();
    private readonly List<UserAccount> _users = [];
    private readonly List<ClientGroup> _groups = [];
    private readonly List<Customer> _customers = [];
    private readonly List<SalesNote> _notes = [];
    private readonly List<HappyVisitGroup> _happyVisitGroups = [];
    private readonly List<DashboardPost> _dashboardPosts = [];
    private int _nextCustomerId = 1;
    private int _nextNoteId = 1;
    private int _nextHappyVisitGroupId = 1;
    private int _nextDashboardPostId = 1;

    public InMemorySalesRepository(IPasswordHasher passwordHasher)
    {
        SeedUsers(passwordHasher);
        SeedGroups();
    }

    public UserAccount? FindUserByLogin(string email)
    {
        lock (_sync)
        {
            return _users.FirstOrDefault(user =>
                user.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        }
    }

    public UserAccount? FindUserById(int? id)
    {
        lock (_sync)
        {
            return _users.FirstOrDefault(user => user.Id == id);
        }
    }

    public IReadOnlyList<UserAccount> GetUsers()
    {
        lock (_sync)
        {
            return _users.ToList();
        }
    }

    public IReadOnlyList<ClientGroup> GetGroups()
    {
        lock (_sync)
        {
            return _groups.ToList();
        }
    }

    public ClientGroup? FindGroup(int? id)
    {
        lock (_sync)
        {
            return _groups.FirstOrDefault(group => group.Id == id);
        }
    }

    public IReadOnlyList<Customer> GetCustomers(ClaimsPrincipal principal, string? customerType, string? prospectStatus, int? groupId)
    {
        lock (_sync)
        {
            var query = ScopeCustomersForUser(principal, _customers);

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
    }

    public Customer? FindCustomer(int id)
    {
        lock (_sync)
        {
            return _customers.FirstOrDefault(customer => customer.Id == id);
        }
    }

    public Customer AddCustomer(Customer customer)
    {
        lock (_sync)
        {
            SyncLegacyCustomerFields(customer);
            customer.Id = _nextCustomerId++;
            customer.CreatedAt = DateTimeOffset.UtcNow;
            _customers.Add(customer);
            return customer;
        }
    }

    public Customer? UpdateCustomer(int id, Customer customer, ClaimsPrincipal principal)
    {
        lock (_sync)
        {
            var existing = ScopeCustomersForUser(principal, _customers).FirstOrDefault(item => item.Id == id);
            if (existing is null)
            {
                return null;
            }

            existing.CompanyName = customer.CompanyName;
            existing.ABN = AbnValidator.Normalize(customer.ABN);
            existing.Address = customer.Address;
            existing.City = customer.City;
            existing.State = customer.State;
            existing.Postcode = customer.Postcode;
            existing.Phone = customer.Phone;
            existing.Email = customer.Email;
            existing.Latitude = customer.Latitude;
            existing.Longitude = customer.Longitude;
            existing.Type = customer.Type;
            existing.TerminationDate = customer.TerminationDate;
            existing.TerminationReason = customer.TerminationReason;
            existing.Competitor = customer.Competitor;
            existing.GeneralNote = customer.GeneralNote;
            existing.GroupId = customer.GroupId;
            existing.AssignedUserId = customer.AssignedUserId;
            SyncLegacyCustomerFields(existing);
            return existing;
        }
    }

    public (Customer Customer, bool WasUpdated) UpsertCustomerByAbn(Customer customer)
    {
        lock (_sync)
        {
            var normalizedAbn = AbnValidator.Normalize(customer.ABN);
            var existing = _customers.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(item.ABN) &&
                AbnValidator.Normalize(item.ABN) == normalizedAbn);

            if (existing is null)
            {
                customer.ABN = normalizedAbn;
                SyncLegacyCustomerFields(customer);
                customer.Id = _nextCustomerId++;
                customer.CreatedAt = DateTimeOffset.UtcNow;
                _customers.Add(customer);
                return (customer, false);
            }

            existing.CompanyName = customer.CompanyName;
            existing.Address = customer.Address;
            existing.City = customer.City;
            existing.State = customer.State;
            existing.Postcode = customer.Postcode;
            existing.Phone = customer.Phone;
            existing.Email = customer.Email;
            existing.Latitude = customer.Latitude;
            existing.Longitude = customer.Longitude;
            existing.Type = customer.Type;
            existing.TerminationDate = customer.TerminationDate;
            existing.TerminationReason = customer.TerminationReason;
            existing.Competitor = customer.Competitor;
            existing.GeneralNote = customer.GeneralNote;
            SyncLegacyCustomerFields(existing);
            existing.GroupId = customer.GroupId;
            existing.AssignedUserId = customer.AssignedUserId;
            return (existing, true);
        }
    }

    public bool UpdateCoordinates(int id, double latitude, double longitude, ClaimsPrincipal principal)
    {
        lock (_sync)
        {
            var customer = ScopeCustomersForUser(principal, _customers).FirstOrDefault(item => item.Id == id);
            if (customer is null)
            {
                return false;
            }

            customer.Latitude = latitude;
            customer.Longitude = longitude;
            return true;
        }
    }

    public SalesNote AddSalesNote(SalesNote note)
    {
        lock (_sync)
        {
            note.Id = _nextNoteId++;
            _notes.Add(note);
            return note;
        }
    }

    public IReadOnlyList<SalesNote> GetSalesNotes(int? customerId)
    {
        lock (_sync)
        {
            var query = _notes.AsEnumerable();
            if (customerId.HasValue)
            {
                query = query.Where(note => note.CustomerId == customerId.Value);
            }

            return query.OrderByDescending(note => note.VisitedDate).ToList();
        }
    }

    public IReadOnlyList<Customer> GetProspectsForUser(ClaimsPrincipal principal)
    {
        lock (_sync)
        {
            return ScopeCustomersForUser(principal, _customers)
                .Where(customer => customer.Type == CustomerLifecycleType.PROSPECT)
                .ToList();
        }
    }

    public IReadOnlyList<PenetrationDashboardRow> GetPenetrationRows()
    {
        lock (_sync)
        {
            return _customers
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
        }
    }

    public IReadOnlyList<HappyVisitGroup> GetHappyVisitGroups()
    {
        lock (_sync)
        {
            return _happyVisitGroups
                .OrderByDescending(group => group.UpdatedAt)
                .ToList();
        }
    }

    public HappyVisitGroup SaveHappyVisitGroup(HappyVisitGroup group)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = group.Id > 0
                ? _happyVisitGroups.FirstOrDefault(item => item.Id == group.Id)
                : null;

            if (existing is null)
            {
                group.Id = _nextHappyVisitGroupId++;
                group.CreatedAt = now;
                group.UpdatedAt = now;
                group.CustomerIds = group.CustomerIds.Distinct().ToList();
                _happyVisitGroups.Add(group);
                return group;
            }

            existing.GroupName = group.GroupName;
            existing.Type = group.Type;
            existing.CustomerIds = group.CustomerIds.Distinct().ToList();
            existing.UpdatedAt = now;
            return existing;
        }
    }

    public IReadOnlyList<DashboardPost> GetDashboardPosts()
    {
        lock (_sync)
        {
            return _dashboardPosts
                .OrderByDescending(post => post.CreatedAt)
                .ToList();
        }
    }

    public DashboardPost? FindDashboardPost(int id)
    {
        lock (_sync)
        {
            return _dashboardPosts.FirstOrDefault(post => post.Id == id);
        }
    }

    public DashboardPost AddDashboardPost(DashboardPost post)
    {
        lock (_sync)
        {
            post.Id = _nextDashboardPostId++;
            post.CreatedAt = DateTimeOffset.UtcNow;
            _dashboardPosts.Add(post);
            return post;
        }
    }

    public DashboardPost? UpdateDashboardPost(int id, DashboardPost post, IReadOnlyList<string>? replacementImagePaths)
    {
        lock (_sync)
        {
            var existing = _dashboardPosts.FirstOrDefault(item => item.Id == id);
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

            return existing;
        }
    }

    public bool DeleteDashboardPost(int id)
    {
        lock (_sync)
        {
            var existing = _dashboardPosts.FirstOrDefault(post => post.Id == id);
            return existing is not null && _dashboardPosts.Remove(existing);
        }
    }

    private static IEnumerable<Customer> ScopeCustomersForUser(ClaimsPrincipal principal, IEnumerable<Customer> source)
    {
        if (principal.IsInRole(UserRole.ADMIN.ToString()))
        {
            return source;
        }

        var userId = principal.GetUserId();
        return source.Where(customer => customer.AssignedUserId is null || customer.AssignedUserId == userId);
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

    private void SeedUsers(IPasswordHasher passwordHasher)
    {
        var admin = passwordHasher.HashPassword("Ecnesoft");
        var sales = passwordHasher.HashPassword("Ecnesoft");

        _users.Add(new UserAccount
        {
            Id = 1,
            Email = "Ecnesoft",
            PasswordHash = admin.Hash,
            Salt = admin.Salt,
            FullName = "ECNESOFT Admin",
            Role = UserRole.ADMIN,
            IsActive = true
        });

        _users.Add(new UserAccount
        {
            Id = 2,
            Email = "sales@ecnesoft.local",
            PasswordHash = sales.Hash,
            Salt = sales.Salt,
            FullName = "Sydney Field Rep",
            Role = UserRole.SALES,
            IsActive = true
        });
    }

    private void SeedGroups()
    {
        _groups.AddRange([
            new ClientGroup { Id = 1, GroupName = "Korean Grocery", HexColor = "#16A34A", Description = "Korean and Asian grocery accounts" },
            new ClientGroup { Id = 2, GroupName = "Restaurant POS", HexColor = "#0EA5E9", Description = "Restaurant POS and table-service prospects" },
            new ClientGroup { Id = 3, GroupName = "Retail POS", HexColor = "#F59E0B", Description = "Retail POS prospects" },
            new ClientGroup { Id = 4, GroupName = "High Touch", HexColor = "#E11D48", Description = "Owner-led, high-value opportunities" }
        ]);
    }

    private void SeedCustomers()
    {
        AddSeed(new Customer
        {
            CompanyName = "ECNE Mart Sydney",
            ABN = "51824753556",
            Address = "680 George Street",
            City = "Sydney",
            State = "NSW",
            Postcode = "2000",
            Phone = "02 9000 1000",
            Email = "owner@sydney.example",
            Latitude = -33.87612,
            Longitude = 151.20698,
            CustomerType = CustomerType.CURRENT,
            ProspectStatus = ProspectStatus.ACTIVE,
            GroupId = 1,
            AssignedUserId = 2
        });

        AddSeed(new Customer
        {
            CompanyName = "Harbour POS Foods",
            ABN = "51824753556",
            Address = "Darling Square",
            City = "Haymarket",
            State = "NSW",
            Postcode = "2000",
            Phone = "02 9000 2000",
            Latitude = -33.87984,
            Longitude = 151.2017,
            CustomerType = CustomerType.PROSPECT,
            ProspectStatus = ProspectStatus.OPEN,
            GroupId = 2,
            AssignedUserId = 2
        });

        AddSeed(new Customer
        {
            CompanyName = "Chatswood Fresh Market",
            ABN = "51824753556",
            Address = "Victoria Avenue",
            City = "Chatswood",
            State = "NSW",
            Postcode = "2067",
            Phone = "02 9410 2000",
            Latitude = -33.79527,
            Longitude = 151.18379,
            CustomerType = CustomerType.CURRENT,
            ProspectStatus = ProspectStatus.ACTIVE,
            GroupId = 1,
            AssignedUserId = 2
        });

        AddSeed(new Customer
        {
            CompanyName = "North Shore Retail Systems",
            ABN = "51824753556",
            Address = "Pacific Highway",
            City = "Chatswood",
            State = "NSW",
            Postcode = "2067",
            Latitude = -33.79793,
            Longitude = 151.1812,
            CustomerType = CustomerType.PROSPECT,
            ProspectStatus = ProspectStatus.OPEN,
            GroupId = 3,
            AssignedUserId = 2
        });

        AddSeed(new Customer
        {
            CompanyName = "Strathfield BBQ House",
            ABN = "51824753556",
            Address = "The Boulevarde",
            City = "Strathfield",
            State = "NSW",
            Postcode = "2135",
            Latitude = -33.8719,
            Longitude = 151.0945,
            CustomerType = CustomerType.PROSPECT,
            ProspectStatus = ProspectStatus.OPEN,
            GroupId = 4,
            AssignedUserId = 2
        });

        AddSeed(new Customer
        {
            CompanyName = "Eastwood Market POS",
            ABN = "51824753556",
            Address = "Rowe Street",
            City = "Eastwood",
            State = "NSW",
            Postcode = "2122",
            Latitude = -33.79009,
            Longitude = 151.08296,
            CustomerType = CustomerType.PROSPECT,
            ProspectStatus = ProspectStatus.TERMINATION,
            GroupId = 1,
            AssignedUserId = null
        });

        AddSeed(new Customer
        {
            CompanyName = "Parramatta Central Foods",
            ABN = "51824753556",
            Address = "Church Street",
            City = "Parramatta",
            State = "NSW",
            Postcode = "2150",
            Latitude = -33.81501,
            Longitude = 151.00111,
            CustomerType = CustomerType.CURRENT,
            ProspectStatus = ProspectStatus.ACTIVE,
            GroupId = 2,
            AssignedUserId = 2
        });

        AddSeed(new Customer
        {
            CompanyName = "Burwood Retail Lane",
            ABN = "51824753556",
            Address = "Burwood Road",
            City = "Burwood",
            State = "NSW",
            Postcode = "2134",
            Latitude = -33.8777,
            Longitude = 151.10385,
            CustomerType = CustomerType.PROSPECT,
            ProspectStatus = ProspectStatus.OPEN,
            GroupId = 3,
            AssignedUserId = 2
        });

        AddSeed(new Customer
        {
            CompanyName = "Melbourne CBD Hospitality",
            ABN = "51824753556",
            Address = "Collins Street",
            City = "Melbourne",
            State = "VIC",
            Postcode = "3000",
            Latitude = -37.8136,
            Longitude = 144.9631,
            CustomerType = CustomerType.PROSPECT,
            ProspectStatus = ProspectStatus.OPEN,
            GroupId = 2,
            AssignedUserId = 2
        });

        AddSeed(new Customer
        {
            CompanyName = "Brisbane River Retail",
            ABN = "51824753556",
            Address = "Queen Street",
            City = "Brisbane",
            State = "QLD",
            Postcode = "4000",
            Latitude = -27.4698,
            Longitude = 153.0251,
            CustomerType = CustomerType.PROSPECT,
            ProspectStatus = ProspectStatus.ACTIVE,
            GroupId = 3,
            AssignedUserId = 2
        });

        AddSeed(new Customer
        {
            CompanyName = "Perth Market Systems",
            ABN = "51824753556",
            Address = "St Georges Terrace",
            City = "Perth",
            State = "WA",
            Postcode = "6000",
            Latitude = -31.9523,
            Longitude = 115.8613,
            CustomerType = CustomerType.PROSPECT,
            ProspectStatus = ProspectStatus.TERMINATION,
            GroupId = 3,
            AssignedUserId = null
        });

        AddSeed(new Customer
        {
            CompanyName = "Adelaide Central POS",
            ABN = "51824753556",
            Address = "King William Street",
            City = "Adelaide",
            State = "SA",
            Postcode = "5000",
            Latitude = -34.9285,
            Longitude = 138.6007,
            CustomerType = CustomerType.CURRENT,
            ProspectStatus = ProspectStatus.ACTIVE,
            GroupId = 2,
            AssignedUserId = 2
        });
    }

    private void SeedNotes()
    {
        _notes.Add(new SalesNote
        {
            Id = _nextNoteId++,
            CustomerId = 2,
            UserId = 2,
            CompetitorProduct = "Legacy POS + manual delivery docket",
            Notes = "Owner wants integrated table ordering and local support. Follow up with restaurant package.",
            VisitedDate = DateTimeOffset.UtcNow.AddDays(-2)
        });
    }

    private void AddSeed(Customer customer)
    {
        customer.Id = _nextCustomerId++;
        _customers.Add(customer);
    }
}
