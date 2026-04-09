using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

namespace HPYSoftEngine.Services;

public class MailService
{
    private readonly DatabaseService _db;
    public MailService(DatabaseService db) => _db = db;

    public async Task<(bool Ok, string Message)> TestSmtp()
    {
        var user = _db.GetSetting("smtp_user");
        if (string.IsNullOrEmpty(user)) return (false, "SMTP bilgileri tanımlanmamış.");
        try
        {
            using var client = new SmtpClient();
            var tls = _db.GetSetting("smtp_tls", "1") == "1";
            await client.ConnectAsync(_db.GetSetting("smtp_host", "mail.natro.com"),
                int.Parse(_db.GetSetting("smtp_port", "587")),
                tls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(user, _db.GetSetting("smtp_password"));
            await client.DisconnectAsync(true);
            return (true, $"Bağlantı başarılı: {_db.GetSetting("smtp_host")}");
        }
        catch (Exception e) { return (false, e.Message); }
    }

    // Paraşüt'ten müşteri mail adresini çek
    public async Task<(string Email, string Name)> GetContactEmail(string invoiceId, string token, string companyId)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var url = $"https://api.parasut.com/v4/{companyId}/sales_invoices/{invoiceId}?include=contact";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return ("", "Müşteri");
            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            var included = json["included"] as JArray ?? new JArray();
            foreach (var item in included)
            {
                if (item["type"]?.ToString() == "contacts")
                {
                    var email = item["attributes"]?["email"]?.ToString() ?? "";
                    var name  = item["attributes"]?["name"]?.ToString()  ?? "Müşteri";
                    return (email, name);
                }
            }
            // included'da yoksa attributes'dan dene
            var attrs = json["data"]?["attributes"];
            return (attrs?["contact_email"]?.ToString() ?? "", attrs?["contact_name"]?.ToString() ?? "Müşteri");
        }
        catch { return ("", "Müşteri"); }
    }

    public async Task<(bool Ok, string Message)> SendInvoiceMail(
        string toAddress, string orderNo, string customerName, byte[]? pdfBytes = null)
    {
        if (string.IsNullOrEmpty(toAddress))
            return (false, "Mail adresi boş.");

        var fromAddr = _db.GetSetting("smtp_from") is string f && f.Length > 0 ? f : _db.GetSetting("smtp_user");
        var fromName = _db.GetSetting("smtp_from_name", "Fatura Sistemi");
        var subject  = _db.GetSetting("mail_subject", "Faturanız hazır - {order_no}")
                          .Replace("{order_no}", orderNo).Replace("{customer_name}", customerName);
        var body     = _db.GetSetting("mail_body", "")
                          .Replace("{order_no}", orderNo).Replace("{customer_name}", customerName);

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(fromName, fromAddr));
        msg.To.Add(MailboxAddress.Parse(toAddress));
        msg.Subject = subject;

        var builder = new BodyBuilder { TextBody = body };
        if (pdfBytes != null)
            builder.Attachments.Add($"fatura-{orderNo}.pdf", pdfBytes, new ContentType("application", "pdf"));
        msg.Body = builder.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            var tls = _db.GetSetting("smtp_tls", "1") == "1";
            await client.ConnectAsync(_db.GetSetting("smtp_host", "mail.natro.com"),
                int.Parse(_db.GetSetting("smtp_port", "587")),
                tls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(_db.GetSetting("smtp_user"), _db.GetSetting("smtp_password"));
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            return (true, "Mail gönderildi.");
        }
        catch (Exception e) { return (false, e.Message); }
    }
}
