# ASP.NET Core Controller Authorization Example

Minimal API endpoints are used in `Program.cs` for this demo app. If the codebase later moves to MVC controllers, use the same authentication schemes and role names.

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcnesoftFieldSales.Controllers;

[ApiController]
[Route("api/secure/customers")]
[Authorize(Roles = "ADMIN,SALES")]
public sealed class SecureCustomersController : ControllerBase
{
    [HttpGet]
    public IActionResult GetMapCustomers()
    {
        return Ok(new { message = "Only active ADMIN or SALES users can access this endpoint." });
    }
}

[ApiController]
[Route("api/admin/imports")]
[Authorize(Roles = "ADMIN")]
public sealed class AdminImportsController : ControllerBase
{
    [HttpPost("abr")]
    public IActionResult ImportFromAbrBackedFile(IFormFile file)
    {
        return Accepted(new { file.FileName });
    }
}
```

Role policy names already registered in `Program.cs`:

```csharp
options.AddPolicy("SalesOrAdmin", policy => policy.RequireRole("ADMIN", "SALES"));
options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
```
