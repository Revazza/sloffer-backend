using Sloffer.Data;
using Sloffer.Jobs.Fetching;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSlofferData(builder.Configuration);
builder.Services.AddJobFetching(builder.Configuration);
// Job sources are registered here as IJobSource, e.g. builder.Services.AddSingleton<IJobSource, SomeSource>().

builder.Build().Run();
