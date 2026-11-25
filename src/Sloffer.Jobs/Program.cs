using Sloffer.Data;
using Sloffer.Jobs.Fetching;
using Sloffer.Jobs.Sources.Arbeitnow;
using Sloffer.Jobs.Sources.Jobicy;
using Sloffer.Jobs.Sources.RemoteOk;
using Sloffer.Jobs.Sources.Remotive;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSlofferData(builder.Configuration);
builder.Services.AddJobFetching(builder.Configuration);

// Job sources: one IJobSource per website.
builder.Services.AddJobicySource(builder.Configuration);
builder.Services.AddArbeitnowSource(builder.Configuration);
builder.Services.AddRemotiveSource(builder.Configuration);
builder.Services.AddRemoteOkSource(builder.Configuration);

builder.Build().Run();
