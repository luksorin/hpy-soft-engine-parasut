using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HPYSoftEngine.Models;

namespace HPYSoftEngine.Services;

public class ParasutService
{
    private readonly DatabaseService _db;
    private readonly HttpClient _http;
    private string? _token;
    private DateTime _tokenExpires = DateTime.MinValue;

    private static readonly List<ProcessedInvoice> DemoInvoices = new()
    {
        new() { ParasutInvoiceId="demo-001", OrderNo="405-7577504-5242722", PlatformName="Amazon Türkiye", Amount=1250m },
        new() { ParasutInvoiceId="demo-002", OrderNo="HB4734009737",         PlatformName="Hepsiburada",    Amount=789.5m },
        new() { ParasutInvoiceId="demo-003", OrderNo="HPY-20240315-001",     PlatformName="HPY Pazar",      Amount=450m },
        new() { ParasutInvoiceId="demo-004", OrderNo="1234567890",           PlatformName="Trendyol",       Amount=999.99m },
        new() { ParasutInvoiceId="demo-005", OrderNo="XYZ123",               PlatformName="BilinmeyenPlatform", Amount=150m },
    };

    public ParasutService(DatabaseService db) { _db = db; var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator }; _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) }; }

    private string CompanyId => _db.GetSetting("parasut_company_id");

    private async Task<string> GetToken()
    {
        if (_token != null && DateTime.Now < _tokenExpires.AddMinutes(-1)) return _token;
        var form = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"]    = "password",
            ["client_id"]     = _db.GetSetting("parasut_client_id"),
            ["client_secret"] = _db.GetSetting("parasut_client_secret"),
            ["username"]      = _db.GetSetting("parasut_username"),
            ["password"]      = _db.GetSetting("parasut_password"),
            ["redirect_uri"]  = "urn:ietf:wg:oauth:2.0:oob",
        });
        var resp = await _http.PostAsync("https://api.parasut.com/oauth/token", form);
        resp.EnsureSuccessStatusCode();
        var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
        _token = json["access_token"]!.ToString();
        _tokenExpires = DateTime.Now.AddSeconds((int)json["expires_in"]!);
        return _token;
    }

    private async Task<JObject> ApiGet(string path, Dictionary<string, string>? qs = null)
    {
        var token = await GetToken();
        var url = $"https://api.parasut.com/v4/{CompanyId}{path}";
        if (qs?.Count > 0) url += "?" + string.Join("&", qs.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            throw new Exception($"API Hatası {(int)resp.StatusCode}: {body[..Math.Min(300, body.Length)]}");
        }
        return JObject.Parse(await resp.Content.ReadAsStringAsync());
    }

    private async Task<JObject> ApiPost(string path, object payload)
    {
        var token = await GetToken();
        var url = $"https://api.parasut.com/v4/{CompanyId}{path}";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)resp.StatusCode}: {body[..Math.Min(400, body.Length)]}");
        return JObject.Parse(body.Length > 0 ? body : "{}");
    }

    public async Task<(bool Ok, string Message)> TestConnection()
    {
        if (string.IsNullOrEmpty(_db.GetSetting("parasut_client_id")))
            return (false, "API bilgileri eksik.");
        try { await GetToken(); return (true, "Bağlantı başarılı."); }
        catch (Exception e) { return (false, e.Message); }
    }

    public async Task<List<ProcessedInvoice>> GetInvoices(string dateFrom, string dateTo)
    {
        var results = new List<ProcessedInvoice>();
        var fromDate = DateTime.Parse(dateFrom);
        var toDate   = DateTime.Parse(dateTo).AddDays(1);
        int page = 1;
        while (true)
        {
            var data = await ApiGet("/sales_invoices", new() {
                ["page[size]"] = "25",
                ["page[number]"] = page.ToString(),
                ["sort"] = "-issue_date",
            });
            var items = data["data"] as JArray ?? new JArray();
            bool tooOld = false;
            foreach (var item in items)
            {
                var attrs  = item["attributes"]!;
                var issueDate = attrs["issue_date"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(issueDate))
                {
                    var d = DateTime.Parse(issueDate);
                    if (d < fromDate) { tooOld = true; break; }
                    if (d > toDate) continue;
                }
                var desc = attrs["description"]?.ToString() ?? "";
                var (prefix, orderNo) = ParsePrefix(desc);
                // remaining = kalan tahsil edilecek tutar (KDV dahil)
                decimal amount = 0;
                foreach (var field in new[]{"remaining","net_total","gross_total","total"})
                {
                    var val = attrs[field];
                    if (val != null && val.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                    {
                        var parsed = (decimal)val;
                        if (parsed > 0) { amount = parsed; break; }
                    }
                }
                results.Add(new ProcessedInvoice {
                    ParasutInvoiceId = item["id"]!.ToString(),
                    OrderNo  = orderNo,
                    PlatformName = prefix,
                    Amount   = amount,
                });
            }
            var totalPages = data["meta"]?["pagination"]?["total_pages"]?.Value<int>() ?? 1;
            if (page >= totalPages || tooOld) break;
            page++;
        }
        return results;
    }

    public async Task<List<ProcessedInvoice>> GetRecentInvoices(int hours = 2)
    {
        var dateFrom = DateTime.Now.AddHours(-hours).ToString("yyyy-MM-dd");
        var dateTo   = DateTime.Now.ToString("yyyy-MM-dd");
        return await GetInvoices(dateFrom, dateTo);
    }

    public async Task<List<(string Id, string Name, string Type)>> GetBankAccounts()
    {
        try
        {
            var results = new List<(string, string, string)>();
            int page = 1;
            while (true)
            {
                var data = await ApiGet("/accounts", new() {
                    ["page[size]"] = "100",
                    ["page[number]"] = page.ToString(),
                });
                var items = data["data"] as JArray ?? new JArray();
                foreach (var a in items)
                {
                    var name = a["attributes"]?["name"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(name))
                        results.Add((a["id"]!.ToString(), name, a["attributes"]?["account_type"]?.ToString() ?? ""));
                }
                var totalPages = data["meta"]?["pagination"]?["total_pages"]?.Value<int>() ?? 1;
                if (page >= totalPages) break;
                page++;
            }
            return results;
        }
        catch (Exception e)
        {
            throw new Exception($"Hesaplar alınamadı: {e.Message}");
        }
    }

    public async Task<List<(string Id, string Name)>> GetCategories()
    {
        try
        {
            var results = new List<(string, string)>();
            int page = 1;
            while (true)
            {
                var data = await ApiGet("/item_categories", new() {
                    ["page[size]"] = "100",
                    ["page[number]"] = page.ToString(),
                });
                var cats = data["data"] as JArray ?? new JArray();
                foreach (var c in cats)
                {
                    var name = c["attributes"]?["name"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(name))
                        results.Add((c["id"]!.ToString(), name));
                }
                var totalPages = data["meta"]?["pagination"]?["total_pages"]?.Value<int>() ?? 1;
                if (page >= totalPages) break;
                page++;
            }
            return results;
        }
        catch (Exception e)
        {
            throw new Exception($"Kategoriler alınamadı: {e.Message}");
        }
    }

    public async Task<byte[]?> GetInvoicePdf(string invoiceId)
    {
        try
        {
            // Faturanın aktif e-belgesini kontrol et
            var invData = await ApiGet($"/sales_invoices/{invoiceId}", new() { ["include"] = "active_e_document" });
            var included = invData["included"] as JArray ?? new JArray();
            var eDoc = included.FirstOrDefault();
            if (eDoc == null) return null;

            var docType = eDoc["type"]?.ToString() ?? "";
            var docId   = eDoc["id"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(docId)) return null;

            // PDF URL'sini al
            string pdfEndpoint = docType == "e_invoices"
                ? $"/e_invoices/{docId}/pdf"
                : $"/e_archives/{docId}/pdf";

            var token = await GetToken();
            var url = $"https://api.parasut.com/v4/{CompanyId}{pdfEndpoint}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/pdf"));

            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";

            // PDF direkt binary olarak geldiyse
            if (contentType.Contains("pdf"))
                return await resp.Content.ReadAsByteArrayAsync();

            // JSON içinde URL varsa
            var body = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(body)) return null;
            var json = Newtonsoft.Json.Linq.JObject.Parse(body);
            var pdfUrl = json["data"]?["attributes"]?["url"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(pdfUrl)) return null;

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            return await httpClient.GetByteArrayAsync(pdfUrl);
        }
        catch (Exception ex)
        {
            _db.AddLog("warn", $"PDF indirilemedi fatura={invoiceId}: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ArchiveInvoice(string invoiceId)
    {
        try
        {
            // Müşterinin e-fatura mükellefi olup olmadığını kontrol et
            var invData = await ApiGet($"/sales_invoices/{invoiceId}", new() { ["include"] = "contact" });
            var included = invData["included"] as JArray ?? new JArray();
            var contact  = included.FirstOrDefault(i => i["type"]?.ToString() == "contacts");
            var taxNumber = contact?["attributes"]?["tax_number"]?.ToString() ?? "";

            if (!string.IsNullOrEmpty(taxNumber))
            {
                // E-fatura mükellefi mi kontrol et
                try
                {
                    var inbox = await ApiGet("/e_invoice_inboxes", new() { ["filter[vkn]"] = taxNumber });
                    var inboxData = inbox["data"] as JArray ?? new JArray();
                    if (inboxData.Count > 0)
                    {
                        var eInvAddress = inboxData[0]?["attributes"]?["e_invoice_address"]?.ToString() ?? "";
                        await ApiPost($"/e_invoices", new
                        {
                            data = new
                            {
                                type = "e_invoices",
                                attributes = new { scenario = "basic", to = eInvAddress },
                                relationships = new
                                {
                                    invoice = new { data = new { id = invoiceId, type = "sales_invoices" } }
                                }
                            }
                        });
                        _db.AddLog("ok", $"E-Fatura oluşturuldu: {invoiceId}");
                        return true;
                    }
                }
                catch { }
            }

            // E-arşiv oluştur
            await ApiPost($"/e_archives", new
            {
                data = new
                {
                    type = "e_archives",
                    relationships = new
                    {
                        sales_invoice = new { data = new { id = invoiceId, type = "sales_invoices" } }
                    }
                }
            });
            _db.AddLog("ok", $"E-Arşiv oluşturuldu: {invoiceId}");
            return true;
        }
        catch (Exception e)
        {
            throw new Exception($"Resmileştirme hatası fatura={invoiceId}: {e.Message}");
        }
    }

    public async Task<bool> SetCategoryById(string invoiceId, string categoryId)
    {
        try
        {
            var token = await GetToken();
            var url = $"https://api.parasut.com/v4/{CompanyId}/sales_invoices/{invoiceId}";
            var req = new HttpRequestMessage(HttpMethod.Patch, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var payload = new
            {
                data = new
                {
                    id = invoiceId,
                    type = "sales_invoices",
                    attributes = new { },
                    relationships = new
                    {
                        category = new { data = new { id = categoryId, type = "item_categories" } }
                    }
                }
            };
            req.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"HTTP {(int)resp.StatusCode}: {body[..Math.Min(300, body.Length)]}");
            }
            return true;
        }
        catch (Exception e)
        {
            throw new Exception($"SetCategory hatası fatura={invoiceId}: {e.Message}");
        }
    }

    public async Task<bool> SetCategory(string invoiceId, string categoryName)
    {
        try
        {
            // Önce tüm kategorileri çek, ada göre bul
            var cats = await ApiGet("/sales_invoice_categories");
            var catData = cats["data"] as JArray ?? new JArray();
            var cat = catData.FirstOrDefault(c =>
                string.Equals(c["attributes"]?["name"]?.ToString(), categoryName, StringComparison.OrdinalIgnoreCase));

            if (cat == null)
                throw new Exception($"Kategori bulunamadı: '{categoryName}'. Paraşüt > Satışlar > Kategoriler bölümündeki isimle birebir aynı olmalı.");

            var catId = cat["id"]!.ToString();
            var token = await GetToken();
            var url = $"https://api.parasut.com/v4/{CompanyId}/sales_invoices/{invoiceId}";
            var req = new HttpRequestMessage(HttpMethod.Patch, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var payload = new
            {
                data = new
                {
                    id = invoiceId,
                    type = "sales_invoices",
                    attributes = new { },
                    relationships = new
                    {
                        category = new { data = new { id = catId, type = "item_categories" } }
                    }
                }
            };
            req.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"HTTP {(int)resp.StatusCode}: {body[..Math.Min(300, body.Length)]}");
            }
            return true;
        }
        catch (Exception e)
        {
            throw new Exception($"SetCategory hatası fatura={invoiceId}: {e.Message}");
        }
    }

    public async Task<bool> AddPayment(string invoiceId, string accountParasutId, decimal amount, string date)
    {
        try
        {
            // Paraşüt payment payload — relationships gereksiz, sadece attributes yeterli
            var payload = new
            {
                data = new
                {
                    type = "payments",
                    attributes = new
                    {
                        account_id = int.Parse(accountParasutId),
                        date,
                        amount = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        currency = "TRL",
                        notes = "Otomatik tahsilat - HPY Soft-Engine",
                    }
                }
            };
            await ApiPost($"/sales_invoices/{invoiceId}/payments", payload);
            return true;
        }
        catch (Exception e)
        {
            throw new Exception($"AddPayment hatası fatura={invoiceId}: {e.Message}");
        }
    }

    public static (string Prefix, string OrderNo) ParsePrefix(string desc)
    {
        var idx = desc.IndexOf(" - ", StringComparison.Ordinal);
        return idx < 0 ? (desc, "") : (desc[..idx].Trim(), desc[(idx + 3)..].Trim());
    }

    /// <summary>
    /// 429 Too Many Requests durumunda otomatik bekleyip tekrar dener.
    /// </summary>
    private static async Task RetryAsync(Func<Task> action, int maxRetries = 3)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (ex.Message.Contains("429") && i < maxRetries - 1)
            {
                // "Try again in N seconds" mesajından bekleme süresini çıkart
                int wait = 3;
                var match = System.Text.RegularExpressions.Regex.Match(ex.Message, @"Try again in (\d+) seconds");
                if (match.Success) wait = int.Parse(match.Groups[1].Value) + 1;
                await Task.Delay(wait * 1000);
            }
        }
        await action(); // Son deneme — hata fırlatsın
    }

    private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetries = 3)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try { return await action(); }
            catch (Exception ex) when (ex.Message.Contains("429") && i < maxRetries - 1)
            {
                int wait = 3;
                var match = System.Text.RegularExpressions.Regex.Match(ex.Message, @"Try again in (\d+) seconds");
                if (match.Success) wait = int.Parse(match.Groups[1].Value) + 1;
                await Task.Delay(wait * 1000);
            }
        }
        return await action();
    }

    public async Task<JobResult> ProcessInvoices(List<ProcessedInvoice> invoices, bool demo, bool forceRecheck)
    {
        var result = new JobResult();
        var platforms = _db.ListPlatforms();
        var today = DateTime.Now.ToString("yyyy-MM-dd");

        foreach (var inv in invoices)
        {
            var existing = _db.GetInvoice(inv.ParasutInvoiceId);
            if (!forceRecheck && existing != null) { result.Already++; continue; }
            if (forceRecheck && existing?.InvoiceCreated == true && existing?.PaymentCreated == true) { result.Already++; continue; }

            result.Total++;
            var platform = platforms.FirstOrDefault(p => p.Active && string.Equals(p.Prefix, inv.PlatformName, StringComparison.OrdinalIgnoreCase));

            if (platform == null)
            {
                _db.AddLog("warn", $"Eşleşme yok: '{inv.PlatformName}' — {inv.OrderNo}");
                _db.UpsertInvoice(inv with { Category = "—", Status = "err", AccountName = "—" });
                result.Err++; continue;
            }

            inv.Category    = platform.Category;
            inv.AccountName = platform.AccountName ?? "—";

            // Önceki durumları al
            bool categoryDone  = existing?.InvoiceCreated  ?? false;
            bool archiveDone   = existing?.InvoiceCreated  ?? false; // aynı flag — kategori+arşiv birlikte
            bool paymentDone   = existing?.PaymentCreated  ?? false;

            // ── ADIM 1: Kategori ata ──────────────────────────
            if (!categoryDone && !demo)
            {
                try
                {
                    if (!string.IsNullOrEmpty(platform.CategoryId))
                        await RetryAsync(() => SetCategoryById(inv.ParasutInvoiceId, platform.CategoryId));
                    else
                        await RetryAsync(() => SetCategory(inv.ParasutInvoiceId, platform.Category));
                    categoryDone = true;
                    _db.AddLog("info", $"Kategori atandı: {inv.OrderNo} → {platform.Category}");
                }
                catch (Exception ex)
                {
                    _db.AddLog("err", $"Kategori ataması başarısız: {inv.OrderNo} → {ex.Message}");
                }
            }
            else if (demo) categoryDone = true;

            // ── ADIM 2: Fatura kesilmeyecek kuralı ───────────
            if (!platform.InvoiceEnabled)
            {
                _db.AddLog("info", $"Atlandı (kural): {platform.Prefix} → {inv.OrderNo}");
                _db.UpsertInvoice(inv with { Status = "skip", InvoiceCreated = false });
                result.Skip++; continue;
            }

            // ── ADIM 3: Resmileştir (e-fatura veya e-arşiv) ──
            if (!archiveDone && !demo)
            {
                try
                {
                    await RetryAsync(() => ArchiveInvoice(inv.ParasutInvoiceId));
                    archiveDone = true;

                    // Resmileştirme başarılıysa ve platform mail açıksa otomatik mail at
                    if (platform.MailEnabled && _db.GetSetting("mail_auto_send","0") == "1")
                    {
                        try
                        {
                            var mailService = new MailService(_db);
                            var token = await GetToken();
                            var (contactEmail, contactName) = await mailService.GetContactEmail(inv.ParasutInvoiceId, token, CompanyId);
                            if (!string.IsNullOrEmpty(contactEmail))
                            {
                                // PDF'i Paraşüt'ten çek
                                await Task.Delay(2000); // E-belge oluşması için bekle
                                var pdfBytes = await GetInvoicePdf(inv.ParasutInvoiceId);

                                var (mailOk, mailMsg) = await mailService.SendInvoiceMail(contactEmail, inv.OrderNo, contactName, pdfBytes);
                                if (mailOk)
                                    _db.AddLog("ok", $"Mail gönderildi{(pdfBytes!=null?" (PDF ekli)":"")}: {inv.OrderNo} → {contactEmail}");
                                else
                                    _db.AddLog("warn", $"Mail gönderilemedi: {inv.OrderNo} → {mailMsg}");
                                _db.SaveMailLog(inv.ParasutInvoiceId, contactEmail, mailOk);
                            }
                            else
                                _db.AddLog("warn", $"Müşteri mail adresi bulunamadı: {inv.OrderNo}");
                        }
                        catch (Exception ex)
                        {
                            _db.AddLog("warn", $"Mail hatası: {inv.OrderNo} → {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _db.AddLog("warn", $"Resmileştirme başarısız: {inv.OrderNo} → {ex.Message}");
                }
            }
            else if (demo) archiveDone = true;

            // ── ADIM 4: Tahsilat ekle ─────────────────────────
            if (!paymentDone && !string.IsNullOrEmpty(platform.AccountParasutId))
            {
                if (demo) paymentDone = true;
                else
                {
                    try
                    {
                        await RetryAsync(() => AddPayment(inv.ParasutInvoiceId, platform.AccountParasutId, inv.Amount, today));
                        paymentDone = true;
                    }
                    catch (Exception ex)
                    {
                        _db.AddLog("err", $"Tahsilat hatası: {inv.OrderNo} → {ex.Message}");
                    }
                }
            }

            // Her fatura arasında bekle (rate limit)
            await Task.Delay(2000);

            bool allOk = categoryDone && archiveDone && (paymentDone || string.IsNullOrEmpty(platform.AccountParasutId));
            if (!allOk && !demo)
            {
                _db.AddLog("err", $"Eksik işlem: {platform.Prefix} → {inv.OrderNo} | Kategori:{categoryDone} Arşiv:{archiveDone} Tahsilat:{paymentDone}");
                _db.UpsertInvoice(inv with { InvoiceCreated = archiveDone, PaymentCreated = paymentDone, Status = "err" });
                result.Err++;
            }
            else
            {
                _db.AddLog("ok", $"Başarılı: {platform.Prefix} → {inv.OrderNo} | {platform.Category} | {inv.Amount:F2} TL");
                _db.UpsertInvoice(inv with { InvoiceCreated = archiveDone, PaymentCreated = paymentDone, Status = "ok" });
                result.Ok++;
            }
        }
        return result;
    }

    public async Task<JobResult> RunJob()
    {
        var hour = DateTime.Now.Hour;
        var startH = int.Parse(_db.GetSetting("job_start_hour", "9"));
        var endH   = int.Parse(_db.GetSetting("job_end_hour",   "23"));
        if (hour < startH || hour >= endH) { _db.AddLog("info", $"Çalışma saati dışı ({hour}:xx). Atlandı."); return new JobResult(); }

        var demo = _db.GetSetting("demo_mode", "1") == "1";
        _db.AddLog("info", $"Job başlatıldı. {(demo ? "[DEMO]" : "[CANLI]")}");

        List<ProcessedInvoice> invoices;
        if (demo)
            invoices = DemoInvoices;
        else
        {
            var hours = int.Parse(_db.GetSetting("lookback_hours", "2"));
            var from = DateTime.Now.AddHours(-hours).ToString("yyyy-MM-dd");
            var to   = DateTime.Now.ToString("yyyy-MM-dd");
            invoices = await GetInvoices(from, to);
        }

        var r = await ProcessInvoices(invoices, demo, false);
        _db.AddLog("info", $"Tamamlandı → {r.Total} işlendi, {r.Ok} başarılı, {r.Skip} atlandı, {r.Err} hata.");
        return r;
    }

    public async Task<JobResult> RunScan(string dateFrom, string dateTo)
    {
        var demo = _db.GetSetting("demo_mode", "1") == "1";
        _db.AddLog("info", $"Tarama başlatıldı: {dateFrom} → {dateTo} {(demo ? "[DEMO]" : "[CANLI]")}");
        var invoices = demo ? DemoInvoices : await GetInvoices(dateFrom, dateTo);
        var r = await ProcessInvoices(invoices, demo, true);
        _db.AddLog("info", $"Tarama tamamlandı → {r.Total} kontrol, {r.Ok} işlendi, {r.Skip} atlandı, {r.Err} hata, {r.Already} tamam.");
        return r;
    }
}
