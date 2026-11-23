using Sloffer.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSlofferData(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<SlofferDbContext>("database");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // The frontend's contract: http://localhost:5010/openapi/v1.json
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

// Lets the test project use WebApplicationFactory<Program>.
public partial class Program;
