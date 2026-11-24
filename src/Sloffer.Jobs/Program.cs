using Sloffer.Data;
using Sloffer.Jobs.Fetching;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSlofferData(builder.Configuration);
builder.Services.AddJobFetching(builder.Configuration);
// Job sources (one IJobSource per website) are registered here once they exist.

builder.Build().Run();
