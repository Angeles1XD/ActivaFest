using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ActivaFest.Services.Email;

public class EmailSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "no-reply@activafest.com";
    public string FromName { get; set; } = "ActivaFest";
    public bool UseStartTls { get; set; } = true;

    public bool Configurado => !string.IsNullOrWhiteSpace(Host);
}

public record AdjuntoCorreo(string NombreArchivo, byte[] Contenido, string TipoContenido = "image/png");

public interface IEmailService
{
    Task EnviarAsync(string destinatario, string asunto, string html, IReadOnlyList<AdjuntoCorreo>? adjuntos = null);
}

// =============================
// ✉️ CORREO TRANSACCIONAL (HU15)
// =============================
// Envía por SMTP con MailKit (Gmail, SendGrid SMTP relay, Mailtrap, etc.).
// Si no hay SMTP configurado, registra el correo en el log para no romper el flujo en local.
public class SmtpEmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailSettings> settings, ILogger<SmtpEmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task EnviarAsync(string destinatario, string asunto, string html, IReadOnlyList<AdjuntoCorreo>? adjuntos = null)
    {
        if (!_settings.Configurado)
        {
            _logger.LogWarning("📧 SMTP no configurado. Correo NO enviado → Para: {Para} | Asunto: {Asunto} | Adjuntos: {Adjuntos}\n{Html}",
                destinatario, asunto, adjuntos?.Count ?? 0, html);
            return;
        }

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
        mensaje.To.Add(MailboxAddress.Parse(destinatario));
        mensaje.Subject = asunto;

        var cuerpo = new BodyBuilder { HtmlBody = html };
        foreach (var adjunto in adjuntos ?? Array.Empty<AdjuntoCorreo>())
            cuerpo.Attachments.Add(adjunto.NombreArchivo, adjunto.Contenido, ContentType.Parse(adjunto.TipoContenido));

        mensaje.Body = cuerpo.ToMessageBody();

        try
        {
            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_settings.Host, _settings.Port,
                _settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);

            if (!string.IsNullOrEmpty(_settings.User))
                await smtp.AuthenticateAsync(_settings.User, _settings.Password);

            await smtp.SendAsync(mensaje);
            await smtp.DisconnectAsync(true);

            _logger.LogInformation("📧 Correo enviado a {Para}: {Asunto}", destinatario, asunto);
        }
        catch (Exception ex)
        {
            // Un fallo de correo no debe tumbar la operación principal (registro, moderación, etc.)
            _logger.LogError(ex, "Error enviando correo a {Para}", destinatario);
        }
    }
}
