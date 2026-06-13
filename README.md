# Agente IA de Soporte Nivel 1 — Turnera de Pilates

Sistema multiagente de soporte técnico nivel 1 construido con .NET, MCP (Model Context Protocol) y Groq (LLaMA).

---

## Arquitectura

```
McpServer           → Servidor HTTP que expone las tools que usan los agentes
AgenteEntrada       → Recolecta datos del usuario y registra el incidente
AgenteEnrutador     → Consulta la KB, diagnostica y decide qué agente invocar
AgenteConversacion  → Gestiona la conversación con el usuario via Telegram
```

Flujo completo:

```
Usuario manda mensaje al bot de Telegram
        ↓
AgenteEntrada recolecta: descripción, módulo afectado, email
        ↓
AgenteEnrutador consulta la KB y decide el agente de acción
        ↓
Agente de acción ejecuta la solución en la turnera
        ↓
AgenteEntrada notifica al usuario el resultado
```

---

## Estructura del repositorio

```
ProyectoFinal/
├── McpServer/              → Servidor MCP HTTP con tools y agentes
│   ├── AgenteEntrada/      → Agente de recolección de datos
│   ├── AgenteEnrutador/    → Agente de diagnóstico y routing
│   ├── AgenteConversacion/ → Agente de conversación con Telegram
│   ├── Tools/              → Tools que llaman a la API y la turnera
│   ├── Api/                → Servicios que conectan con la API del backend
│   └── MessageQueue/       → Integración con AWS SQS
├── KnowledgeBase/          → Artículos de KB en formato .md
├── Prompts/                → System prompts de cada agente en formato .md
└── AgentMemory/            → Memoria de casos procesados por agente
```

---

## Requisitos

- .NET 10 SDK
- Cuenta en [Groq](https://console.groq.com) con API key

---

## Configuración — Variables de entorno

```bash
setx GROQ_API_KEY "tu_api_key_de_groq"
setx AGENTAI_API_URL "url_de_la_api_del_backend"
setx TURNERA_API_URL "url_de_la_api_de_la_turnera"
setx AGENT_API_KEY "api_key_de_la_turnera"
```

## Configuración — User Secrets (McpServer)

```bash
cd McpServer
dotnet user-secrets set "Groq:ApiKey" "tu_api_key_de_groq"
dotnet user-secrets set "Groq:Modelo" "llama-3.1-8b-instant"
dotnet user-secrets set "Api:BaseUrl" "url_de_la_api_del_backend"
dotnet user-secrets set "Api:Username" "tu_email_registrado_en_la_api"
dotnet user-secrets set "Api:Password" "tu_password"
dotnet user-secrets set "Telegram:BotToken" "token_del_bot"
dotnet user-secrets set "Telegram:DefaultChatId" "chat_id_de_prueba"
dotnet user-secrets set "ServiceNow:BaseUrl" "url_de_servicenow"
dotnet user-secrets set "ServiceNow:ClientId" "client_id_de_servicenow"
dotnet user-secrets set "ServiceNow:ClientSecret" "client_secret_de_servicenow"
dotnet user-secrets set "ServiceNow:Username" "usuario_de_servicenow"
dotnet user-secrets set "ServiceNow:Password" "password_de_servicenow"
```

---

## Registrar usuario en la API

La API usa autenticación JWT con AWS Cognito. Para registrarte:

```bash
# 1. Crear cuenta (usá un email real o de tempmail)
curl -X POST "URL_API/auth/sign-up" -H "Content-Type: application/json" -d "{\"email\":\"TU_EMAIL\",\"password\":\"TuPassword1!\"}"

# 2. Confirmar con el código que llega al email
curl -X POST "URL_API/auth/confirm" -H "Content-Type: application/json" -d "{\"email\":\"TU_EMAIL\",\"code\":\"CODIGO\"}"
```

---

## Levantar el McpServer localmente

```bash
cd McpServer
dotnet run
```

Ver las tools disponibles:

```bash
curl -X POST "http://localhost:PUERTO/mcp" -H "Content-Type: application/json" -d "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}"
```

---

## Bot de Telegram

El sistema usa un bot de Telegram como canal de comunicación con el usuario. Para probar el flujo completo mandá el número de ticket directamente al bot:

```
INC0010081
```

El bot va a pedir información adicional y resolver el problema automáticamente.

---

## API del backend

| Endpoint | Método | Auth | Descripción |
|---|---|---|---|
| `/health/live` | GET | No | Confirma que la app está levantada |
| `/health/ready` | GET | No | Confirma conexión con la BD |
| `/auth/sign-up` | POST | No | Registrar usuario |
| `/auth/confirm` | POST | No | Confirmar cuenta con código |
| `/auth/sign-in` | POST | No | Obtener JWT |
| `/tickets` | GET | Sí | Listar tickets |
| `/tickets/{id}` | GET | Sí | Obtener ticket por ID |
| `/tickets/by-number/{number}` | GET | Sí | Obtener ticket por número |
| `/tickets` | POST | Sí | Crear ticket |
| `/tickets/{id}` | PUT | Sí | Actualizar ticket |

---

## Base de datos

La KB está en archivos `.md` en la carpeta `KnowledgeBase/`. Cada archivo corresponde a un módulo del sistema de pilates.

Los prompts de cada agente están en la carpeta `Prompts/` y se cargan automáticamente al arrancar.