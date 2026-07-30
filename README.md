# 🚀 Event-Driven Architecture with .NET 10, MassTransit & RabbitMQ

Este repositorio demuestra la implementación de una **Arquitectura Orientada a Eventos (EDA)** sólida, resiliente y escalable utilizando **.NET 10**, **MassTransit**, **RabbitMQ** y el patrón de diseño **Transactional Outbox** con **Entity Framework Core**.

---

## 🏛️ Arquitectura del Sistema

La solución está dividida en servicios desacoplados que interactúan de forma asíncrona:

```text
[ Cliente HTTP ]
       │
       ▼
┌──────────────┐     Transacción ACID     ┌─────────────────────┐
│ Publisher API│ ───────────────────────► │ SQL Server (Outbox) │
└──────────────┘                          └──────────┬──────────┘
                                                     │ (Proceso Outbox)
                                                     ▼
                                          ┌─────────────────────┐
                                          │      RabbitMQ       │
                                          └──────────┬──────────┘
                                                     │ (Pub / Sub)
                                                     ▼
                                          ┌─────────────────────┐
                                          │   Consumer Worker   │
                                          └─────────────────────┘
```

## 🛠️ Tecnologías y Herramientas

- **Plataforma**: .NET 8 (Web API & Worker Service) ⚙️

- **Mensajería & Service Bus**: RabbitMQ & MassTransit 🚌

- **Base de Datos & ORM**: SQL Server & Entity Framework Core 🗄️

- **Contenedores**: Docker & Docker Compose 🐳

## 🧠 Desafíos Técnicos Resueltos

### 1. El Problema de la Doble Escritura (Dual-Write Problem)

En sistemas distribuidos, guardar en la base de datos y publicar en la cola de mensajes en dos pasos independientes puede causar inconsistencias si la red o el servicio de mensajería fallan entre ambas operaciones.

### 2. Patrón Transactional Outbox 🛡️

Para garantizar la consistencia eventual y evitar la pérdida de mensajes:

- Las órdenes y sus correspondientes eventos se guardan dentro de la **misma transacción ACID** en SQL Server.

- **MassTransit Outbox Delivery Service** lee la tabla `OutboxMessage` en segundo plano y envía los mensajes a RabbitMQ.

- Si RabbitMQ está fuera de línea, los eventos se acumulan de forma segura en la base de datos y se envían automáticamente cuando la conexión se restablece.

## 📦 Estructura de la Solución

- `EventDriven.Contracts`: Librería compartida que contiene la definición inmutable de los eventos (e.g., OrderCreatedEvent).

- `EventDriven.Publisher.Api`: Web API en .NET 8 encargada de recibir peticiones HTTP, registrar la transacción en SQL Server y delegar la publicación de eventos al Outbox.

- `EventDriven.Consumer.Worker`: Servicio de fondo (Worker Service) que consume y procesa los eventos mediante consumidores desacoplados (`IConsumer<T>`).

## 🚀 Guía de Inicio Rápido (Quickstart)

Requisitos Previos

- .NET 10 SDK 💻

- Docker Desktop 🐳

### 1. Levantar la infraestructura local

Inicia los servicios de RabbitMQ y SQL Server 2022 con Docker Compose:

```bash
 docker compose up -d
```

- RabbitMQ Management UI: `http://localhost:15672` (Usuario: `guest` | Clave: `guest`)

- SQL Server: `127.0.0.1,143` (Usuario: `sa` | Clave: `TuPassword123!`)

### 2. Aplicar Migraciones de la Base de Datos

Ubícate en la carpeta de la API (EventDriven.Publisher.Api) y ejecuta:

```bash
dotnet ef database update
```

### 3. Ejecutar la solución

Puedes iniciar ambos proyectos (`Publisher.Api` y `Consumer.Worker`) desde Visual Studio o ejecutando en terminales separadas:

```bash
# Terminal 1 - API
dotnet run --project EventDriven.Publisher.Api

# Terminal 2 - Worker
dotnet run --project EventDriven.Consumer.Worker
```

## 🧪 Pruebas de Resiliencia

1. Flujo Feliz: Realiza un `POST /orders` desde la API y observa en la consola del Worker cómo se procesa el evento al instante.

2. Prueba de Falla de Red:

    - Detén RabbitMQ: `docker stop rabbitmq-local`

    - Envía un `POST /orders` desde la API. Notarás que la orden se guarda y el mensaje queda retenido en la tabla `OutboxMessage`.

    - Vuelve a levantar RabbitMQ: `docker start rabbitmq-local`

    - Observa cómo el proceso del Outbox detecta la conexión y reenvía automáticamente todos los mensajes acumulados al Worker sin perder información.
