using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using EcnesoftFieldSales.Auth;
using EcnesoftFieldSales.Domain;
using EcnesoftFieldSales.Infrastructure;
using EcnesoftFieldSales.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

const string CsrfHeaderName = "X-CSRF-TOKEN";
const string RefreshCookieName = "EcnesoftSales.Refresh";

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.AddAntiforgery(options => options.HeaderName = CsrfHeaderName);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddDbContext<SalesDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("SalesDb") ?? "Data Source=App_Data/ecnesoft-sales.db"));
// Swap MockAbrLookupClient to RealAbrLookupClient here when the production ABR API key is configured.
builder.Services.AddHttpClient<IAbrLookupClient, MockAbrLookupClient>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<ISalesRepository, SqliteSalesRepository>();
builder.Services.AddSingleton<IImportParser, ImportParser>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services
    .AddHealthChecks()
    .AddCheck<SqliteHealthCheck>("sqlite")
    .AddCheck<UploadDirectoryHealthCheck>("uploads");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString();
        }

        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests",
            Detail = "Please wait before sending more requests."
        }, cancellationToken);
    };

    options.AddPolicy("LoginLimiter", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRemoteIpKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("RefreshLimiter", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRemoteIpKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
            context.Request.Path.StartsWithSegments("/api/auth/login") ||
            context.Request.Path.StartsWithSegments("/api/auth/refresh"))
        {
            return RateLimitPartition.GetNoLimiter("non-general-api");
        }

        var key = context.User.Identity?.IsAuthenticated == true
            ? $"user:{context.User.GetUserId()}"
            : $"ip:{GetRemoteIpKey(context)}";

        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "SmartAuth";
        options.DefaultChallengeScheme = "SmartAuth";
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddPolicyScheme("SmartAuth", "Cookie or Bearer token", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtAuthenticationHandler.SchemeName
                : CookieAuthenticationDefaults.AuthenticationScheme;
        };
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "EcnesoftSales.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddScheme<AuthenticationSchemeOptions, JwtAuthenticationHandler>(JwtAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SalesOrAdmin", policy => policy.RequireRole("ADMIN", "SALES"));
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
});

var app = builder.Build();

await SalesDbInitializer.InitializeAsync(app.Services, app.Configuration);

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Unexpected server error",
            Detail = app.Environment.IsDevelopment()
                ? context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error.Message
                : "The request could not be completed."
        });
    });
});

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name is "app.js" or "app.css" or "index.html")
        {
            context.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            context.Context.Response.Headers.Pragma = "no-cache";
            context.Context.Response.Headers.Expires = "0";
        }
    }
});
app.UseAuthentication();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (RequiresCsrfValidation(context))
    {
        if (string.IsNullOrWhiteSpace(context.Request.Headers[CsrfHeaderName]))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing CSRF token",
                Detail = $"Send the {CsrfHeaderName} header with cookie-based write requests."
            });
            return;
        }

        try
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid CSRF token",
                Detail = "Refresh the page and try again."
            });
            return;
        }
    }

    await next();
});
app.UseAntiforgery();
app.UseAuthorization();

app.MapPost("/api/auth/login", async (
        LoginRequest request,
        ISalesRepository repository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IAntiforgery antiforgery,
        HttpContext httpContext) =>
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["Login ID and password are required."]
            });
        }

        var user = repository.FindUserByLogin(request.Email.Trim());
        if (user is null ||
            !user.IsActive ||
            !passwordHasher.VerifyPassword(request.Password, user.PasswordHash, user.Salt))
        {
            return Results.Unauthorized();
        }

        var (token, expiresAt) = jwtTokenService.CreateToken(user);
        var principal = jwtTokenService.CreatePrincipal(user);
        httpContext.User = principal;
        var refreshToken = CreateRefreshToken();
        var refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        repository.StoreRefreshToken(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashRefreshToken(refreshToken),
            ExpiresAt = refreshTokenExpiresAt
        });
        SetRefreshTokenCookie(httpContext, refreshToken, refreshTokenExpiresAt);

        if (request.UseCookie)
        {
            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = expiresAt,
                    AllowRefresh = true
                });
        }

        return Results.Ok(new LoginResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.Role.ToString(),
            token,
            expiresAt,
            CreateCsrfToken(httpContext, antiforgery)));
    })
    .AllowAnonymous()
    .RequireRateLimiting("LoginLimiter");

app.MapGet("/api/auth/me", (ClaimsPrincipal user) =>
        Results.Ok(user.ToProfile()))
    .RequireAuthorization("SalesOrAdmin");

app.MapPost("/api/auth/refresh", async (
        HttpContext httpContext,
        ISalesRepository repository,
        IJwtTokenService jwtTokenService,
        IAntiforgery antiforgery) =>
    {
        var refreshToken = httpContext.Request.Cookies[RefreshCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Results.Unauthorized();
        }

        var tokenHash = HashRefreshToken(refreshToken);
        var storedToken = repository.FindActiveRefreshToken(tokenHash);
        if (storedToken is null)
        {
            ClearRefreshTokenCookie(httpContext);
            return Results.Unauthorized();
        }

        var user = repository.FindUserById(storedToken.UserId);
        if (user is null || !user.IsActive)
        {
            repository.RevokeRefreshToken(tokenHash);
            ClearRefreshTokenCookie(httpContext);
            return Results.Unauthorized();
        }

        repository.RevokeRefreshToken(tokenHash);
        var newRefreshToken = CreateRefreshToken();
        var newRefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        repository.StoreRefreshToken(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashRefreshToken(newRefreshToken),
            ExpiresAt = newRefreshTokenExpiresAt
        });
        SetRefreshTokenCookie(httpContext, newRefreshToken, newRefreshTokenExpiresAt);

        var (accessToken, expiresAt) = jwtTokenService.CreateToken(user);
        var principal = jwtTokenService.CreatePrincipal(user);
        httpContext.User = principal;
        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = expiresAt,
                AllowRefresh = true
            });

        return Results.Ok(new LoginResponse(
            user.Id,
            user.Email,
            user.FullName,
            user.Role.ToString(),
            accessToken,
            expiresAt,
            CreateCsrfToken(httpContext, antiforgery)));
    })
    .AllowAnonymous()
    .RequireRateLimiting("RefreshLimiter");

app.MapGet("/health", async (HealthCheckService healthCheckService, CancellationToken cancellationToken) =>
    {
        var report = await healthCheckService.CheckHealthAsync(cancellationToken);
        var checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => new
            {
                status = entry.Value.Status == HealthStatus.Healthy ? "healthy" : "degraded",
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2)
            });
        var isHealthy = report.Status == HealthStatus.Healthy;

        return Results.Json(
            new
            {
                status = isHealthy ? "healthy" : "degraded",
                checks
            },
            statusCode: isHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    })
    .AllowAnonymous();

app.MapPost("/api/auth/logout", async (HttpContext httpContext, ISalesRepository repository) =>
    {
        var refreshToken = httpContext.Request.Cookies[RefreshCookieName];
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            repository.RevokeRefreshToken(HashRefreshToken(refreshToken));
        }

        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        ClearRefreshTokenCookie(httpContext);
        return Results.NoContent();
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapGet("/api/groups", (ISalesRepository repository) =>
        Results.Ok(repository.GetGroups().Select(GroupDto.From)))
    .RequireAuthorization("SalesOrAdmin");

app.MapGet("/api/happy-visits", (ISalesRepository repository) =>
        Results.Ok(repository.GetHappyVisitGroups().Select(HappyVisitGroupDto.From)))
    .RequireAuthorization("SalesOrAdmin");

app.MapPost("/api/happy-visits", (
        SaveHappyVisitGroupRequest request,
        ClaimsPrincipal user,
        ISalesRepository repository) =>
    SaveHappyVisitGroup(0, request, user, repository))
    .RequireAuthorization("SalesOrAdmin");

app.MapPut("/api/happy-visits/{id:int}", (
        int id,
        SaveHappyVisitGroupRequest request,
        ClaimsPrincipal user,
        ISalesRepository repository) =>
    SaveHappyVisitGroup(id, request, user, repository))
    .RequireAuthorization("SalesOrAdmin");

app.MapGet("/api/config/maps", (IConfiguration configuration) =>
    {
        var apiKey = configuration["GoogleMaps:ApiKey"] ?? "";
        var mapId = configuration["GoogleMaps:MapId"] ?? "DEMO_MAP_ID";

        return Results.Ok(new
        {
            provider = string.IsNullOrWhiteSpace(apiKey) ? "internal" : "google",
            apiKey,
            mapId = string.IsNullOrWhiteSpace(mapId) ? "DEMO_MAP_ID" : mapId,
            region = "AU",
            language = "en"
        });
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapGet("/api/users/sales", (ISalesRepository repository) =>
        Results.Ok(repository.GetUsers()
            .Where(user => user.Role == UserRole.SALES && user.IsActive)
            .Select(user => new UserProfileResponse(user.Id, user.Email, user.FullName, user.Role.ToString()))))
    .RequireAuthorization("AdminOnly");

app.MapGet("/api/users", (ISalesRepository repository) =>
        Results.Ok(repository.GetUsers().Select(UserAccountDto.From)))
    .RequireAuthorization("AdminOnly");

app.MapPost("/api/users", (
        CreateUserRequest request,
        ISalesRepository repository,
        IPasswordHasher passwordHasher) =>
    {
        var validation = ValidateCreateUserRequest(request, repository);
        if (validation.Count > 0)
        {
            return Results.ValidationProblem(validation);
        }

        var password = passwordHasher.HashPassword(request.Password);
        var user = repository.AddUser(new UserAccount
        {
            Email = request.Email.Trim(),
            FullName = request.FullName.Trim(),
            PasswordHash = password.Hash,
            Salt = password.Salt,
            Role = Enum.Parse<UserRole>(request.Role, ignoreCase: true),
            IsActive = request.IsActive
        });

        return Results.Created($"/api/users/{user.Id}", UserAccountDto.From(user));
    })
    .RequireAuthorization("AdminOnly");

app.MapPut("/api/users/{id:int}", (
        int id,
        UpdateUserRequest request,
        ISalesRepository repository,
        IPasswordHasher passwordHasher) =>
    {
        var existing = repository.FindUserById(id);
        if (existing is null)
        {
            return Results.NotFound();
        }

        var validation = ValidateUpdateUserRequest(id, request, repository);
        if (validation.Count > 0)
        {
            return Results.ValidationProblem(validation);
        }

        string? passwordHash = null;
        string? salt = null;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var password = passwordHasher.HashPassword(request.Password);
            passwordHash = password.Hash;
            salt = password.Salt;
        }

        var updated = repository.UpdateUser(id, new UserAccount
        {
            Email = string.IsNullOrWhiteSpace(request.Email) ? existing.Email : request.Email.Trim(),
            FullName = string.IsNullOrWhiteSpace(request.FullName) ? existing.FullName : request.FullName.Trim(),
            Role = string.IsNullOrWhiteSpace(request.Role)
                ? existing.Role
                : Enum.Parse<UserRole>(request.Role, ignoreCase: true),
            IsActive = request.IsActive ?? existing.IsActive
        }, passwordHash, salt);

        return updated is null ? Results.NotFound() : Results.Ok(UserAccountDto.From(updated));
    })
    .RequireAuthorization("AdminOnly");

app.MapDelete("/api/users/{id:int}", (int id, ISalesRepository repository) =>
        repository.DeactivateUser(id) ? Results.NoContent() : Results.NotFound())
    .RequireAuthorization("AdminOnly");

app.MapGet("/api/customers", (
        ClaimsPrincipal user,
        ISalesRepository repository,
        string? type,
        string? customerType,
        string? prospectStatus,
        int? groupId) =>
    {
        var customers = repository.GetCustomers(user, customerType, prospectStatus, groupId);
        if (Enum.TryParse<CustomerLifecycleType>(type, ignoreCase: true, out var parsedType))
        {
            customers = customers.Where(customer => customer.Type == parsedType).ToList();
        }

        return Results.Ok(customers.Select(customer => CustomerDto.From(
            customer,
            repository.FindGroup(customer.GroupId),
            repository.FindUserById(customer.AssignedUserId))));
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapPost("/api/customers", (
        CreateCustomerRequest request,
        ClaimsPrincipal user,
        ISalesRepository repository) =>
    {
        var validation = ValidateCustomerRequest(request);
        if (validation.Count > 0)
        {
            return Results.ValidationProblem(validation);
        }

        var customer = repository.AddCustomer(BuildCustomerFromRequest(request, user));

        return Results.Created($"/api/customers/{customer.Id}", CustomerDto.From(
            customer,
            repository.FindGroup(customer.GroupId),
            repository.FindUserById(customer.AssignedUserId)));
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapPut("/api/customers/{id:int}", (
        int id,
        CreateCustomerRequest request,
        ClaimsPrincipal user,
        ISalesRepository repository) =>
    {
        var validation = ValidateCustomerRequest(request);
        if (validation.Count > 0)
        {
            return Results.ValidationProblem(validation);
        }

        var customer = repository.UpdateCustomer(id, BuildCustomerFromRequest(request, user), user);

        return customer is null
            ? Results.NotFound()
            : Results.Ok(CustomerDto.From(
                customer,
                repository.FindGroup(customer.GroupId),
                repository.FindUserById(customer.AssignedUserId)));
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapDelete("/api/customers", (
        ClaimsPrincipal user,
        ISalesRepository repository) =>
    {
        var deleted = repository.ClearCustomers(user);
        return Results.Ok(new { deleted });
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapPost("/api/customers/import", (
        CustomerXmlImportRequest request,
        ClaimsPrincipal user,
        ISalesRepository repository) =>
    {
        IReadOnlyList<CreateCustomerRequest> rows = request.Customers ?? [];
        var errors = new List<CustomerXmlImportRowResult>();
        var customers = new List<Customer>();

        for (var index = 0; index < rows.Count; index += 1)
        {
            var rowNumber = index + 2;
            var row = rows[index];
            var validation = ValidateCustomerRequest(row);
            if (validation.Count > 0)
            {
                errors.Add(new CustomerXmlImportRowResult(
                    rowNumber,
                    false,
                    null,
                    row.CompanyName,
                    string.Join(" ", validation.Values.SelectMany(value => value))));
                continue;
            }

            customers.Add(BuildCustomerFromRequest(row, user));
        }

        if (errors.Count > 0)
        {
            return Results.BadRequest(new CustomerXmlImportResponse(
                rows.Count,
                0,
                0,
                false,
                errors));
        }

        if (customers.Count == 0)
        {
            return Results.BadRequest(new CustomerXmlImportResponse(
                rows.Count,
                0,
                0,
                false,
                [new CustomerXmlImportRowResult(2, false, null, "", "No valid customer rows were found.")]));
        }

        var result = repository.ReplaceCustomers(user, customers);
        return Results.Ok(result);
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapPatch("/api/customers/{id:int}/coordinates", (
        int id,
        CoordinateUpdateRequest request,
        ClaimsPrincipal user,
        ISalesRepository repository) =>
    {
        if (!IsValidCoordinate(request.Latitude, request.Longitude))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["coordinates"] = ["Latitude and longitude are outside valid ranges."]
            });
        }

        return repository.UpdateCoordinates(id, request.Latitude, request.Longitude, user)
            ? Results.NoContent()
            : Results.NotFound();
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapGet("/api/sales-notes", (int? customerId, ISalesRepository repository) =>
        Results.Ok(repository.GetSalesNotes(customerId).Select(SalesNoteDto.From)))
    .RequireAuthorization("SalesOrAdmin");

app.MapPost("/api/customers/{id:int}/notes", async (
        int id,
        HttpRequest request,
        ClaimsPrincipal user,
        ISalesRepository repository,
        IFileStorageService fileStorage,
        CancellationToken cancellationToken) =>
    {
        var visibleCustomer = repository.GetCustomers(user, null, null, null).Any(customer => customer.Id == id);
        if (!visibleCustomer)
        {
            return Results.NotFound();
        }

        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "multipart/form-data is required." });
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var imagePath = await fileStorage.SaveSalesNoteImageAsync(form.Files.GetFile("image"), cancellationToken);
        var visitedDate = DateTimeOffset.TryParse(form["visitedDate"], out var parsedVisitedDate)
            ? parsedVisitedDate
            : DateTimeOffset.UtcNow;

        var note = repository.AddSalesNote(new SalesNote
        {
            CustomerId = id,
            UserId = user.GetUserId(),
            CompetitorProduct = EmptyToNull(form["competitorProduct"]),
            Notes = EmptyToNull(form["notes"]) ?? "",
            ImagePath = imagePath,
            VisitedDate = visitedDate
        });

        return Results.Created($"/api/sales-notes?customerId={id}", SalesNoteDto.From(note));
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapGet("/api/recommendations", (
        ClaimsPrincipal user,
        ISalesRepository repository,
        double latitude,
        double longitude,
        double radiusKm) =>
    {
        if (!IsValidCoordinate(latitude, longitude) || radiusKm <= 0 || radiusKm > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["Use valid coordinates and a radius between 0 and 100 km."]
            });
        }

        var prospects = repository.GetProspectsForUser(user)
            .Select(customer => new
            {
                Customer = customer,
                DistanceKm = HaversineKm(latitude, longitude, customer.Latitude, customer.Longitude)
            })
            .Where(item => item.DistanceKm <= radiusKm)
            .OrderBy(item => item.DistanceKm)
            .Select(item => new RecommendationDto(
                item.Customer.Id,
                item.Customer.CompanyName,
                item.Customer.Address,
                item.Customer.City,
                item.Customer.Postcode,
                item.Customer.ProspectStatus?.ToString(),
                item.Customer.Latitude,
                item.Customer.Longitude,
                Math.Round(item.DistanceKm, 2)))
            .ToList();

        return Results.Ok(prospects);
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapPost("/api/admin/import", async (
        HttpRequest request,
        ISalesRepository repository,
        IImportParser parser,
        IAbrLookupClient abrLookupClient,
        CancellationToken cancellationToken) =>
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "multipart/form-data with a file field named 'file' is required." });
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { message = "Import file is required." });
        }

        var rows = await parser.ParseAsync(file.OpenReadStream(), file.FileName, cancellationToken);
        var errors = new List<BulkImportError>();
        var inserted = 0;
        var updated = 0;

        foreach (var row in rows)
        {
            var abn = AbnValidator.Normalize(row.ABN);
            var lookup = await abrLookupClient.LookupAsync(abn, cancellationToken);
            if (!lookup.IsValid)
            {
                errors.Add(new BulkImportError(row.RowNumber, abn, lookup.Message));
                continue;
            }

            var customerType = ParseCustomerTypeOrDefault(row.CustomerType, CustomerType.PROSPECT);
            var type = ParseLifecycleType(row.ProspectStatus)
                ?? ParseLifecycleType(row.CustomerType)
                ?? (customerType == CustomerType.CURRENT ? CustomerLifecycleType.ACTIVE : CustomerLifecycleType.PROSPECT);

            var (customer, wasUpdated) = repository.UpsertCustomerByAbn(new Customer
            {
                ABN = abn,
                CompanyName = FirstNonEmpty(row.CompanyName, lookup.LegalName, lookup.TradingName, $"ABN {abn}"),
                Address = FirstNonEmpty(row.Address, lookup.Address, "Address pending"),
                City = FirstNonEmpty(row.City, "Sydney"),
                State = FirstNonEmpty(row.State, lookup.State, "NSW").ToUpperInvariant(),
                Postcode = FirstNonEmpty(row.Postcode, lookup.Postcode, "2000"),
                Phone = row.Phone,
                Email = row.Email,
                Latitude = row.Latitude ?? -33.8688,
                Longitude = row.Longitude ?? 151.2093,
                Type = type,
                GroupId = row.GroupId,
                AssignedUserId = row.AssignedUserId
            });

            if (wasUpdated)
            {
                updated++;
            }
            else
            {
                inserted++;
            }
        }

        return Results.Ok(new BulkImportResponse(
            rows.Count,
            inserted,
            updated,
            errors.Count,
            errors));
    })
    .RequireAuthorization("AdminOnly");

app.MapGet("/api/admin/dashboard/penetration", (ISalesRepository repository) =>
        Results.Ok(repository.GetPenetrationRows()))
    .RequireAuthorization("AdminOnly");

app.MapGet("/api/dashboard/posts", (ISalesRepository repository) =>
        Results.Ok(repository.GetDashboardPosts().Select(DashboardPostDto.From)))
    .RequireAuthorization("SalesOrAdmin");

app.MapGet("/api/dashboard/posts/{id:int}", (int id, ISalesRepository repository) =>
    {
        var post = repository.FindDashboardPost(id);
        return post is null ? Results.NotFound() : Results.Ok(DashboardPostDto.From(post));
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapPost("/api/dashboard/posts", async (
        HttpRequest request,
        ISalesRepository repository,
        IFileStorageService fileStorage,
        CancellationToken cancellationToken) =>
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "multipart/form-data is required." });
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var postName = EmptyToNull(form["postName"]);
        var editor = EmptyToNull(form["editor"]);
        var description = EmptyToNull(form["description"]);
        var files = form.Files.GetFiles("images");

        var errors = ValidateDashboardPost(postName, editor, description, files.Count);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var imagePaths = new List<string>();
        foreach (var file in files)
        {
            var saved = await fileStorage.SaveSalesNoteImageAsync(file, cancellationToken);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                imagePaths.Add(saved);
            }
        }

        var post = repository.AddDashboardPost(new DashboardPost
        {
            PostName = postName!,
            Editor = editor!,
            Description = description!,
            ImagePaths = imagePaths
        });

        return Results.Created($"/api/dashboard/posts/{post.Id}", DashboardPostDto.From(post));
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapPut("/api/dashboard/posts/{id:int}", async (
        int id,
        HttpRequest request,
        ISalesRepository repository,
        IFileStorageService fileStorage,
        CancellationToken cancellationToken) =>
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { message = "multipart/form-data is required." });
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var postName = EmptyToNull(form["postName"]);
        var editor = EmptyToNull(form["editor"]);
        var description = EmptyToNull(form["description"]);
        var files = form.Files.GetFiles("images");

        var errors = ValidateDashboardPost(postName, editor, description, files.Count);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        List<string>? replacementImagePaths = null;
        if (files.Count > 0)
        {
            replacementImagePaths = [];
            foreach (var file in files)
            {
                var saved = await fileStorage.SaveSalesNoteImageAsync(file, cancellationToken);
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    replacementImagePaths.Add(saved);
                }
            }
        }

        var post = repository.UpdateDashboardPost(id, new DashboardPost
        {
            PostName = postName!,
            Editor = editor!,
            Description = description!
        }, replacementImagePaths);

        return post is null ? Results.NotFound() : Results.Ok(DashboardPostDto.From(post));
    })
    .RequireAuthorization("SalesOrAdmin");

app.MapDelete("/api/dashboard/posts/{id:int}", (int id, ISalesRepository repository) =>
        repository.DeleteDashboardPost(id) ? Results.NoContent() : Results.NotFound())
    .RequireAuthorization("SalesOrAdmin");

app.MapFallbackToFile("index.html");

app.Run();

static IResult SaveHappyVisitGroup(
    int id,
    SaveHappyVisitGroupRequest request,
    ClaimsPrincipal user,
    ISalesRepository repository)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.GroupName))
    {
        errors["groupName"] = ["Group Name is required."];
    }

    if (!Enum.TryParse<CustomerLifecycleType>(request.Type, ignoreCase: true, out var type))
    {
        errors["type"] = ["Type must be Active, Termination, Closed, Prospect, or Ownership."];
    }

    var visibleIds = repository
        .GetCustomers(user, null, null, null)
        .Select(customer => customer.Id)
        .ToHashSet();
    var selectedIds = request.CustomerIds?
        .Where(visibleIds.Contains)
        .Distinct()
        .ToList() ?? [];

    if (selectedIds.Count == 0)
    {
        errors["customerIds"] = ["Add at least one customer to the group."];
    }

    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var saved = repository.SaveHappyVisitGroup(new HappyVisitGroup
    {
        Id = id,
        GroupName = request.GroupName.Trim(),
        Type = type,
        CustomerIds = selectedIds
    });

    return Results.Ok(HappyVisitGroupDto.From(saved));
}

static Customer BuildCustomerFromRequest(CreateCustomerRequest request, ClaimsPrincipal user)
{
    var type = Enum.Parse<CustomerLifecycleType>(request.Type!, ignoreCase: true);
    return new Customer
    {
        CompanyName = request.CompanyName.Trim(),
        ABN = AbnValidator.Normalize(request.ABN),
        Address = request.Address.Trim(),
        City = request.City.Trim(),
        State = request.State.Trim().ToUpperInvariant(),
        Postcode = request.Postcode.Trim(),
        Phone = request.Phone?.Trim(),
        Email = request.Email?.Trim(),
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        Type = type,
        TerminationDate = type == CustomerLifecycleType.TERMINATION ? request.TerminationDate : null,
        TerminationReason = type == CustomerLifecycleType.TERMINATION ? request.TerminationReason?.Trim() : null,
        Competitor = ParseCompetitor(request.Competitor),
        GeneralNote = EmptyToNull(request.GeneralNote),
        GroupId = request.GroupId,
        AssignedUserId = user.IsInRole("ADMIN") ? request.AssignedUserId : user.GetUserId()
    };
}

static Dictionary<string, string[]> ValidateCustomerRequest(CreateCustomerRequest request)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.CompanyName))
    {
        errors["companyName"] = ["Company name is required."];
    }

    if (string.IsNullOrWhiteSpace(request.Address) ||
        string.IsNullOrWhiteSpace(request.City) ||
        string.IsNullOrWhiteSpace(request.State) ||
        string.IsNullOrWhiteSpace(request.Postcode))
    {
        errors["address"] = ["Address, city, state, and postcode are required."];
    }

    if (string.IsNullOrWhiteSpace(request.Type) ||
        !Enum.TryParse<CustomerLifecycleType>(request.Type, ignoreCase: true, out var type))
    {
        errors["type"] = ["Type must be Active, Termination, Closed, Prospect, or Ownership."];
    }
    else if (type == CustomerLifecycleType.TERMINATION)
    {
        if (!request.TerminationDate.HasValue)
        {
            errors["terminationDate"] = ["Termination Date is required when Type is Termination."];
        }

        if (string.IsNullOrWhiteSpace(request.TerminationReason))
        {
            errors["terminationReason"] = ["Termination Reason is required when Type is Termination."];
        }
        else if (request.TerminationReason.Trim().Length > 100)
        {
            errors["terminationReason"] = ["Termination Reason must be 100 characters or fewer."];
        }
    }

    if (!string.IsNullOrWhiteSpace(request.Competitor) &&
        !Enum.TryParse<CompetitorType>(request.Competitor, ignoreCase: true, out _))
    {
        errors["competitor"] = ["Competitor must be Kpos, OrderNow, Qonus, Square, or ETC."];
    }

    if (!string.IsNullOrWhiteSpace(request.GeneralNote) && request.GeneralNote.Trim().Length > 200)
    {
        errors["generalNote"] = ["Note must be 200 characters or fewer."];
    }

    if (!IsValidCoordinate(request.Latitude, request.Longitude))
    {
        errors["coordinates"] = ["Latitude and longitude are outside valid ranges."];
    }

    return errors;
}

static Dictionary<string, string[]> ValidateDashboardPost(
    string? postName,
    string? editor,
    string? description,
    int imageCount)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(postName))
    {
        errors["postName"] = ["Post Name is required."];
    }

    if (string.IsNullOrWhiteSpace(editor))
    {
        errors["editor"] = ["Editor is required."];
    }

    if (string.IsNullOrWhiteSpace(description))
    {
        errors["description"] = ["Description is required."];
    }

    if (imageCount > 3)
    {
        errors["images"] = ["Upload up to 3 images."];
    }

    return errors;
}

static Dictionary<string, string[]> ValidateCreateUserRequest(CreateUserRequest request, ISalesRepository repository)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.Email))
    {
        errors["email"] = ["Username is required."];
    }
    else if (repository.GetUsers().Any(user => user.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
    {
        errors["email"] = ["Username already exists."];
    }

    if (string.IsNullOrWhiteSpace(request.FullName))
    {
        errors["fullName"] = ["Full name is required."];
    }

    if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
    {
        errors["password"] = ["Password must be at least 8 characters."];
    }

    if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out _))
    {
        errors["role"] = ["Role must be ADMIN or SALES."];
    }

    return errors;
}

static Dictionary<string, string[]> ValidateUpdateUserRequest(int userId, UpdateUserRequest request, ISalesRepository repository)
{
    var errors = new Dictionary<string, string[]>();
    if (!string.IsNullOrWhiteSpace(request.Email) &&
        repository.GetUsers().Any(user =>
            user.Id != userId &&
            user.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
    {
        errors["email"] = ["Username already exists."];
    }

    if (!string.IsNullOrWhiteSpace(request.Password) && request.Password.Length < 8)
    {
        errors["password"] = ["Password must be at least 8 characters."];
    }

    if (!string.IsNullOrWhiteSpace(request.Role) &&
        !Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out _))
    {
        errors["role"] = ["Role must be ADMIN or SALES."];
    }

    return errors;
}

static bool RequiresCsrfValidation(HttpContext context)
{
    if (!context.Request.Path.StartsWithSegments("/api") ||
        HttpMethods.IsGet(context.Request.Method) ||
        HttpMethods.IsHead(context.Request.Method) ||
        HttpMethods.IsOptions(context.Request.Method) ||
        context.Request.Path.StartsWithSegments("/api/auth/login") ||
        context.Request.Path.StartsWithSegments("/api/auth/refresh"))
    {
        return false;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    return !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
}

static string GetRemoteIpKey(HttpContext context) =>
    context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static string CreateCsrfToken(HttpContext httpContext, IAntiforgery antiforgery) =>
    antiforgery.GetAndStoreTokens(httpContext).RequestToken ?? "";

static string CreateRefreshToken() =>
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

static string HashRefreshToken(string refreshToken) =>
    Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

static void SetRefreshTokenCookie(HttpContext httpContext, string refreshToken, DateTimeOffset expiresAt)
{
    httpContext.Response.Cookies.Append("EcnesoftSales.Refresh", refreshToken, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = httpContext.Request.IsHttps,
        Expires = expiresAt
    });
}

static void ClearRefreshTokenCookie(HttpContext httpContext)
{
    httpContext.Response.Cookies.Delete("EcnesoftSales.Refresh", new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = httpContext.Request.IsHttps
    });
}

static CustomerType ParseCustomerTypeOrDefault(string? value, CustomerType fallback) =>
    Enum.TryParse<CustomerType>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

static CustomerLifecycleType? ParseLifecycleType(string? value) =>
    Enum.TryParse<CustomerLifecycleType>(value, ignoreCase: true, out var parsed) ? parsed : null;

static CompetitorType? ParseCompetitor(string? value) =>
    Enum.TryParse<CompetitorType>(value, ignoreCase: true, out var parsed) ? parsed : null;

static bool IsValidCoordinate(double latitude, double longitude) =>
    latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

static string? EmptyToNull(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();

static string FirstNonEmpty(params string?[] values) =>
    values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

static double HaversineKm(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude)
{
    const double earthRadiusKm = 6371.0088;
    var dLat = DegreesToRadians(toLatitude - fromLatitude);
    var dLon = DegreesToRadians(toLongitude - fromLongitude);
    var lat1 = DegreesToRadians(fromLatitude);
    var lat2 = DegreesToRadians(toLatitude);

    var a = Math.Pow(Math.Sin(dLat / 2), 2) +
            Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
    var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    return earthRadiusKm * c;
}

static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
