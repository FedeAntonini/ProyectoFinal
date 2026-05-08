# Agente IA de Soporte Nivel 1 — E-Commerce

Sistema multiagente de soporte técnico nivel 1 construido con .NET, MCP (Model Context Protocol) y Groq (LLaMA 3.3).

---

## Arquitectura

```
AgenteEntrada    → Recolecta datos del usuario y registra el incidente
AgenteEnrutador  → Lee el ticket, diagnostica y decide qué agente invocar
AgenteAccion     → Recibe el diagnóstico y levanta el subagente correcto
McpServer        → Expone las tools que usan los agentes
```

Flujo completo:

```
Usuario reporta problema
        ↓
AgenteEntrada recolecta: descripción, sistema, tipo de error, email
        ↓
AgenteEnrutador lee el ticket y decide: DELEGAR_A: [agente]
        ↓
AgenteAccion levanta el subagente correcto
        ↓
Subagente ejecuta la acción y cierra el ticket
```

---

## Estructura del repositorio

```
ProyectoFinal/
├── McpServer/          → MCP Server con todas las tools
├── AgenteEntrada/      → Recolecta datos del incidente
├── AgenteEnrutador/    → Diagnostica y deriva
├── AgenteAccion/       → Levanta el subagente correcto
└── ProyectoFinal.slnx  → Solución .NET
```

## Ramas

- `main` — código estable mergeado
- `agente-entrada` — desarrollo del Agente de Entrada
- `agente-enrutador` — desarrollo del Agente Enrutador
- `agente-accion` — desarrollo del Agente de Acción
- `mcp-server` — desarrollo del MCP Server

---

## Requisitos

- .NET 10 SDK
- Cuenta en [Groq](https://console.groq.com) con API key

---

## Configuración

Configurá la variable de entorno antes de correr cualquier agente:

```bash
setx GROQ_API_KEY "tu_api_key_de_groq"
```

Cerrá y abrí la terminal para que tome efecto. Verificá:

```bash
echo %GROQ_API_KEY%
```

---

## Cómo probar

### Paso 1 — AgenteEntrada

```bash
cd AgenteEntrada
dotnet run
```

Simulá un usuario reportando un problema. El agente va a hacer preguntas hasta tener toda la información y registrar el incidente. Ejemplo:

```
Usuario: No puedo iniciar sesión
Agente:  ¿Qué sistema estás usando?
Usuario: El sistema de usuarios
Agente:  ¿Qué tipo de error es?
Usuario: Un dato incorrecto
Agente:  ¿Cuál es tu email?
Usuario: test@empresa.com
Agente:  Tu incidente fue registrado con el ID: INC1234
```

### Paso 2 — AgenteEnrutador

Abrí otra terminal:

```bash
cd AgenteEnrutador
dotnet run
```

Ingresá un ticket conocido (INC0001 a INC0005):

```
Input: Tengo el ticket INC0002
[Enrutador] DELEGAR_A: AgenteAccionPedido
```

### Paso 3 — AgenteAccion

Abrí otra terminal:

```bash
cd AgenteAccion
dotnet run
```

Pegá el diagnóstico del Enrutador:

```
Diagnóstico: DELEGAR_A: AgenteAccionPedido
[AgenteAccion] Levantando subagente: PEDIDO
[SubagentePedido] Su pedido ORD-5521 está en preparación...
```

---

## Tickets de prueba disponibles

| ID | Usuario | Problema | Sistema |
|---|---|---|---|
| INC0001 | juan.perez@empresa.com | No puede iniciar sesión | usuarios |
| INC0002 | maria.gomez@empresa.com | Pedido ORD-5521 pendiente | pedidos |
| INC0003 | carlos.ruiz@empresa.com | Pago rechazado con débito | pagos |
| INC0004 | laura.diaz@empresa.com | Precio incorrecto SKU-8821 | catalogo |
| INC0005 | admin@empresa.com | Stock SKU-3310 no sincronizado | stock |

---

## Subagentes implementados

| Subagente | Problema | Estado |
|---|---|---|
| SubagentePedido | Consulta estado de pedido | ✅ Funcionando |
| SubagenteAcceso | Resetea acceso de usuario | 🔜 Pendiente conexión BD |
| SubagentePago | Consulta pago rechazado | 🔜 Pendiente conexión BD |
| SubagentePrecio | Corrige precio en catálogo | 🔜 Pendiente conexión BD |
| SubagenteStock | Sincroniza stock | 🔜 Pendiente conexión BD |

---

## Notas

- El modelo de IA usado es `llama-3.3-70b-versatile` de Groq.
- Groq free tier tiene límite de requests por minuto. Si el agente se cuelga, esperá 1-2 minutos.
- Los datos de tickets y pedidos son hardcodeados. Se reemplazarán por consultas a la BD real cuando el backend tenga los endpoints listos.
- Para agregar un nuevo subagente: agregar sus tools al McpServer, filtrarlas en AgenteAccion y agregar su system prompt.