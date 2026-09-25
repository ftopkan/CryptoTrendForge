using CryptoTrendForge.Core.Infrastructure.Database;
using CryptoTrendForge.Dashboard.Configuration;
using CryptoTrendForge.Dashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    var databaseProvider = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()?.Provider ?? "Postgres";
    if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        options.UseNpgsql(connectionString);
    }
});
builder.Services
    .AddOptions<DashboardAuthOptions>()
    .Bind(builder.Configuration.GetSection(DashboardAuthOptions.SectionName));
builder.Services.AddScoped<DashboardSession>();
builder.Services.AddScoped<DashboardDataService>();

var app = builder.Build();
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
