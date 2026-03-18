using InventoryApp.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Swagger UI, so that we can conveniently test the API in the browser later on
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register DB context. It automatically pulls the connection string from appsettings.json
builder.Services.AddDbContext<InventoryDbContext>(options =>
options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));


var app = builder.Build();


// IMPORTANT: Executes pending EF Core migrations directly when the app starts
// Saves us from having to manually execute SQL Scripts in Azure when we change the tables
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    db.Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();

// Standard middleware (HTTPS enforcement, auth pipeline, etc.)
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();