# Integrating HAGI.Robust with Docker Compose

This guide explains how to integrate HAGI.Robust health checks into your Docker Compose configuration for proper service orchestration and dependency management.

## Overview

HAGI.Robust provides a `/health/ready` endpoint that reports when a service and all its dependencies are ready. This endpoint can be used in Docker Compose health checks to ensure proper service startup ordering.

## Current Compose File Analysis

Based on the analysis of `docker-compose.yml` at https://github.com/MTBonde/PB_SI_HAGI_ClientDummy:

### Service Inventory

The compose file contains **9 services**:

1. **rabbitmq** - Message broker (rabbitmq:3-management)
2. **redis** - Cache/data store (redis:7-alpine)
3. **auth** - Authentication service (ghcr.io/mtbonde/hagi-authservice:0.2.1)
4. **registry** - Registry service (ghcr.io/mtbonde/hagi-registryservice:0.2.3)
5. **game** - Game server (ghcr.io/alexanderlind98/unrealmp_server:latest)
6. **session** - Session management (ghcr.io/mtbonde/hagi-sessionservice:0.2.1)
7. **chat** - Chat service (ghcr.io/mtbonde/hagi-chatservice:0.2.0)
8. **relay** - Relay service (ghcr.io/mtbonde/hagi-relayservice:0.3.0)
9. **dummyclient** - Test client (local build)

### Existing Health Checks

Currently, only **infrastructure services** have health checks:

- **rabbitmq**: `rabbitmq-diagnostics -q ping` (interval: 5s, retries: 30)
- **redis**: `redis-cli ping` (interval: 5s, retries: 30)

### Current Issues

1. **No health checks on microservices** - auth, registry, game, session, chat, relay lack health checks
2. **Blind dependencies** - dummyclient waits for services to start but not for them to be ready
3. **Manual delay workaround** - Program.cs has `await Task.Delay(5000)` to wait for services
4. **No startup ordering** - Services like session/chat/relay don't wait for auth or registry to be ready

## Recommended Configuration

### Basic Health Check Configuration

For any service with HAGI.Robust integrated:

```yaml
servicename:
  image: ghcr.io/yourorg/yourservice:version
  ports:
    - "5000:8080"
  healthcheck:
    test: ["CMD", "curl", "-f", "http://localhost:8080/health/ready"]
    interval: 5s
    timeout: 3s
    retries: 30
    start_period: 10s
  networks:
    - hagi-net
```

### Health Check Parameters Explained

- **test**: Command to check service health (using curl to call `/health/ready`)
- **interval**: How often to run health checks (5s recommended)
- **timeout**: Max time for health check to complete (3s)
- **retries**: Number of consecutive failures before marking unhealthy (30 retries)
- **start_period**: Grace period before health checks count towards retries (10-15s)

### Service Dependencies with Health Conditions

```yaml
service_a:
  depends_on:
    rabbitmq:
      condition: service_healthy  # Waits for RabbitMQ health check to pass
    redis:
      condition: service_healthy  # Waits for Redis health check to pass
```

## Complete Example docker-compose.yml

```yaml
services:
  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 5s
      timeout: 3s
      retries: 30
    networks:
      - hagi-net

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 30
    networks:
      - hagi-net

  auth:
    image: ghcr.io/mtbonde/hagi-authservice:0.2.1
    ports:
      - "5000:8080"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/ready"]
      interval: 5s
      timeout: 3s
      retries: 30
      start_period: 10s
    networks:
      - hagi-net

  registry:
    image: ghcr.io/mtbonde/hagi-registryservice:0.2.3
    ports:
      - "5001:8080"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/ready"]
      interval: 5s
      timeout: 3s
      retries: 30
      start_period: 10s
    networks:
      - hagi-net

  game:
    image: ghcr.io/alexanderlind98/unrealmp_server:latest
    ports:
      - "5002:7777/udp"
      - "5002:8080"
    environment:
      - SERVER_HOST=127.0.0.1
      - SERVER_PORT=5002
      - REGISTRY_URL=http://registry:8080
    depends_on:
      registry:
        condition: service_healthy
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/ready"]
      interval: 5s
      timeout: 3s
      retries: 30
      start_period: 15s
    networks:
      - hagi-net

  session:
    image: ghcr.io/mtbonde/hagi-sessionservice:0.2.1
    depends_on:
      rabbitmq:
        condition: service_healthy
      redis:
        condition: service_healthy
    environment:
      - RABBITMQ_HOST=rabbitmq
      - REDIS_HOST=redis:6379
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/ready"]
      interval: 5s
      timeout: 3s
      retries: 30
      start_period: 10s
    networks:
      - hagi-net

  chat:
    image: ghcr.io/mtbonde/hagi-chatservice:0.2.0
    depends_on:
      rabbitmq:
        condition: service_healthy
    environment:
      - RABBITMQ_HOST=rabbitmq
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/ready"]
      interval: 5s
      timeout: 3s
      retries: 30
      start_period: 10s
    networks:
      - hagi-net

  relay:
    image: ghcr.io/mtbonde/hagi-relayservice:0.3.0
    ports:
      - "5004:8080"
    depends_on:
      rabbitmq:
        condition: service_healthy
    environment:
      - RABBITMQ_HOST=rabbitmq
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/ready"]
      interval: 5s
      timeout: 3s
      retries: 30
      start_period: 10s
    networks:
      - hagi-net

  dummyclient:
    build:
      context: ./DummyClient
      dockerfile: Dockerfile
    depends_on:
      auth:
        condition: service_healthy
      registry:
        condition: service_healthy
      game:
        condition: service_healthy
      relay:
        condition: service_healthy
    environment:
      - AUTH_URL=http://auth:8080
      - REGISTRY_URL=http://registry:8080
      - GAME_URL=http://game:8080
      - RELAY_URL=http://relay:8080
    networks:
      - hagi-net

networks:
  hagi-net:
    driver: bridge
```

## Alternative Health Check Options

If `curl` is not available in your Docker images:

### Option 1: wget

```yaml
healthcheck:
  test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8080/health/ready"]
```

### Option 2: nc (netcat) - Basic Port Check

```yaml
healthcheck:
  test: ["CMD", "nc", "-z", "localhost", "8080"]
```

Note: This only checks if the port is open, not if the service is ready.

### Option 3: Custom Shell Script

```yaml
healthcheck:
  test: ["CMD", "/bin/sh", "-c", "wget -q -O- http://localhost:8080/health/ready || exit 1"]
```

## Benefits of This Configuration

1. **Eliminates manual delays** - Remove `await Task.Delay(5000)` from Program.cs
2. **Proper startup ordering** - Services start only when dependencies are ready
3. **Faster failure detection** - Compose knows immediately if a service fails to become healthy
4. **Better observability** - `docker-compose ps` shows health status of all services
5. **Reliable testing** - DummyClient starts only when all services are actually ready

## Implementation Steps

1. **Verify HAGI.Robust integration** - Confirm which services have the `/health/ready` endpoint
2. **Add healthcheck blocks** - Add to each service that has the endpoint
3. **Update depends_on sections** - Use `condition: service_healthy` for proper ordering
4. **Test the configuration** - Run `docker-compose up` and monitor health status
5. **Remove manual delays** - Remove `Task.Delay()` workarounds from your code

## Verification Commands

### Check health status of all services

```bash
docker-compose ps
```

### View health check logs for a specific service

```bash
docker inspect --format='{{json .State.Health}}' <container_name> | jq
```

### Watch services start up with health checks

```bash
docker-compose up --build
```

### Check if a specific service is healthy

```bash
docker inspect --format='{{.State.Health.Status}}' <container_name>
```

## Troubleshooting

### Service never becomes healthy

- Check if the service is actually listening on the expected port
- Verify the `/health/ready` endpoint is accessible inside the container
- Increase `start_period` if the service takes longer to start
- Check container logs: `docker-compose logs <service_name>`

### curl: command not found

- Use an alternative health check method (wget, nc, or shell script)
- Install curl in your Docker image: `RUN apt-get update && apt-get install -y curl`

### Health checks taking too long

- Reduce the number of `retries`
- Decrease the `interval` between checks
- Optimize your service startup time

## Additional Resources

- [Docker Compose Health Check Documentation](https://docs.docker.com/compose/compose-file/compose-file-v3/#healthcheck)
- [HAGI.Robust GitHub Repository](https://github.com/MTBonde/HAGI.Robust)
- [Docker Health Check Best Practices](https://docs.docker.com/engine/reference/builder/#healthcheck)
