# HAGI.Robust

A shared resilience toolkit for HAGI microservices.

## Features
- Polly Retry (1-3-9s) + Circuit Breaker
- Timeout policy
- Health and Readiness endpoints
- Dependency Waiter (waits for Rabbit/DB)
- ILogger integration
- One-line registration via `AddHagiResilience()`

## Usage
```csharp
using Hagi.Robust;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHagiResilience();
builder.Services.AddSingleton<IStartupProbe>(sp => new RabbitProbe(/* ... */));

var app = builder.Build();
app.MapReadinessEndpoint();
app.Run();

```

## Installation

```bash
dotnet add package HAGI.Robust
```

## Repository

[https://github.com/MTBonde/HAGI.Robust](https://github.com/MTBonde/HAGI.Robust)
