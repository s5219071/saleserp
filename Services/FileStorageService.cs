using Microsoft.Extensions.Options;

namespace EcnesoftFieldSales.Services;

public sealed class SecurityOptions
{
    public long MaxUploadBytes { get; init; } = 5 * 1024 * 1024;
}

public interface IFileStorageService
{
    Task<string?> SaveSalesNoteImageAsync(IFormFile? file, CancellationToken cancellationToken);
}

public sealed class LocalFileStorageService(IWebHostEnvironment environment, IOptions<SecurityOptions> securityOptions)
    : IFileStorageService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    public async Task<string?> SaveSalesNoteImageAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        if (file.Length > securityOptions.Value.MaxUploadBytes)
        {
            throw new InvalidOperationException("Image file is larger than the configured upload limit.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Only JPG, PNG, and WEBP images are allowed.");
        }

        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var uploadRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads", "sales-notes"));
        var dateSegment = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var targetDirectory = Path.GetFullPath(Path.Combine(uploadRoot, dateSegment));
        Directory.CreateDirectory(targetDirectory);

        var safeFileName = $"{Guid.NewGuid():N}{extension}";
        var destination = Path.GetFullPath(Path.Combine(targetDirectory, safeFileName));
        if (!destination.StartsWith(uploadRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved upload path escaped the configured upload directory.");
        }

        await using var destinationStream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await file.CopyToAsync(destinationStream, cancellationToken);

        return $"/uploads/sales-notes/{dateSegment}/{safeFileName}";
    }
}
