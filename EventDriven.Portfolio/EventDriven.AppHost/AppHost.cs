var builder = DistributedApplication.CreateBuilder(args);

// 1. Declarar los contenedores de infraestructura 🐳
var sqlServer = builder.AddSqlServer("sqlserver")
                       .AddDatabase("DefaultConnection");

var rabbitmq = builder.AddRabbitMQ("messaging");

// 1. Agregar la API
var apiService = builder.AddProject<Projects.EventDriven_Publisher_Api>("api-service")
    .WithReference(sqlServer)
    .WithReference(rabbitmq);

// 2. Agregar el Worker
builder.AddProject<Projects.EventDriven_Consumer_Worker>("worker-service")
    .WithReference(rabbitmq);

await builder.Build().RunAsync();