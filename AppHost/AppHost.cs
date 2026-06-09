using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .AddDatabase("Omakase");

var api = builder.AddProject<OG_Gateway_Api>("Api");

builder.Build().Run();