var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedisContainer("redis");
var rabbitMq = builder.AddRabbitMQContainer("EventBus");
var postgres = builder.AddPostgresContainer("postgres")
    .WithAnnotation(new ContainerImageAnnotation
    {
        Image = "ankane/pgvector",
        Tag = "latest"
    });

var catalogDb = postgres.AddDatabase("CatalogDB");
var identityDb = postgres.AddDatabase("IdentityDB");
var orderDb = postgres.AddDatabase("OrderingDB");
var webhooksDb = postgres.AddDatabase("WebHooksDB");

var NEW_RELIC_REGION = Environment.GetEnvironmentVariable("NEW_RELIC_REGION");
string OTEL_EXPORTER_OTLP_ENDPOINT = "https://otlp.nr-data.net";
if (NEW_RELIC_REGION != null &&
    NEW_RELIC_REGION != "" &&
    NEW_RELIC_REGION == "EU")
{
    OTEL_EXPORTER_OTLP_ENDPOINT = "https://otlp.eu01.nr-data.net";
}
var NEW_RELIC_LICENSE_KEY = Environment.GetEnvironmentVariable("NEW_RELIC_LICENSE_KEY");
string OTEL_EXPORTER_OTLP_HEADERS = "api-key=" + NEW_RELIC_LICENSE_KEY;

// Services
var identityApi = builder.AddProject<Projects.Identity_API>("identity-api")
    .WithReference(identityDb)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OTEL_EXPORTER_OTLP_ENDPOINT)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", OTEL_EXPORTER_OTLP_HEADERS)
    .WithEnvironment("OTEL_SERVICE_NAME", "identity-api");

var basketApi = builder.AddProject<Projects.Basket_API>("basket-api")
    .WithReference(redis)
    .WithReference(rabbitMq)
    .WithEnvironment("Identity__Url", identityApi.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OTEL_EXPORTER_OTLP_ENDPOINT)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", OTEL_EXPORTER_OTLP_HEADERS)
    .WithEnvironment("OTEL_SERVICE_NAME", "basket-api");

var catalogApi = builder.AddProject<Projects.Catalog_API>("catalog-api")
    .WithReference(rabbitMq)
    .WithReference(catalogDb)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OTEL_EXPORTER_OTLP_ENDPOINT)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", OTEL_EXPORTER_OTLP_HEADERS)
    .WithEnvironment("OTEL_SERVICE_NAME", "catalog-api");

var orderingApi = builder.AddProject<Projects.Ordering_API>("ordering-api")
    .WithReference(rabbitMq)
    .WithReference(orderDb)
    .WithEnvironment("Identity__Url", identityApi.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OTEL_EXPORTER_OTLP_ENDPOINT)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", OTEL_EXPORTER_OTLP_HEADERS)
    .WithEnvironment("OTEL_SERVICE_NAME", "ordering-api");

builder.AddProject<Projects.OrderProcessor>("order-processor")
    .WithReference(rabbitMq)
    .WithReference(orderDb)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OTEL_EXPORTER_OTLP_ENDPOINT)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", OTEL_EXPORTER_OTLP_HEADERS)
    .WithEnvironment("OTEL_SERVICE_NAME", "order-processor");

builder.AddProject<Projects.PaymentProcessor>("payment-processor")
    .WithReference(rabbitMq)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OTEL_EXPORTER_OTLP_ENDPOINT)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", OTEL_EXPORTER_OTLP_HEADERS)
    .WithEnvironment("OTEL_SERVICE_NAME", "payment-processor");

var webHooksApi = builder.AddProject<Projects.Webhooks_API>("webhooks-api")
    .WithReference(rabbitMq)
    .WithReference(webhooksDb)
    .WithEnvironment("Identity__Url", identityApi.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OTEL_EXPORTER_OTLP_ENDPOINT)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", OTEL_EXPORTER_OTLP_HEADERS)
    .WithEnvironment("OTEL_SERVICE_NAME", "webhooks-api");

// Reverse proxies
builder.AddProject<Projects.Mobile_Bff_Shopping>("mobile-bff")
    .WithReference(catalogApi)
    .WithReference(identityApi)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OTEL_EXPORTER_OTLP_ENDPOINT)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", OTEL_EXPORTER_OTLP_HEADERS)
    .WithEnvironment("OTEL_SERVICE_NAME", "mobile-bff");

// Apps
var webhooksClient = builder.AddProject<Projects.WebhookClient>("webhooksclient")
    .WithReference(webHooksApi)
    .WithEnvironment("IdentityUrl", identityApi.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OTEL_EXPORTER_OTLP_ENDPOINT)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", OTEL_EXPORTER_OTLP_HEADERS)
    .WithEnvironment("OTEL_SERVICE_NAME", "webhooksclient");

var webApp = builder.AddProject<Projects.WebApp>("webapp")
    .WithReference(basketApi)
    .WithReference(catalogApi)
    .WithReference(orderingApi)
    .WithReference(rabbitMq)
    .WithEnvironment("IdentityUrl", identityApi.GetEndpoint("http"))
    .WithLaunchProfile("https")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", OTEL_EXPORTER_OTLP_ENDPOINT)
    .WithEnvironment("OTEL_EXPORTER_OTLP_HEADERS", OTEL_EXPORTER_OTLP_HEADERS)
    .WithEnvironment("OTEL_SERVICE_NAME", "webapp");

// Wire up the callback urls (self referencing)
webApp.WithEnvironment("CallBackUrl", webApp.GetEndpoint("https"));
webhooksClient.WithEnvironment("CallBackUrl", webhooksClient.GetEndpoint("https"));

// Identity has a reference to all of the apps for callback urls, this is a cyclic reference
identityApi.WithEnvironment("BasketApiClient", basketApi.GetEndpoint("http"))
           .WithEnvironment("OrderingApiClient", orderingApi.GetEndpoint("http"))
           .WithEnvironment("WebhooksApiClient", webHooksApi.GetEndpoint("http"))
           .WithEnvironment("WebhooksWebClient", webhooksClient.GetEndpoint("https"))
           .WithEnvironment("WebAppClient", webApp.GetEndpoint("https"));

builder.Build().Run();
