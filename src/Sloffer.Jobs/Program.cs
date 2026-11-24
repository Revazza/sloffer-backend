using Sloffer.Data;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Jobicy;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSlofferData(builder.Configuration);
builder.Services.AddJobFetching(builder.Configuration);

// Job sources: one IJobSource per website.
builder.Services.AddJobicySource(builder.Configuration);

builder.Build().Run();
