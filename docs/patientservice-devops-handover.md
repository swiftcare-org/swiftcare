# PatientService — DevOps Handover (SWC-9)

PatientService now exists as a working, tested .NET service (`services/PatientService/`) but is **not yet wired into `docker-compose.yml` or `.env.example`**. Per the developer/DevOps split, I have not touched either file. This note lists exactly what's needed to bring it into the local compose stack.

## `.env.example` — new keys

`PATIENT_DB_NAME` already exists (used by `mysql-init` to create the `swiftcare_patient` database). No new key is needed for the database name itself. Add:

| Key | Purpose | Notes |
| --- | --- | --- |
| `PATIENT_SERVICE_PORT` | Host port mapped to the container's `5002` | Mirrors `AUTH_SERVICE_PORT` (default suggestion: `5002`) |

`GATEWAY_INTERNAL_SECRET`, `KAFKA_BOOTSTRAP_SERVERS`, and the MySQL credentials are already defined (reused from AuthService's block) and need no new entries.

## `docker-compose.yml` — new service block

A `patientservice` block, mirroring `authservice`'s shape:

```yaml
  patientservice:
    build:
      context: .
      dockerfile: services/PatientService/Dockerfile
    restart: unless-stopped
    depends_on:
      mysql:
        condition: service_healthy
      kafka:
        condition: service_healthy
    environment:
      ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}
      ConnectionStrings__PatientDb: "Server=mysql;Port=3306;Database=${PATIENT_DB_NAME};User Id=${MYSQL_USER};Password=${MYSQL_PASSWORD};"
      Gateway__InternalSecret: ${GATEWAY_INTERNAL_SECRET}
      Kafka__BootstrapServers: kafka:29092
    ports:
      - "${PATIENT_SERVICE_PORT:-5002}:5002"
    networks:
      - swiftcare-network
```

Note `Kafka__BootstrapServers` is set to the in-network broker address (`kafka:29092`), not the `${KAFKA_BOOTSTRAP_SERVERS}` env var AuthService uses — confirm whether that var already resolves to the in-network address or the host-facing one (`localhost:9092`) before reusing it here; PatientService's startup check only verifies the value is *present*, not that it points at the right listener.

## ApiGateway — route destination override

`ApiGateway/appsettings.json` already has `ReverseProxy:Clusters:patient-cluster:Destinations:patient-destination:Address` set to `http://localhost:5002` for local (non-compose) development. Once `patientservice` is in compose, add an override alongside `apigateway`'s existing `ReverseProxy__Clusters__auth-cluster__...` line:

```yaml
      ReverseProxy__Clusters__patient-cluster__Destinations__patient-destination__Address: http://patientservice:5002
```

And add `patientservice` to `apigateway`'s `depends_on` (with `condition: service_healthy`, matching how it depends on `authservice`).

## Migration

`swiftcare_patient` is created empty by `mysql-init` today. The `InitialPatientSchema` migration (`services/PatientService/Migrations/`) is committed but has not been applied to any shared/staging environment — per the migration rules, that application is a controlled deployment step, not something to automate at container startup.
