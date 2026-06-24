using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DriveScanner.Api.Hubs;
using DriveScanner.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Register scanner dependencies
builder.Services.AddSingleton<ScanResultStore>();
builder.Services.AddSingleton<EvaluatorService>();
builder.Services.AddSingleton<ScannerService>();
builder.Services.AddTransient<ExcelExportService>();

// CORS configuration (allow Angular front-end to talk to backend during development)
builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors("CorsPolicy");
app.UseHttpsRedirection();

var browserPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "browser");
if (Directory.Exists(browserPath))
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(browserPath),
        RequestPath = ""
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(browserPath),
        RequestPath = ""
    });
}
else
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseAuthorization();
app.MapControllers();
app.MapHub<ScanHub>("/hubs/scan");

if (Directory.Exists(browserPath))
{
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(browserPath)
    });
}

// Automatically start Angular dev client in the background if the client folder exists and we're in Development mode
if (app.Environment.IsDevelopment())
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    StartAngularClient(logger);
}

app.Run();

void StartAngularClient(ILogger logger)
{
    var clientDir = Path.Combine(Directory.GetCurrentDirectory(), "client");
    if (!Directory.Exists(clientDir))
    {
        logger.LogWarning("Angular client directory not found at {ClientDir}. Skipping SPA auto-launch.", clientDir);
        return;
    }

    var nodeModules = Path.Combine(clientDir, "node_modules");
    var hasNodeModules = Directory.Exists(nodeModules);

    Task.Run(() =>
    {
        try
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var npmCmd = isWindows ? "cmd.exe" : "npm";
            var npmArgsPrefix = isWindows ? "/c npm " : "";

            if (!hasNodeModules)
            {
                logger.LogInformation("node_modules missing in client folder. Running 'npm install'...");
                var installStartInfo = new ProcessStartInfo
                {
                    FileName = npmCmd,
                    Arguments = $"{npmArgsPrefix}install",
                    WorkingDirectory = clientDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var installProcess = Process.Start(installStartInfo);
                installProcess?.WaitForExit();
                logger.LogInformation("npm install completed.");
            }

            logger.LogInformation("Starting Angular development server (npm start)...");
            var startInfo = new ProcessStartInfo
            {
                FileName = npmCmd,
                Arguments = $"{npmArgsPrefix}run start",
                WorkingDirectory = clientDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) logger.LogInformation("[Angular] {Data}", e.Data);
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) logger.LogError("[Angular Error] {Data}", e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch
                {
                    // Ignore exit killing errors
                }
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start Angular client dev server.");
        }
    });
}
