using System.Net;
using ActivaFest.Models;

namespace ActivaFest.Services.Email;

// =============================
// 🎨 PLANTILLAS HTML DE CORREO (HU15)
// =============================
public static class EmailTemplates
{
    private static string Layout(string titulo, string contenido, string? botonTexto = null, string? botonUrl = null)
    {
        var boton = botonUrl == null ? "" : $@"
            <p style=""text-align:center;margin:30px 0;"">
                <a href=""{botonUrl}"" style=""background:linear-gradient(90deg,#ff4d6d,#ff006e);background-color:#ff006e;color:#ffffff;padding:12px 24px;border-radius:10px;text-decoration:none;font-weight:bold;"">{botonTexto}</a>
            </p>";

        return $@"<!DOCTYPE html>
<html lang=""es"">
<body style=""margin:0;padding:0;background:#f4f4f7;font-family:'Segoe UI',Arial,sans-serif;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f4f4f7;padding:30px 0;"">
    <tr><td align=""center"">
      <table width=""560"" cellpadding=""0"" cellspacing=""0"" style=""background:#ffffff;border-radius:14px;overflow:hidden;"">
        <tr><td style=""background:#0a0a1a;padding:20px 30px;color:#ff4d6d;font-size:22px;font-weight:bold;"">ActivaFest</td></tr>
        <tr><td style=""padding:30px;color:#333;font-size:15px;line-height:1.6;"">
          <h2 style=""margin-top:0;color:#111;"">{titulo}</h2>
          {contenido}
          {boton}
        </td></tr>
        <tr><td style=""background:#fafafa;padding:16px 30px;color:#999;font-size:12px;text-align:center;"">
          Este es un correo automático de ActivaFest. Por favor no respondas a este mensaje.
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";
    }

    private static string E(string? texto) => WebUtility.HtmlEncode(texto ?? "");

    public static string Bienvenida(string nombre, string rol, string urlLogin) =>
        Layout($"¡Bienvenido/a, {E(nombre)}!",
            $"<p>Tu cuenta en ActivaFest fue creada correctamente con el perfil <b>{E(rol)}</b>.</p>" +
            "<p>Ya puedes explorar eventos, y si eres organizador, publicar los tuyos.</p>",
            "Iniciar sesión", urlLogin);

    public static string EventoRecibido(Evento ev) =>
        Layout("Recibimos tu evento",
            $"<p>Tu evento <b>{E(ev.Titulo)}</b> fue registrado y está <b>pendiente de revisión</b> por nuestro equipo.</p>" +
            "<p>Te avisaremos por correo cuando sea aprobado o si necesita cambios.</p>");

    public static string EventoAprobado(Evento ev, string urlEvento) =>
        Layout("¡Tu evento fue aprobado! 🎉",
            $"<p>El evento <b>{E(ev.Titulo)}</b> ya está publicado en el catálogo de ActivaFest.</p>" +
            $"<p>📍 {E(ev.Ubicacion)}<br/>📅 {ev.Fecha:dd/MM/yyyy HH:mm}</p>",
            "Ver evento", urlEvento);

    public static string EventoRechazado(Evento ev, string urlMisEventos) =>
        Layout("Tu evento no fue aprobado",
            $"<p>Revisamos el evento <b>{E(ev.Titulo)}</b> y por ahora no puede publicarse.</p>" +
            $"<p><b>Motivo:</b> {E(ev.MotivoRechazo)}</p>" +
            "<p>Puedes corregir la información y volver a enviarlo.</p>",
            "Ir a mis eventos", urlMisEventos);

    public static string EntradasCompradas(string nombre, Orden orden, Evento evento, string urlMisEntradas)
    {
        var filas = string.Join("", orden.Items.Select(i =>
            $"<tr><td style=\"padding:6px 0;\">{i.Cantidad} × {E(i.TipoEntrada?.Nombre)}</td>" +
            $"<td style=\"padding:6px 0;text-align:right;\">{Formato.Precio(i.Subtotal)}</td></tr>"));

        return Layout("¡Tus entradas están listas! 🎟️",
            $"<p>Hola {E(nombre)}, tu compra para <b>{E(evento.Titulo)}</b> fue confirmada.</p>" +
            $"<p>📅 {Formato.Fecha(evento.Fecha, "dddd dd 'de' MMMM 'de' yyyy, HH:mm")}<br/>📍 {E(evento.Ubicacion)}</p>" +
            $"<table width=\"100%\" style=\"border-top:1px solid #eee;border-bottom:1px solid #eee;margin:15px 0;\">{filas}" +
            $"<tr><td style=\"padding:8px 0;\"><b>Total</b></td><td style=\"padding:8px 0;text-align:right;\"><b>{Formato.Precio(orden.Total)}</b></td></tr></table>" +
            $"<p>Adjuntamos <b>{orden.Entradas.Count}</b> {(orden.Entradas.Count == 1 ? "código QR" : "códigos QR")}, uno por persona. " +
            "Preséntalo en la puerta desde tu celular o impreso. Cada QR solo se puede usar una vez.</p>" +
            $"<p style=\"color:#888;font-size:13px;\">N.° de orden: {E(orden.Codigo)}</p>",
            "Ver mis entradas", urlMisEntradas);
    }

    public static string RecuperarContrasena(string nombre, string urlReset) =>
        Layout("Restablecer contraseña",
            $"<p>Hola {E(nombre)}, recibimos una solicitud para restablecer tu contraseña.</p>" +
            "<p>Si no fuiste tú, ignora este mensaje. El enlace caduca en unas horas.</p>",
            "Restablecer contraseña", urlReset);
}
