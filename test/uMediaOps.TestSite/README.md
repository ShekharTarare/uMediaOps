# uMediaOps Test Site

Umbraco 17 test site for developing and testing the uMediaOps package.

## Quick Start

```bash
# From the repository root
dotnet build uMediaOps.sln
dotnet run --project test/uMediaOps.TestSite/uMediaOpsTestSite.csproj
```

Open `https://localhost:44353/umbraco` — login: `admin@example.com` / `ChangeMe123!`

## What This Site Includes

- Umbraco 17 with The Starter Kit (sample content with media references)
- uMediaOps package loaded via project reference
- App_Plugins automatically copied on build
- SQLite database (auto-created on first run)

## Seeding Test Data

To test with bulk media (duplicates, references, etc.), you can create a seeder service:

1. Create `Services/MediaSeederService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Services;

namespace uMediaOpsTestSite.Services;

public class MediaSeederService
{
    private readonly IMediaService _mediaService;
    private readonly MediaFileManager _mediaFileManager;
    private readonly ILogger<MediaSeederService> _logger;

    public MediaSeederService(
        IMediaService mediaService,
        MediaFileManager mediaFileManager,
        ILogger<MediaSeederService> logger)
    {
        _mediaService = mediaService;
        _mediaFileManager = mediaFileManager;
        _logger = logger;
    }

    public int SeedMedia(string imageDir, int maxFiles = 500)
    {
        var files = Directory.GetFiles(imageDir, "*.jpg").Take(maxFiles).ToArray();
        int uploaded = 0;

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var media = _mediaService.CreateMedia(fileName, Constants.System.Root, Constants.Conventions.MediaTypes.Image);

            var mediaPath = $"/media/{media.Key:N}/{fileName}";
            using var stream = System.IO.File.OpenRead(filePath);
            _mediaFileManager.FileSystem.AddFile(mediaPath, stream);

            var fileInfo = new FileInfo(filePath);
            media.SetValue("umbracoFile", System.Text.Json.JsonSerializer.Serialize(new { src = mediaPath }));
            media.SetValue("umbracoBytes", fileInfo.Length);
            media.SetValue("umbracoExtension", Path.GetExtension(fileName).TrimStart('.'));

            if (_mediaService.Save(media).Success) uploaded++;
        }

        return uploaded;
    }
}
```

2. Register in `Program.cs` (inside development block):

```csharp
builder.Services.AddScoped<uMediaOpsTestSite.Services.MediaSeederService>();

app.MapPost("/api/seed-media", (MediaSeederService seeder) =>
{
    var dir = Path.Combine(app.Environment.ContentRootPath, "temp-images");
    return Results.Ok(new { uploaded = seeder.SeedMedia(dir) });
});
```

3. Generate test images:

```powershell
$dir = "test\uMediaOps.TestSite\temp-images"; mkdir $dir -Force
for ($i = 0; $i -lt 100; $i++) {
    $f = "$dir\photo-$($i.ToString('D4')).jpg"
    Invoke-WebRequest "https://picsum.photos/800/600" -OutFile $f
    1..4 | % { Copy-Item $f "$dir\photo-$($i.ToString('D4'))-copy$_.jpg" }
    Start-Sleep -Milliseconds 200
}
```

4. Trigger: `POST https://localhost:44353/api/seed-media`

5. Remove seeder code when done testing.
