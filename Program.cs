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
builder.Services.AddHostedService<FyersLiveMarketService>();
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

// Ensure Database Created & Seeded with positive starting balances
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CopyTradingDbContext>();
    db.Database.EnsureCreated();

    var accounts = db.Accounts.ToList();
    if (!accounts.Any())
    {
        db.Accounts.AddRange(
            new FyersCopyTrading.Models.Account { AccountId = "P001", AccountName = "Chandana (Parent)", AccountType = "PARENT", Balance = 1000000.00m },
            new FyersCopyTrading.Models.Account { AccountId = "C001", AccountName = "Ramu (Child 1)", AccountType = "CHILD", Balance = 500000.00m },
            new FyersCopyTrading.Models.Account { AccountId = "C002", AccountName = "Seenu (Child 2)", AccountType = "CHILD", Balance = 500000.00m },
            new FyersCopyTrading.Models.Account { AccountId = "C003", AccountName = "Priya (Child 3)", AccountType = "CHILD", Balance = 500000.00m },
            new FyersCopyTrading.Models.Account { AccountId = "C004", AccountName = "Arjun (Child 4)", AccountType = "CHILD", Balance = 500000.00m }
        );
        db.SaveChanges();
    }
    else if (accounts.All(a => a.Balance <= 0))
    {
        foreach (var a in accounts)
        {
            a.Balance = a.AccountType == "PARENT" ? 1000000.00m : 500000.00m;
        }
        db.SaveChanges();
    }
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

// Graceful & immediate termination handling on Ctrl+C (SIGINT)
Console.CancelKeyPress += (sender, eventArgs) =>
{
    Console.WriteLine("\n🛑 [SHUTDOWN] Ctrl+C received. Terminating Copy Trading server immediately...");
    eventArgs.Cancel = true;
    Environment.Exit(0);
};

app.Lifetime.ApplicationStopping.Register(() =>
{
    Console.WriteLine("🛑 [SHUTDOWN] Server stopping, freeing resources and exiting...");
    Task.Run(async () =>
    {
        await Task.Delay(500);
        Environment.Exit(0);
    });
});

Console.WriteLine("=================================================");
Console.WriteLine("🚀 Copy Trading .NET API Server Started!");
Console.WriteLine("📍 Backend API: http://localhost:5000/api/stocks");
Console.WriteLine("📍 SignalR Hub: http://localhost:5000/hubs/market");
Console.WriteLine("📍 Swagger Docs: http://localhost:5000/swagger");
Console.WriteLine("📍 Press Ctrl+C to stop the server");
Console.WriteLine("=================================================");

app.Run();
