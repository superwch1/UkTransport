using Backend;
using Backend.Repositories;
using Backend.Services;
using System.Text;

// Register legacy code pages (Windows-1252, etc.) before anything parses XML (for bus timetable)
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddHttpClient();

builder.Services.AddScoped<BusRepository>();
builder.Services.AddSingleton<TransportDataStore>();

//builder.Services.AddHostedService<BusStopImportService>();
//builder.Services.AddHostedService<BusLocationTrackingService>();
builder.Services.AddHostedService<BusTimetableImportService>();

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
