using Sloffer.Data;
using Sloffer.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSlofferData(builder.Configuration);
builder.Services.AddHostedService<DatabaseCheckService>();
// Job-fetching services are added here as BackgroundServices once they exist.

builder.Build().Run();
