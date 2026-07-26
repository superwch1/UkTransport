using Azure.Core;
using Azure.Identity;
using Backend;
using Backend.Repositories;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text;

// Register legacy code pages (Windows-1252, etc.) before anything parses XML (for bus timetable) 
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddHttpClient();

builder.Services.AddScoped<BusRepository>();
builder.Services.AddSingleton<TransportDataStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TimeService>();

string connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string not found");
NpgsqlDataSourceBuilder dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
if (!builder.Environment.IsDevelopment())
{
    dataSourceBuilder.UsePeriodicPasswordProvider(async (_, ct) =>
    {
        DefaultAzureCredential credential = new DefaultAzureCredential();
        AccessToken token = await credential.GetTokenAsync(new TokenRequestContext(new[] { "https://ossrdbms-aad.database.windows.net/.default" }), ct);
        return token.Token;
    }, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10)); // refresh every 30 minutes (success) and 10 minutes (fail)      
}
NpgsqlDataSource dataSource = dataSourceBuilder.Build();
builder.Services.AddDbContext<UkTransportDbContext>(options => options.UseNpgsql(dataSource, npgsql =>
        npgsql.CommandTimeout(300)));

builder.Services.AddHostedService<BusStopImportService>();
builder.Services.AddHostedService<BusLocationTrackingService>();
builder.Services.AddHostedService<BusScheduleEstimationService>();
// builder.Services.AddHostedService<BusTimetableImportService>();

builder.Services.AddControllers();

if(builder.Environment.IsDevelopment())
{
    // allows connection from other devices under the same network
    builder.WebHost
        .UseUrls("http://0.0.0.0:5000")
        .UseKestrel();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
    });
}

var app = builder.Build();

// Configure the HTTP request pipeline.

// https://stackoverflow.com/questions/75718271/failed-to-determine-the-https-port-for-redirect
// app.UseHttpsRedirection();

if (builder.Environment.IsDevelopment())
{
    app.UseCors();
}


app.MapControllers();


await app.RunAsync();
