var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Unimetry_Api>("api")
    .WithHttpEndpoint(port: 5288, name: "http", isProxied: false)
    .WithExternalHttpEndpoints();

builder.Build().Run();
