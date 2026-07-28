var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Monitoring_Blazor>("monitoring-blazor");

builder.Build().Run();
