using InventoryApp.Api.Data;
using InventoryApp.Api.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Authentication, authorization, rate limiting and the clock (TimeProvider) used for session expiry.
builder.Services.AddInventorySecurity(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// One tenant per request scope. Phase 3 fills it from the validated session token.
builder.Services.AddScoped<TenantContext>();

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


app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the test project.
public partial class Program { }