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

    try
    {
        db.Database.ExecuteSqlRaw(@"
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ParentOrders') AND name = 'EntryTime')
                ALTER TABLE ParentOrders ADD EntryTime DATETIME2 NOT NULL DEFAULT GETDATE();
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ParentOrders') AND name = 'ExitTime')
                ALTER TABLE ParentOrders ADD ExitTime DATETIME2 NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ParentOrders') AND name = 'ExitPrice')
                ALTER TABLE ParentOrders ADD ExitPrice DECIMAL(18,2) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ParentOrders') AND name = 'RealizedPnL')
                ALTER TABLE ParentOrders ADD RealizedPnL DECIMAL(18,2) NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ChildOrders') AND name = 'EntryTime')
                ALTER TABLE ChildOrders ADD EntryTime DATETIME2 NOT NULL DEFAULT GETDATE();
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ChildOrders') AND name = 'ExitTime')
                ALTER TABLE ChildOrders ADD ExitTime DATETIME2 NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ChildOrders') AND name = 'ExitPrice')
                ALTER TABLE ChildOrders ADD ExitPrice DECIMAL(18,2) NULL;
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ChildOrders') AND name = 'RealizedPnL')
                ALTER TABLE ChildOrders ADD RealizedPnL DECIMAL(18,2) NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Accounts') AND name = 'IsActive')
                ALTER TABLE Accounts ADD IsActive BIT NOT NULL DEFAULT 1;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AccountMappings') AND name = 'IsActive')
                ALTER TABLE AccountMappings ADD IsActive BIT NOT NULL DEFAULT 1;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AccountMappings') AND name = 'AllocationMode')
                ALTER TABLE AccountMappings ADD AllocationMode NVARCHAR(20) NOT NULL DEFAULT 'RATIO';

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AccountMappings') AND name = 'FixedQuantity')
                ALTER TABLE AccountMappings ADD FixedQuantity INT NOT NULL DEFAULT 1;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AccountMappings') AND name = 'QtyMultiplier')
                ALTER TABLE AccountMappings ADD QtyMultiplier DECIMAL(5,2) NOT NULL DEFAULT 1.0;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AccountMappings') AND name = 'AllowedSymbols')
                ALTER TABLE AccountMappings ADD AllowedSymbols NVARCHAR(500) NOT NULL DEFAULT 'ALL';
        ");
    }
    catch { }

    var accounts = db.Accounts.ToList();
    if (!accounts.Any())
    {
        db.Accounts.AddRange(
            new FyersCopyTrading.Models.Account { AccountId = "P001", AccountName = "Chandana (Parent)", AccountType = "PARENT", Balance = 1000000.00m, IsActive = true },
            new FyersCopyTrading.Models.Account { AccountId = "C001", AccountName = "Ramu (Child 1)", AccountType = "CHILD", Balance = 500000.00m, IsActive = true },
            new FyersCopyTrading.Models.Account { AccountId = "C002", AccountName = "Seenu (Child 2)", AccountType = "CHILD", Balance = 500000.00m, IsActive = true },
            new FyersCopyTrading.Models.Account { AccountId = "C003", AccountName = "Priya (Child 3)", AccountType = "CHILD", Balance = 500000.00m, IsActive = true },
            new FyersCopyTrading.Models.Account { AccountId = "C004", AccountName = "Arjun (Child 4)", AccountType = "CHILD", Balance = 500000.00m, IsActive = true }
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

    // Ensure AccountMappings exist in Database so account status is permanently stored and restored
    var existingMappings = db.AccountMappings.ToList();
    var defaultChildConfigs = new[]
    {
        new { Id = "C001", Multiplier = 0.1m, Mode = "RATIO", FixedQty = 1 },
        new { Id = "C002", Multiplier = 0.5m, Mode = "RATIO", FixedQty = 5 },
        new { Id = "C003", Multiplier = 1.0m, Mode = "RATIO", FixedQty = 10 },
        new { Id = "C004", Multiplier = 1.0m, Mode = "RATIO", FixedQty = 10 }
    };

    foreach (var cfg in defaultChildConfigs)
    {
        var existing = existingMappings.FirstOrDefault(m => m.ChildAccountId == cfg.Id);
        if (existing == null)
        {
            db.AccountMappings.Add(new FyersCopyTrading.Models.AccountMapping
            {
                ParentAccountId = "P001",
                ChildAccountId = cfg.Id,
                QtyMultiplier = cfg.Multiplier,
                IsActive = true,
                AllocationMode = cfg.Mode,
                FixedQuantity = cfg.FixedQty,
                AllowedSymbols = "ALL"
            });
        }
    }
    db.SaveChanges();
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
