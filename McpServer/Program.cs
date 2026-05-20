using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using McpServer.MessageQueue;
using McpServer.Agentes;
using McpServer.Services;
using McpServer.Api;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddScoped<AgenteEntrada>();
builder.Services.AddScoped<AgenteConversacion>();
builder.Services.AddScoped<AgenteEnrutador>();
builder.Services.AddLlmServices(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});


if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddMessageQueues(builder.Configuration);
    builder.Services.AddHostedService<QueueWorker>();
}


var app = builder.Build();

app.MapMcp("/mcp");

app.MapGet("/debug/tools", () =>
{
    var assembly = typeof(Program).Assembly;

    var tools = assembly
        .GetTypes()
        .Where(t => t.GetCustomAttributes(typeof(McpServerToolTypeAttribute), true).Any())
        .Select(t => new
        {
            ToolType = t.FullName,
            Methods = t.GetMethods()
                .Where(m => m.GetCustomAttributes(typeof(McpServerToolAttribute), true).Any())
                .Select(m => new
                {
                    Method = m.Name,
                    ReturnType = m.ReturnType.FullName
                })
        });

    return Results.Json(tools);
});

// Endpoint de prueba 
app.MapPost("/debug/enrutar/{ticketId}", async (
    int ticketId,
    ILlmService llm,
    CancellationToken ct) =>
{
    // Obtener el ticket de las tools hardcodeadas
    var ticket = ToolsEnrutador.ObtenerTicket($"INC{ticketId:D4}");

    const string systemPrompt = """
        Sos el Agente Enrutador de un sistema de soporte nivel 1 para un estudio de pilates.
        
        Analizá el problema y decidí cuál agente es el más adecuado:
        - AgenteAccionReserva: problemas con reservas de turnos
        - AgenteAccionAcceso: problemas de login o acceso
        - AgenteAccionPago: problemas con cobros o pagos
        - AgenteAccionNotificacion: no recibió confirmación de turno
        - Escalacion: si no encaja en ninguno
        
        Respondé ÚNICAMENTE con JSON: {"agente": "NombreDelAgente", "motivo": "explicación breve"}
        Sin backticks ni texto adicional.
        """;

    var response = await llm.CompleteAsync(
        systemPrompt,
        [new(ChatRole.User, $"Datos del ticket:\n{ticket}")],
        ct);

    return Results.Ok(new { ticketId, ticket, decision = response });
});

app.Run();

// ============================================================
// TOOLS DEL AGENTE ENTRADA
// ============================================================
[McpServerToolType]
public static class ToolsEntrada
{
    [McpServerTool, Description("Registra los datos del incidente cuando el agente tiene toda la información necesaria para derivar.")]
    public static string RegistrarIncidente(
        [Description("Descripción clara del problema reportado por el usuario")]
        string descripcion,
        [Description("Sistema afectado: usuarios, pedidos, pagos, catalogo, stock")]
        string sistema,
        [Description("Tipo de error: dato_incorrecto, operacion_bloqueada, inconsistencia, error_sistema")]
        string tipoError,
        [Description("Email del usuario que reporta el incidente")]
        string usuario)
    {
        var ticketId = $"INC{new Random().Next(1000, 9999)}";
        return $"{{ \"ticketId\": \"{ticketId}\", \"descripcion\": \"{descripcion}\", \"sistema\": \"{sistema}\", \"tipoError\": \"{tipoError}\", \"usuario\": \"{usuario}\", \"estado\": \"Abierto\" }}";
    }
}


// ============================================================
// TOOLS DEL AGENTE ENRUTADOR
// ============================================================

/*[McpServerToolType]
public static class ToolsEnrutador
{
    [McpServerTool, Description("Obtiene los detalles de un ticket de soporte por su ID.")]
    public static string ObtenerTicket(
        [Description("ID del ticket, por ejemplo: INC0001")]
        string ticketId)
    {
        var tickets = new Dictionary<string, object>
        {
            ["INC0001"] = new { Usuario = "juan.perez@empresa.com", Problema = "No puedo iniciar sesión en la plataforma", Prioridad = "Alta", Sistema = "usuarios" },
            ["INC0002"] = new { Usuario = "maria.gomez@empresa.com", Problema = "Mi pedido ORD-5521 figura como pendiente hace 3 días", Prioridad = "Media", Sistema = "pedidos" },
            ["INC0003"] = new { Usuario = "carlos.ruiz@empresa.com", Problema = "Me rechazaron el pago con tarjeta pero el dinero fue debitado", Prioridad = "Alta", Sistema = "pagos" },
            ["INC0004"] = new { Usuario = "laura.diaz@empresa.com", Problema = "El precio del producto SKU-8821 está mal cargado", Prioridad = "Media", Sistema = "catalogo" },
            ["INC0005"] = new { Usuario = "admin@empresa.com", Problema = "El stock del producto SKU-3310 no se sincronizó", Prioridad = "Baja", Sistema = "stock" },
        };

        if (tickets.TryGetValue(ticketId.ToUpper(), out var ticket))
            return $"Ticket {ticketId}: {System.Text.Json.JsonSerializer.Serialize(ticket)}";

        return $"Ticket {ticketId} no encontrado.";
    }

    [McpServerTool, Description("Analiza el problema del ticket y determina qué agente de acción debe resolverlo.")]
    public static string DiagnosticarProblema(
        [Description("Descripción del problema")]
        string descripcion,
        [Description("Sistema afectado: usuarios, pedidos, pagos, catalogo, stock")]
        string sistema)
    {
        return (sistema.ToLower()) switch
        {
            "usuarios" => "{ \"agente\": \"AgenteAccionAcceso\", \"accion\": \"resetear_acceso\", \"confianza\": \"alta\" }",
            "pedidos"  => "{ \"agente\": \"AgenteAccionPedido\", \"accion\": \"consultar_estado_pedido\", \"confianza\": \"alta\" }",
            "pagos"    => "{ \"agente\": \"AgenteAccionPago\", \"accion\": \"consultar_pago\", \"confianza\": \"alta\" }",
            "catalogo" or "precio" => "{ \"agente\": \"AgenteAccionPrecio\", \"accion\": \"corregir_precio\", \"confianza\": \"alta\" }",
            "stock"    => "{ \"agente\": \"AgenteAccionStock\", \"accion\": \"sincronizar_stock\", \"confianza\": \"alta\" }",
            _          => "{ \"agente\": \"Escalacion\", \"accion\": \"escalar_nivel2\", \"confianza\": \"baja\" }"
        };
    }
}*/

[McpServerToolType]
public static class ToolsEnrutador
{
    [McpServerTool, Description("Obtiene los detalles de un ticket de soporte por su ID.")]
    public static string ObtenerTicket(
        [Description("ID del ticket, por ejemplo: INC0001")]
        string ticketId)
    {
        var tickets = new Dictionary<string, object>
        {
            ["INC0001"] = new { Usuario = "ana.garcia@mail.com", Problema = "No puedo iniciar sesión en la app del estudio", Prioridad = "Alta", Sistema = "acceso" },
            ["INC0002"] = new { Usuario = "lucas.perez@mail.com", Problema = "Reservé un turno para el martes a las 9am pero no me aparece en mi calendario", Prioridad = "Media", Sistema = "reserva" },
            ["INC0003"] = new { Usuario = "sofia.ruiz@mail.com", Problema = "Me cobraron dos veces la clase del jueves", Prioridad = "Alta", Sistema = "pago" },
            ["INC0004"] = new { Usuario = "martin.lopez@mail.com", Problema = "Quiero cancelar mi turno del viernes pero el botón no funciona", Prioridad = "Media", Sistema = "reserva" },
            ["INC0005"] = new { Usuario = "paula.diaz@mail.com", Problema = "Reservé turno pero no recibí ningún mail de confirmación", Prioridad = "Baja", Sistema = "notificacion" },
        };

        if (tickets.TryGetValue(ticketId.ToUpper(), out var ticket))
            return $"Ticket {ticketId}: {System.Text.Json.JsonSerializer.Serialize(ticket)}";

        return $"Ticket {ticketId} no encontrado.";
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — PEDIDO
// ============================================================
[McpServerToolType]
public static class ToolsAccionPedido
{
    [McpServerTool, Description("Consulta el estado actual de un pedido por su ID.")]
    public static string ConsultarEstadoPedido(
        [Description("ID del pedido, por ejemplo: ORD-5521")]
        string pedidoId)
    {
        var pedidos = new Dictionary<string, object>
        {
            ["ORD-5521"] = new { Estado = "En preparación", FechaEstimada = "2026-05-10", Ubicacion = "Depósito central" },
            ["ORD-8834"] = new { Estado = "Enviado", FechaEstimada = "2026-05-08", Ubicacion = "En tránsito - correo argentino" },
            ["ORD-1122"] = new { Estado = "Entregado", FechaEstimada = "2026-05-05", Ubicacion = "Entregado al destinatario" },
        };

        if (pedidos.TryGetValue(pedidoId.ToUpper(), out var pedido))
            return $"Pedido {pedidoId}: {System.Text.Json.JsonSerializer.Serialize(pedido)}";

        return $"Pedido {pedidoId} no encontrado.";
    }

    [McpServerTool, Description("Cierra un ticket de pedido una vez resuelto.")]
    public static string CerrarTicketPedido(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return $"[PEDIDO] Ticket {ticketId} cerrado. Resolución: {resolucion}";
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — ACCESO
// ============================================================
[McpServerToolType]
public static class ToolsAccionAcceso
{
    [McpServerTool, Description("Resetea el acceso de un usuario que no puede iniciar sesión.")]
    public static string ResetearAcceso(
        [Description("Email del usuario con problema de acceso")]
        string usuario)
    {
        return $"[ACCESO] Acceso reseteado para {usuario}. Se envió un link de recuperación al email registrado.";
    }

    [McpServerTool, Description("Cierra un ticket de acceso una vez resuelto.")]
    public static string CerrarTicketAcceso(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return $"[ACCESO] Ticket {ticketId} cerrado. Resolución: {resolucion}";
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — PAGO
// ============================================================
[McpServerToolType]
public static class ToolsAccionPago
{
    [McpServerTool, Description("Consulta el estado de un pago rechazado y verifica si el débito fue aplicado.")]
    public static string ConsultarPago(
        [Description("Email del usuario con problema de pago")]
        string usuario)
    {
        return $"[PAGO] Consultando pago para {usuario}. Se detectó un débito pendiente de reversión. El área de pagos fue notificada para procesar el reembolso en 48hs hábiles.";
    }

    [McpServerTool, Description("Cierra un ticket de pago una vez resuelto.")]
    public static string CerrarTicketPago(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return $"[PAGO] Ticket {ticketId} cerrado. Resolución: {resolucion}";
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — PRECIO
// ============================================================
[McpServerToolType]
public static class ToolsAccionPrecio
{
    [McpServerTool, Description("Corrige el precio de un producto en el catálogo.")]
    public static string CorregirPrecio(
        [Description("SKU del producto con precio incorrecto")]
        string sku,
        [Description("Precio correcto del producto")]
        string precioCorrecto)
    {
        return $"[PRECIO] Precio del producto {sku} actualizado a {precioCorrecto}. Catálogo actualizado exitosamente.";
    }

    [McpServerTool, Description("Cierra un ticket de precio una vez resuelto.")]
    public static string CerrarTicketPrecio(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return $"[PRECIO] Ticket {ticketId} cerrado. Resolución: {resolucion}";
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — STOCK
// ============================================================
[McpServerToolType]
public static class ToolsAccionStock
{
    [McpServerTool, Description("Sincroniza el stock de un producto que no se actualizó correctamente.")]
    public static string SincronizarStock(
        [Description("SKU del producto con stock desincronizado")]
        string sku)
    {
        return $"[STOCK] Stock del producto {sku} sincronizado exitosamente. Inventario actualizado con los datos del último archivo de importación.";
    }

    [McpServerTool, Description("Cierra un ticket de stock una vez resuelto.")]
    public static string CerrarTicketStock(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return $"[STOCK] Ticket {ticketId} cerrado. Resolución: {resolucion}";
    }
}
