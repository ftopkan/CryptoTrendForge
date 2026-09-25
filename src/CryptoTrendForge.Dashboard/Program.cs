using CryptoTrendForge.Core.Infrastructure.Database;
using CryptoTrendForge.Dashboard.Configuration;
using CryptoTrendForge.Dashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    DatabaseConfiguration.ConfigureAppDbContext(options, builder.Configuration));
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
