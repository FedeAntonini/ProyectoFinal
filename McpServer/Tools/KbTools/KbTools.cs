using ModelContextProtocol.Server;
using System.ComponentModel;
using McpServer.Api.Kb;

namespace McpServer.Tools;

[McpServerToolType]
public class KbTools
{
    [McpServerTool, Description("Busca en la base de conocimiento una solución aplicable para el problema y sistema del ticket.")]
    public string BuscarKb(
        [Description("Descripción del problema del ticket")]
        string descripcion,
        [Description("Sistema afectado: acceso, turnos, pagos, disponibilidad")]
        string sistema)
    {
        var resultado = MarkdownKnowledgeBase.Search($"{sistema} {descripcion}", sistema);

        if (resultado is null)
            return "No se encontró un artículo de KB aplicable. Se recomienda escalar.";

        return System.Text.Json.JsonSerializer.Serialize(resultado, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
    }

    [McpServerTool, Description("Trae todos los articulos de la base de conocimiento")]
    public string GetAllArticles()
    {
        var resultado = MarkdownKnowledgeBase.GetAllArticles();
        if (resultado is null)
            return "No se encontró un artículo de KB aplicable. Se recomienda escalar.";

        return System.Text.Json.JsonSerializer.Serialize(resultado, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
    }
}