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

app.Run();

// ============================================================
// TOOLS DEL AGENTE ENTRADA
// ============================================================
[McpServerToolType]
public static class ToolsEntrada
{
    [McpServerTool, Description("Registra los datos del incidente cuando el agente tiene toda la información necesaria para derivar.")]
    public static string RegistrarIncidente(
        [Description("Descripción clara del problema reportado por el socio")]
        string descripcion,
        [Description("Sistema afectado: socios, turnos, pagos, clases, instructores")]
        string sistema,
        [Description("Tipo de error: dato_incorrecto, operacion_bloqueada, inconsistencia, error_sistema")]
        string tipoError,
        [Description("Email del socio que reporta el incidente")]
        string usuario)
    {
        var ticketId = $"INC{new Random().Next(1000, 9999)}";
        return $"{{ \"ticketId\": \"{ticketId}\", \"descripcion\": \"{descripcion}\", \"sistema\": \"{sistema}\", \"tipoError\": \"{tipoError}\", \"usuario\": \"{usuario}\", \"estado\": \"Abierto\" }}";
    }
}


// ============================================================
// TOOLS DEL AGENTE ENRUTADOR
// ============================================================
[McpServerToolType]
public static class ToolsEnrutador
{
    [McpServerTool, Description("Obtiene los detalles de un ticket de soporte de la turnera de pilates por su ID.")]
    public static string ObtenerTicket(
        [Description("ID del ticket, por ejemplo: INC0001")]
        string ticketId)
    {
        var tickets = new Dictionary<string, object>
        {
            ["INC0001"] = new { Usuario = "ana.garcia@gmail.com", Problema = "No puedo iniciar sesion en la app de la turnera", Prioridad = "Alta", Sistema = "socios" },
            ["INC0002"] = new { Usuario = "carlos.lopez@gmail.com", Problema = "Hice una reserva y no aparece en el sistema", Prioridad = "Media", Sistema = "turnos" },
            ["INC0003"] = new { Usuario = "maria.fernandez@gmail.com", Problema = "Pague un paquete de clases y no se acreditaron los creditos", Prioridad = "Alta", Sistema = "pagos" },
            ["INC0004"] = new { Usuario = "roberto.sanchez@gmail.com", Problema = "El turno esta completo pero deberia haber lugares disponibles", Prioridad = "Media", Sistema = "clases" },
            ["INC0005"] = new { Usuario = "laura.torres@gmail.com", Problema = "El profesor de mi reserva es diferente al que elegi", Prioridad = "Media", Sistema = "instructores" },
        };

        if (tickets.TryGetValue(ticketId.ToUpper(), out var ticket))
            return $"Ticket {ticketId}: {System.Text.Json.JsonSerializer.Serialize(ticket)}";

        return $"Ticket {ticketId} no encontrado.";
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — SOCIOS
// ============================================================
[McpServerToolType]
public static class ToolsAccionSocio
{
    [McpServerTool, Description("Resetea el acceso de un socio que no puede ingresar a la app de la turnera.")]
    public static string ResetearAccesoSocio(
        [Description("Email del socio con problema de acceso")]
        string email)
    {
        return $"[SOCIOS] Acceso reseteado para {email}. Se envió un link de recuperación al email registrado.";
    }

    [McpServerTool, Description("Cierra un ticket de socios una vez resuelto.")]
    public static string CerrarTicketSocio(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return $"[SOCIOS] Ticket {ticketId} cerrado. Resolución: {resolucion}";
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — TURNOS
// ============================================================
[McpServerToolType]
public static class ToolsAccionTurno
{
    [McpServerTool, Description("Consulta el estado de un turno y reprograma si fue cancelado incorrectamente.")]
    public static string ConsultarTurno(
        [Description("Email del socio con problema de turno")]
        string email)
    {
        return $"[TURNOS] Se verificó el turno de {email}. El turno fue reprogramado para el mismo horario de la semana siguiente y se notificó al socio por email.";
    }

    [McpServerTool, Description("Cierra un ticket de turnos una vez resuelto.")]
    public static string CerrarTicketTurno(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return $"[TURNOS] Ticket {ticketId} cerrado. Resolución: {resolucion}";
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — PAGOS
// ============================================================
[McpServerToolType]
public static class ToolsAccionPago
{
    [McpServerTool, Description("Verifica el estado de la cuota de un socio y gestiona devoluciones por cobros duplicados.")]
    public static string ConsultarCuota(
        [Description("Email del socio con problema de pago")]
        string email)
    {
        return $"[PAGOS] Se verificó el cobro de {email}. Se detectó un cobro duplicado de la cuota mensual. El área de administración fue notificada para procesar la devolución en 48hs hábiles.";
    }

    [McpServerTool, Description("Cierra un ticket de pagos una vez resuelto.")]
    public static string CerrarTicketPago(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return $"[PAGOS] Ticket {ticketId} cerrado. Resolución: {resolucion}";
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — CLASES
// ============================================================
[McpServerToolType]
public static class ToolsAccionClase
{
    [McpServerTool, Description("Habilita una clase que figura incorrectamente como no disponible en el sistema de turnos.")]
    public static string HabilitarClase(
        [Description("Nombre o descripción de la clase a habilitar")]
        string clase)
    {
        return $"[CLASES] La clase '{clase}' fue habilitada correctamente en el sistema. Los socios ya pueden reservar turnos.";
    }

    [McpServerTool, Description("Cierra un ticket de clases una vez resuelto.")]
    public static string CerrarTicketClase(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return $"[CLASES] Ticket {ticketId} cerrado. Resolución: {resolucion}";
    }
}


// ============================================================
// TOOLS DEL AGENTE DE ACCIÓN — INSTRUCTORES
// ============================================================
[McpServerToolType]
public static class ToolsAccionInstructor
{
    [McpServerTool, Description("Asigna un instructor disponible a una clase que no tiene instructor asignado.")]
    public static string AsignarInstructor(
        [Description("Nombre o descripción de la clase sin instructor")]
        string clase)
    {
        return $"[INSTRUCTORES] Se asignó un instructor disponible a la clase '{clase}'. La agenda fue actualizada y los socios inscriptos fueron notificados.";
    }

    [McpServerTool, Description("Cierra un ticket de instructores una vez resuelto.")]
    public static string CerrarTicketInstructor(
        [Description("ID del ticket a cerrar")]
        string ticketId,
        [Description("Descripción de la resolución")]
        string resolucion)
    {
        return $"[INSTRUCTORES] Ticket {ticketId} cerrado. Resolución: {resolucion}";
    }
}


// ============================================================
// TOOLS DE KNOWLEDGE BASE — consulta a la API
// ============================================================
[McpServerToolType]
public class ToolsKnowledgeBase(IKnowledgeBaseApiService kbApi)
{
    [McpServerTool, Description("Analiza el problema del ticket consultando la base de conocimiento de la turnera y determina si el bot puede resolverlo o si hay que escalar. Retorna decision (continuar/pedir_mas_info/escalar), mensaje sugerido y criterios de escalación.")]
    public async Task<string> DiagnosticarProblema(
        [Description("Descripción del problema reportado por el socio")]
        string descripcion,
        [Description("Sistema afectado (ej: socios, turnos, pagos, clases, instructores)")]
        string sistema)
    {
        var resultado = await kbApi.DiagnosticarAsync(sistema, descripcion);

        if (resultado == null)
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                puedoResolver = false,
                decision = "escalar",
                confianza = "ninguna",
                mensajeSugerido = "No pude consultar la base de conocimiento. Se recomienda escalar a soporte de nivel 2."
            });

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            puedoResolver        = resultado.PuedoResolver,
            decision             = resultado.Decision,
            confianza            = resultado.Confianza,
            mensajeSugerido      = resultado.MensajeSugerido,
            criteriosEscalacion  = resultado.CriteriosEscalacion,
            accionesRecomendadas = resultado.AccionesRecomendadas,
            articuloId           = resultado.ArticleId,
            articuloCodigo       = resultado.ArticleCode
        });
    }

    [McpServerTool, Description("Consulta la base de conocimiento de la turnera para obtener síntomas, causa probable, acción recomendada y criterios de escalación del artículo más relevante.")]
    public async Task<string> ConsultarKB(
        [Description("Descripción del problema a buscar en la KB")]
        string descripcion,
        [Description("Sistema afectado (ej: socios, turnos, pagos, clases, instructores)")]
        string sistema)
    {
        var articulos = await kbApi.SearchAsync(descripcion, sistema);

        if (articulos.Count == 0)
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                encontrado = false,
                mensaje = "No se encontró un artículo en la KB para este problema. Se recomienda escalar a nivel 2."
            });

        var top = articulos[0];
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            encontrado           = true,
            articuloId           = top.ArticleId,
            articuloCodigo       = top.ArticleCode,
            descripcion          = top.Description,
            sintomas             = top.Symptoms,
            causaProbable        = top.ProbableCause,
            accionRecomendada    = top.RecommendedAction,
            datosRequeridos      = top.RequiredData,
            criteriosEscalacion  = top.EscalationCriteria,
            mensajeSugerido      = top.SuggestedUserMessage,
            confianza            = top.Confidence
        });
    }
}
