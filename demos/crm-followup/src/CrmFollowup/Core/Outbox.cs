using System.Text;

namespace CrmFollowup.Core;

public interface IEmailSender
{
    Task<string> SendAsync(OutboundMessage email, string from, CancellationToken ct = default);
}

/// <summary>
/// Simulated SMTP: writes each approved message as an RFC-822 .eml to an outbox folder.
/// Swap for SMTP or Microsoft Graph in a real build.
/// </summary>
public sealed class FileOutboxEmailSender(string outboxDir) : IEmailSender
{
    public async Task<string> SendAsync(OutboundMessage email, string from, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outboxDir);
        var safe = string.Concat(email.Subject.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        if (safe.Length > 60) safe = safe[..60];
        var path = Path.Combine(outboxDir, $"{email.Id}-{safe}.eml");
        var sb = new StringBuilder();
        sb.Append($"From: {from}\r\nTo: {email.To}\r\nSubject: {email.Subject}\r\nDate: {DateTime.UtcNow:R}\r\n");
        sb.Append("MIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n");
        sb.Append(email.Body.Replace("\n", "\r\n"));
        if (!email.Body.EndsWith('\n')) sb.Append("\r\n");
        await File.WriteAllTextAsync(path, sb.ToString(), ct);
        return path;
    }
}
