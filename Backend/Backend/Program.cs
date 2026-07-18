using Backend;
using Backend.Repositories;
using Backend.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddHttpClient();

builder.Services.AddScoped<BusRepository>();
builder.Services.AddSingleton<TransportDataStore>();
builder.Services.AddHostedService<BusLocationTrackingService>();

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
