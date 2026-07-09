using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// Infraestructura de datos 
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume()
    .AddDatabase("Omakase");

var redis = builder.AddRedis("redis")
    .WithRedisInsight()
    .WithDataVolume();

// Identidad 
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KEYCLOAK_ADMIN", "admin")
    .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin")
    .WithBindMount("./config/keycloak", "/opt/keycloak/data/import")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http");

// Observabilidad 

var tempo = builder.AddContainer("tempo", "grafana/tempo")
    .WithArgs("-config.file=/etc/tempo.yaml")
    .WithBindMount("./config/tempo.yaml", "/etc/tempo.yaml")
    .WithHttpEndpoint(port: 3200, targetPort: 3200, name: "http");

var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib")
    .WithArgs("--config=/etc/otelcol/config.yaml")
    .WithBindMount("./config/otel-collector.yaml", "/etc/otelcol/config.yaml")
    .WithEndpoint(port: 4317, targetPort: 4317, scheme: "http", name: "otlp-grpc")
    .WithEndpoint(port: 4318, targetPort: 4318, scheme: "http", name: "otlp-http")
    .WaitFor(tempo);

var loki = builder.AddContainer("loki", "grafana/loki")
    .WithHttpEndpoint(port: 3100, targetPort: 3100, name: "http");

builder.AddContainer("grafana", "grafana/grafana")
    .WithHttpEndpoint(port: 3000, targetPort: 3000)
    .WithBindMount("./config/grafana/provisioning", "/etc/grafana/provisioning")
    .WithBindMount("./config/grafana/dashboards", "/etc/grafana/dashboards")
    .WaitFor(tempo)
    .WaitFor(loki);

// Servicios de aplicación 
builder.AddProject<OG_Gateway_Api>("gateway-api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(loki.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4318")
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(keycloak)
    .WaitFor(otelCollector);

builder.AddProject<OG_Dashboard_Api>("dashboard-api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(loki.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://otel-collector:4318")
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(keycloak)
    .WaitFor(otelCollector);

await builder.Build().RunAsync();