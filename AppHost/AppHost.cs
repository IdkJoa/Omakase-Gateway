using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume()
    .AddDatabase("Omakase");

var redis = builder.AddRedis("redis")
    .WithRedisInsight()
    .WithDataVolume();

var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KEYCLOAK_ADMIN", "admin")
    .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin")
    .WithBindMount("./config/keycloak", "/opt/keycloak/data/import")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http");

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

// Credenciales explícitas (HU-036): sin esto Grafana exige cambiar la contraseña en el primer
// login y bloquea a quien solo quiere ver el panel. Rol anónimo en Viewer: consulta sin editar.
builder.AddContainer("grafana", "grafana/grafana")
    .WithHttpEndpoint(port: 3000, targetPort: 3000)
    .WithBindMount("./config/grafana/provisioning", "/etc/grafana/provisioning")
    .WithBindMount("./config/grafana/dashboards", "/etc/grafana/dashboards")
    .WithEnvironment("GF_SECURITY_ADMIN_USER", "admin")
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", "omakase")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Viewer")
    .WithEnvironment("GF_USERS_ALLOW_SIGN_UP", "false")
    .WaitFor(tempo)
    .WaitFor(loki);

builder.AddProject<OG_Gateway_Api>("gateway-api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(loki.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318")
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(otelCollector);

builder.AddProject<OG_Dashboard_Api>("dashboard-api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(loki.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318")
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(keycloak)
    .WaitFor(otelCollector);

await builder.Build().RunAsync();