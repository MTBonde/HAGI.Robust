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
builder.Services.AddHagiResilience();
app.MapHealthChecks("/health/ready");
```

## Installation

```bash
dotnet add package HAGI.Robust
```

## Repository

[https://github.com/MTBonde/HAGI.Robust](https://github.com/MTBonde/HAGI.Robust)
