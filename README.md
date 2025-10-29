# HAGI.Robust

A shared resilience toolkit for HAGI microservices supporting both web applications and console applications.

## Features
- Polly Retry (1-3-9s) + Circuit Breaker
- Timeout policy
- Health and Readiness endpoints (web apps)
- Dependency Waiter (RabbitMQ, Redis, HTTP, TCP)
- ILogger integration
- Standalone mode for console apps
- Integrated mode for web apps

## Usage

### Standalone Mode (Console Apps)

Simple 3-line integration for console applications:

```csharp
using Hagi.Robust;
using Hagi.Robust.Probes;

Console.WriteLine("Starting ChatService...");

// Wait for RabbitMQ to be ready
var rabbitProbe = new RabbitMqProbe("rabbitmq", 5672);
await HagiRobust.WaitForDependenciesAsync(new[] { rabbitProbe });

Console.WriteLine("RabbitMQ is ready! Starting service...");
// Continue with your application logic
```

Available probes:
- `RabbitMqProbe(host, port)` - RabbitMQ connection
- `RedisProbe(host, port)` - Redis connection
- `HttpProbe(url)` - HTTP endpoint availability
- `TcpProbe(host, port)` - Generic TCP port check

Multiple dependencies:
```csharp
var probes = new IStartupProbe[]
{
    new RabbitMqProbe("rabbitmq", 5672),
    new RedisProbe("redis", 6379),
    new HttpProbe("http://authservice:8080/health")
};

await HagiRobust.WaitForDependenciesAsync(probes);
```

### Integrated Mode (Web Apps)

Full integration with ASP.NET Core health endpoints:

```csharp
using Hagi.Robust;
using Hagi.Robust.Probes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHagiResilience();
builder.Services.AddSingleton<IStartupProbe>(sp => new RabbitMqProbe("rabbitmq", 5672));

var app = builder.Build();
app.MapReadinessEndpoint();  // Creates endpoint at /health/ready
app.Run();
```

## Installation

```bash
dotnet add package HAGI.Robust --version 1.1.0
```

Or from GitHub Packages:
```bash
dotnet add package HAGI.Robust --source https://nuget.pkg.github.com/MTBonde/index.json --version 1.1.0
```

## Repository

[https://github.com/MTBonde/HAGI.Robust](https://github.com/MTBonde/HAGI.Robust)
