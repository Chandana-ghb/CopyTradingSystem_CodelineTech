using FyersCopyTrading.Data;
using FyersCopyTrading.Hubs;
using FyersCopyTrading.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5000");

// Add Services to Container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

// Database Configuration: Use SQL Server (connection string defined in appsettings.json)
var sqlConn = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<CopyTradingDbContext>(options =>
    options.UseSqlServer(sqlConn));

// Register Application Services
builder.Services.AddScoped<CopyTradingService>();
builder.Services.AddScoped<HistoricalDataService>();
        builder.Services.AddHostedService<MarketScheduler>();

// Dynamic CORS Policy for Angular Frontend (Supports localhost:4200, 50361, or any port)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Ensure Database Created & Seeded and Historical Data Files Initialized
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CopyTradingDbContext>();
    db.Database.EnsureCreated();

    var historicalService = scope.ServiceProvider.GetRequiredService<HistoricalDataService>();
    await historicalService.InitializeAllHistoricalDataAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngular");
app.UseAuthorization();
app.MapControllers();
app.MapHub<MarketHub>("/hubs/market");

Console.WriteLine("=================================================");
Console.WriteLine("🚀 Copy Trading .NET API Server Started!");
Console.WriteLine("📍 Backend API: http://localhost:5000/api/stocks");
Console.WriteLine("📍 SignalR Hub: http://localhost:5000/hubs/market");
Console.WriteLine("📍 Swagger Docs: http://localhost:5000/swagger");
Console.WriteLine("=================================================");

app.Run();
