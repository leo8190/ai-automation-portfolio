using System.Text;

namespace QuoteInvoice.Core;

public interface IEmailSender
{
    Task<string> SendAsync(OutboundEmail email, CancellationToken ct = default);
}

/// <summary>
/// Simulated SMTP: writes each approved message as an RFC-822 .eml (with optional PDF)
/// to an outbox folder. Swap for SMTP / Microsoft Graph in a real build.
/// </summary>
public class FileOutboxEmailSender(string outboxDir, string from) : IEmailSender
{
    public async Task<string> SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outboxDir);
        var safe = string.Concat(email.Subject.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        var path = Path.Combine(outboxDir, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{safe[..Math.Min(60, safe.Length)]}.eml");
        var boundary = "=_boundary_" + Guid.NewGuid().ToString("N");
        var sb = new StringBuilder();
        sb.Append($"From: {from}\r\nTo: {email.To}\r\nSubject: {email.Subject}\r\nDate: {DateTime.UtcNow:R}\r\nMIME-Version: 1.0\r\n");
        sb.Append($"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n\r\n");
        sb.Append($"--{boundary}\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n{email.Body.Replace("\n", "\r\n")}\r\n");
        if (email.AttachmentPath is { } att && File.Exists(att))
        {
            var b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(att, ct), Base64FormattingOptions.InsertLineBreaks);
            sb.Append($"--{boundary}\r\nContent-Type: application/pdf; name=\"{Path.GetFileName(att)}\"\r\n");
            sb.Append($"Content-Transfer-Encoding: base64\r\nContent-Disposition: attachment; filename=\"{Path.GetFileName(att)}\"\r\n\r\n{b64}\r\n");
        }
        sb.Append($"--{boundary}--\r\n");
        await File.WriteAllTextAsync(path, sb.ToString(), ct);
        return path;
    }
}
