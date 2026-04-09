using MySqlConnector;
using Newtonsoft.Json;
using HPYSoftEngine.Models;

namespace HPYSoftEngine.Services;

/// <summary>
/// MySQL tabanlı veri depolama.
/// Bağlantı bilgileri AppData/config.json'dan okunur.
/// İlk çalışmada tablolar otomatik oluşturulur.
/// MySQL yoksa JSON dosya moduna düşer.
/// </summary>
public class DatabaseService
{
    private readonly string _configPath;
    private readonly string _dataDir;
    private DbConfig _cfg = new();
    private bool _mysqlOk = false;

    // JSON fallback
    private Dictionary<string, string> _settings = new();
    private List<Account> _accounts = new();
    private List<Platform> _platforms = new();
    private List<ProcessedInvoice> _invoices = new();
    private List<JobLog> _logs = new();
    private List<MailLogEntry> _mailLog = new();
    private int _nextAccountId=1,_nextPlatformId=1,_nextInvoiceId=1,_nextLogId=1,_nextMailId=1;

    public DatabaseService()
    {
        _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HPY-Soft-Engine");
        Directory.CreateDirectory(_dataDir);
        _configPath = Path.Combine(_dataDir, "config.json");
        LoadConfig();
        if (_cfg.UseMySQL && !string.IsNullOrEmpty(_cfg.Host))
            TryInitMySQL();
        if (!_mysqlOk)
            LoadJsonFallback();
        SeedDefaults();
    }

    // ── Config ───────────────────────────────────────────
    private void LoadConfig()
    {
        if (File.Exists(_configPath))
            try { _cfg = JsonConvert.DeserializeObject<DbConfig>(File.ReadAllText(_configPath)) ?? new(); } catch { }
    }

    public void SaveConfig(DbConfig cfg)
    {
        _cfg = cfg;
        File.WriteAllText(_configPath, JsonConvert.SerializeObject(cfg, Formatting.Indented));
    }

    public DbConfig GetConfig() => _cfg;
    public bool IsMySQLActive() => _mysqlOk;

    // ── MySQL Init ────────────────────────────────────────
    private void TryInitMySQL()
    {
        try
        {
            using var conn = OpenMySQL();
            conn.Open();
            CreateTables(conn);
            _mysqlOk = true;
        }
        catch (Exception ex)
        {
            _mysqlOk = false;
            // Hata log dosyasına yaz
            File.AppendAllText(Path.Combine(_dataDir, "db-error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} MySQL bağlantı hatası: {ex.Message}\n");
        }
    }

    public (bool Ok, string Message) TestMySQL(DbConfig cfg)
    {
        try
        {
            var cs = BuildConnStr(cfg);
            using var conn = new MySqlConnection(cs);
            conn.Open();
            return (true, $"Bağlantı başarılı! MySQL {conn.ServerVersion}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private string BuildConnStr(DbConfig cfg) =>
        $"Server={cfg.Host};Port={cfg.Port};Database={cfg.Database};Uid={cfg.Username};Pwd={cfg.Password};CharSet=utf8mb4;ConnectionTimeout=10;SslMode=None;";

    private MySqlConnection OpenMySQL() => new(BuildConnStr(_cfg));

    private void CreateTables(MySqlConnection conn)
    {
        var sqls = new[] {
            @"CREATE TABLE IF NOT EXISTS settings (
                `key` VARCHAR(100) PRIMARY KEY,
                `value` TEXT NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

            @"CREATE TABLE IF NOT EXISTS accounts (
                id INT AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(200) NOT NULL,
                parasut_id VARCHAR(50) NOT NULL,
                account_type VARCHAR(50) DEFAULT 'kasa',
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

            @"CREATE TABLE IF NOT EXISTS platforms (
                id INT AUTO_INCREMENT PRIMARY KEY,
                prefix VARCHAR(200) NOT NULL,
                category VARCHAR(200) NOT NULL DEFAULT '',
                category_id VARCHAR(50) DEFAULT '',
                invoice_enabled TINYINT(1) DEFAULT 1,
                account_id INT DEFAULT NULL,
                account_parasut_id VARCHAR(50) DEFAULT '',
                mail_enabled TINYINT(1) DEFAULT 0,
                active TINYINT(1) DEFAULT 1,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

            @"CREATE TABLE IF NOT EXISTS processed_invoices (
                id INT AUTO_INCREMENT PRIMARY KEY,
                parasut_invoice_id VARCHAR(50) NOT NULL,
                order_no VARCHAR(200) DEFAULT '',
                platform VARCHAR(200) DEFAULT '',
                category VARCHAR(200) DEFAULT '',
                invoice_created TINYINT(1) DEFAULT 0,
                payment_created TINYINT(1) DEFAULT 0,
                account_name VARCHAR(200) DEFAULT '',
                mail_sent TINYINT(1) DEFAULT 0,
                status VARCHAR(20) DEFAULT '',
                amount DECIMAL(12,2) DEFAULT 0,
                processed_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                UNIQUE KEY uq_parasut_id (parasut_invoice_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

            @"CREATE TABLE IF NOT EXISTS mail_log (
                id INT AUTO_INCREMENT PRIMARY KEY,
                parasut_invoice_id VARCHAR(50) DEFAULT '',
                order_no VARCHAR(200) DEFAULT '',
                platform VARCHAR(200) DEFAULT '',
                to_address VARCHAR(300) DEFAULT '',
                success TINYINT(1) DEFAULT 0,
                sent_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",

            @"CREATE TABLE IF NOT EXISTS job_logs (
                id INT AUTO_INCREMENT PRIMARY KEY,
                level VARCHAR(20) NOT NULL,
                message TEXT NOT NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
        };
        foreach (var sql in sqls)
        {
            using var cmd = new MySqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }
    }

    // ── JSON Fallback ─────────────────────────────────────
    private string P(string f) => Path.Combine(_dataDir, f);
    private T? LoadJson<T>(string f) { var p=P(f); if(!File.Exists(p)) return default; try{return JsonConvert.DeserializeObject<T>(File.ReadAllText(p));}catch{return default;} }
    private void SaveJson(string f, object d) { try{File.WriteAllText(P(f),JsonConvert.SerializeObject(d,Formatting.Indented));}catch{} }

    private void LoadJsonFallback()
    {
        _settings  = LoadJson<Dictionary<string,string>>("settings.json") ?? new();
        _accounts  = LoadJson<List<Account>>("accounts.json")             ?? new();
        _platforms = LoadJson<List<Platform>>("platforms.json")           ?? new();
        _invoices  = LoadJson<List<ProcessedInvoice>>("invoices.json")   ?? new();
        _logs      = LoadJson<List<JobLog>>("logs.json")                  ?? new();
        _mailLog   = LoadJson<List<MailLogEntry>>("maillog.json")         ?? new();
        if(_accounts.Count>0)  _nextAccountId  = _accounts.Max(x=>x.Id)+1;
        if(_platforms.Count>0) _nextPlatformId = _platforms.Max(x=>x.Id)+1;
        if(_invoices.Count>0)  _nextInvoiceId  = _invoices.Max(x=>x.Id)+1;
        if(_logs.Count>0)      _nextLogId      = _logs.Max(x=>x.Id)+1;
        if(_mailLog.Count>0)   _nextMailId     = _mailLog.Max(x=>x.Id)+1;
    }

    // ── Seed ─────────────────────────────────────────────
    private void SeedDefaults()
    {
        var defs = new Dictionary<string,string>{
            ["job_freq_minutes"]="60",["job_start_hour"]="9",["job_end_hour"]="23",
            ["job_active"]="1",["lookback_hours"]="2",["demo_mode"]="1",
            ["smtp_host"]="mail.natro.com",["smtp_port"]="587",["smtp_tls"]="1",
            ["smtp_from_name"]="Fatura Sistemi",["mail_auto_send"]="0",
            ["mail_subject"]="Faturanız hazır - {order_no}",
            ["mail_body"]="Sayın {customer_name},\n\n{order_no} numaralı siparişinize ait fatura ektedir.\n\nİyi günler.",
        };
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            foreach (var kv in defs)
            {
                using var cmd = new MySqlCommand("INSERT IGNORE INTO settings (`key`,`value`) VALUES (@k,@v)", conn);
                cmd.Parameters.AddWithValue("@k",kv.Key); cmd.Parameters.AddWithValue("@v",kv.Value);
                cmd.ExecuteNonQuery();
            }
            // Demo hesaplar
            using var cnt = new MySqlCommand("SELECT COUNT(*) FROM accounts", conn);
            if ((long)cnt.ExecuteScalar()! == 0)
            {
                var accs = new[]{
                    ("Pazaryeri Amazon Hesabı","11001001","kasa"),
                    ("Pazaryeri Hepsiburada Hesabı","11001002","kasa"),
                    ("Pazaryeri Trendyol Hesabı","11001003","banka"),
                    ("Pazaryeri N11 Hesabı","11001004","kasa"),
                };
                foreach (var (n,pid,t) in accs)
                {
                    using var c = new MySqlCommand("INSERT INTO accounts (name,parasut_id,account_type) VALUES (@n,@p,@t)", conn);
                    c.Parameters.AddWithValue("@n",n); c.Parameters.AddWithValue("@p",pid); c.Parameters.AddWithValue("@t",t);
                    c.ExecuteNonQuery();
                }
            }
        }
        else
        {
            bool ch=false;
            foreach (var kv in defs) if(!_settings.ContainsKey(kv.Key)){_settings[kv.Key]=kv.Value;ch=true;}
            if(ch) SaveJson("settings.json",_settings);
            if(_accounts.Count==0){
                _accounts.AddRange(new[]{
                    new Account{Id=1,Name="Pazaryeri Amazon Hesabı",     ParasutId="11001001",AccountType="kasa"},
                    new Account{Id=2,Name="Pazaryeri Hepsiburada Hesabı",ParasutId="11001002",AccountType="kasa"},
                    new Account{Id=3,Name="Pazaryeri Trendyol Hesabı",   ParasutId="11001003",AccountType="banka"},
                    new Account{Id=4,Name="Pazaryeri N11 Hesabı",        ParasutId="11001004",AccountType="kasa"},
                });
                _nextAccountId=5; SaveJson("accounts.json",_accounts);
            }
        }
    }

    // ── Settings ─────────────────────────────────────────
    public string GetSetting(string key, string def="")
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand("SELECT `value` FROM settings WHERE `key`=@k", conn);
            cmd.Parameters.AddWithValue("@k",key);
            var r = cmd.ExecuteScalar();
            return r is string s ? s : def;
        }
        return _settings.TryGetValue(key,out var v)?v:def;
    }

    public void SetSetting(string key, string value)
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand("INSERT INTO settings (`key`,`value`) VALUES (@k,@v) ON DUPLICATE KEY UPDATE `value`=@v", conn);
            cmd.Parameters.AddWithValue("@k",key); cmd.Parameters.AddWithValue("@v",value);
            cmd.ExecuteNonQuery();
        }
        else { _settings[key]=value; SaveJson("settings.json",_settings); }
    }

    // ── Platforms ─────────────────────────────────────────
    public List<Platform> ListPlatforms()
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand(
                "SELECT p.*, a.name as aname FROM platforms p LEFT JOIN accounts a ON p.account_id=a.id ORDER BY p.id", conn);
            using var r = cmd.ExecuteReader();
            var list = new List<Platform>();
            while (r.Read()) list.Add(ReadPlatform(r));
            return list;
        }
        return _platforms.Select(p=>{
            var acc=_accounts.FirstOrDefault(a=>a.Id==p.AccountId);
            return new Platform{Id=p.Id,Prefix=p.Prefix,Category=p.Category,CategoryId=p.CategoryId??"",
                InvoiceEnabled=p.InvoiceEnabled,AccountId=p.AccountId,AccountName=acc?.Name,
                AccountParasutId=p.AccountParasutId??acc?.ParasutId,MailEnabled=p.MailEnabled,Active=p.Active};
        }).OrderBy(p=>p.Id).ToList();
    }

    private Platform ReadPlatform(MySqlDataReader r) => new()
    {
        Id             = r.GetInt32("id"),
        Prefix         = r.GetString("prefix"),
        Category       = r.IsDBNull(r.GetOrdinal("category")) ? "" : r.GetString("category"),
        CategoryId     = r.IsDBNull(r.GetOrdinal("category_id")) ? "" : r.GetString("category_id"),
        InvoiceEnabled = r.GetBoolean("invoice_enabled"),
        AccountId      = r.IsDBNull(r.GetOrdinal("account_id")) ? null : r.GetInt32("account_id"),
        AccountParasutId = r.IsDBNull(r.GetOrdinal("account_parasut_id")) ? null : r.GetString("account_parasut_id"),
        MailEnabled    = r.GetBoolean("mail_enabled"),
        Active         = r.GetBoolean("active"),
        AccountName    = r.IsDBNull(r.GetOrdinal("aname")) ? null : r.GetString("aname"),
    };

    public void SavePlatform(Platform p)
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            if (p.Id > 0)
            {
                using var cmd = new MySqlCommand(
                    "UPDATE platforms SET prefix=@pr,category=@ca,category_id=@ci,invoice_enabled=@ie,account_id=@ai,account_parasut_id=@ap,mail_enabled=@me,active=@ac WHERE id=@id", conn);
                SetPlatformParams(cmd,p); cmd.Parameters.AddWithValue("@id",p.Id);
                cmd.ExecuteNonQuery();
            }
            else
            {
                using var cmd = new MySqlCommand(
                    "INSERT INTO platforms (prefix,category,category_id,invoice_enabled,account_id,account_parasut_id,mail_enabled,active) VALUES (@pr,@ca,@ci,@ie,@ai,@ap,@me,@ac)", conn);
                SetPlatformParams(cmd,p); cmd.ExecuteNonQuery();
            }
        }
        else
        {
            if(p.Id>0){var i=_platforms.FindIndex(x=>x.Id==p.Id);if(i>=0)_platforms[i]=p;}
            else{p.Id=_nextPlatformId++;_platforms.Add(p);}
            SaveJson("platforms.json",_platforms);
        }
    }

    private void SetPlatformParams(MySqlCommand cmd, Platform p)
    {
        cmd.Parameters.AddWithValue("@pr", p.Prefix);
        cmd.Parameters.AddWithValue("@ca", p.Category ?? "");
        cmd.Parameters.AddWithValue("@ci", p.CategoryId ?? "");
        cmd.Parameters.AddWithValue("@ie", p.InvoiceEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@ai", p.AccountId.HasValue ? (object)p.AccountId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@ap", p.AccountParasutId ?? "");
        cmd.Parameters.AddWithValue("@me", p.MailEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@ac", p.Active ? 1 : 0);
    }

    public void DeletePlatform(int id)
    {
        if (_mysqlOk) { using var conn=OpenMySQL();conn.Open();new MySqlCommand($"DELETE FROM platforms WHERE id={id}",conn).ExecuteNonQuery(); }
        else { _platforms.RemoveAll(p=>p.Id==id); SaveJson("platforms.json",_platforms); }
    }

    // ── Accounts ──────────────────────────────────────────
    public List<Account> ListAccounts()
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand("SELECT * FROM accounts ORDER BY id", conn);
            using var r = cmd.ExecuteReader();
            var list = new List<Account>();
            while (r.Read()) list.Add(new Account{Id=r.GetInt32("id"),Name=r.GetString("name"),ParasutId=r.GetString("parasut_id"),AccountType=r.GetString("account_type")});
            return list;
        }
        return _accounts.OrderBy(a=>a.Id).ToList();
    }

    public void SaveAccount(Account a)
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            if (a.Id > 0)
            {
                using var cmd = new MySqlCommand("UPDATE accounts SET name=@n,parasut_id=@p,account_type=@t WHERE id=@id",conn);
                cmd.Parameters.AddWithValue("@n",a.Name);cmd.Parameters.AddWithValue("@p",a.ParasutId);cmd.Parameters.AddWithValue("@t",a.AccountType);cmd.Parameters.AddWithValue("@id",a.Id);
                cmd.ExecuteNonQuery();
            }
            else
            {
                using var cmd = new MySqlCommand("INSERT INTO accounts (name,parasut_id,account_type) VALUES (@n,@p,@t)",conn);
                cmd.Parameters.AddWithValue("@n",a.Name);cmd.Parameters.AddWithValue("@p",a.ParasutId);cmd.Parameters.AddWithValue("@t",a.AccountType);
                cmd.ExecuteNonQuery();
            }
        }
        else
        {
            if(a.Id>0){var i=_accounts.FindIndex(x=>x.Id==a.Id);if(i>=0)_accounts[i]=a;}
            else{a.Id=_nextAccountId++;_accounts.Add(a);}
            SaveJson("accounts.json",_accounts);
        }
    }

    public void DeleteAccount(int id)
    {
        if (_mysqlOk)
        {
            using var conn=OpenMySQL();conn.Open();
            new MySqlCommand($"UPDATE platforms SET account_id=NULL WHERE account_id={id}",conn).ExecuteNonQuery();
            new MySqlCommand($"DELETE FROM accounts WHERE id={id}",conn).ExecuteNonQuery();
        }
        else
        {
            _accounts.RemoveAll(a=>a.Id==id);
            _platforms.Where(p=>p.AccountId==id).ToList().ForEach(p=>p.AccountId=null);
            SaveJson("accounts.json",_accounts); SaveJson("platforms.json",_platforms);
        }
    }

    // ── Invoices ──────────────────────────────────────────
    public List<ProcessedInvoice> ListInvoices(int limit=200)
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand($"SELECT * FROM processed_invoices ORDER BY processed_at DESC LIMIT {limit}",conn);
            using var r = cmd.ExecuteReader();
            var list = new List<ProcessedInvoice>();
            while (r.Read()) list.Add(ReadInvoice(r));
            return list;
        }
        return _invoices.OrderByDescending(i=>i.ProcessedAt).Take(limit).ToList();
    }

    private ProcessedInvoice ReadInvoice(MySqlDataReader r) => new()
    {
        Id               = r.GetInt32("id"),
        ParasutInvoiceId = r.GetString("parasut_invoice_id"),
        OrderNo          = r.IsDBNull(r.GetOrdinal("order_no")) ? "" : r.GetString("order_no"),
        PlatformName     = r.IsDBNull(r.GetOrdinal("platform")) ? "" : r.GetString("platform"),
        Category         = r.IsDBNull(r.GetOrdinal("category")) ? "" : r.GetString("category"),
        InvoiceCreated   = r.GetBoolean("invoice_created"),
        PaymentCreated   = r.GetBoolean("payment_created"),
        AccountName      = r.IsDBNull(r.GetOrdinal("account_name")) ? "" : r.GetString("account_name"),
        MailSent         = r.GetBoolean("mail_sent"),
        Status           = r.IsDBNull(r.GetOrdinal("status")) ? "" : r.GetString("status"),
        Amount           = r.IsDBNull(r.GetOrdinal("amount")) ? 0 : r.GetDecimal("amount"),
        ProcessedAt      = r.GetDateTime("processed_at"),
    };

    public TodayMetrics TodayMetrics()
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand(
                "SELECT COUNT(*) as total, SUM(status='ok') as ok, SUM(status='skip') as skip, SUM(status='err') as err FROM processed_invoices WHERE DATE(processed_at)=CURDATE()", conn);
            using var r = cmd.ExecuteReader();
            if (r.Read()) return new TodayMetrics{
                Total=r.IsDBNull(0)?0:(int)r.GetInt64(0),
                Ok   =r.IsDBNull(1)?0:(int)r.GetInt64(1),
                Skip =r.IsDBNull(2)?0:(int)r.GetInt64(2),
                Err  =r.IsDBNull(3)?0:(int)r.GetInt64(3),
            };
        }
        var t=DateTime.Today;var ts=_invoices.Where(i=>i.ProcessedAt.Date==t).ToList();
        return new TodayMetrics{Total=ts.Count,Ok=ts.Count(i=>i.Status=="ok"),Skip=ts.Count(i=>i.Status=="skip"),Err=ts.Count(i=>i.Status=="err")};
    }

    public void UpsertInvoice(ProcessedInvoice inv)
    {
        inv.ProcessedAt = DateTime.Now;
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand(@"INSERT INTO processed_invoices
                (parasut_invoice_id,order_no,platform,category,invoice_created,payment_created,account_name,mail_sent,status,amount,processed_at)
                VALUES (@pid,@on,@pl,@cat,@ic,@pc,@an,@ms,@st,@am,@pat)
                ON DUPLICATE KEY UPDATE
                order_no=@on,platform=@pl,category=@cat,invoice_created=@ic,payment_created=@pc,
                account_name=@an,mail_sent=@ms,status=@st,amount=@am,processed_at=@pat", conn);
            cmd.Parameters.AddWithValue("@pid",inv.ParasutInvoiceId);
            cmd.Parameters.AddWithValue("@on",inv.OrderNo);
            cmd.Parameters.AddWithValue("@pl",inv.PlatformName);
            cmd.Parameters.AddWithValue("@cat",inv.Category);
            cmd.Parameters.AddWithValue("@ic",inv.InvoiceCreated?1:0);
            cmd.Parameters.AddWithValue("@pc",inv.PaymentCreated?1:0);
            cmd.Parameters.AddWithValue("@an",inv.AccountName);
            cmd.Parameters.AddWithValue("@ms",inv.MailSent?1:0);
            cmd.Parameters.AddWithValue("@st",inv.Status);
            cmd.Parameters.AddWithValue("@am",(double)inv.Amount);
            cmd.Parameters.AddWithValue("@pat",inv.ProcessedAt);
            cmd.ExecuteNonQuery();
        }
        else
        {
            var i=_invoices.FindIndex(x=>x.ParasutInvoiceId==inv.ParasutInvoiceId);
            if(i>=0){inv.Id=_invoices[i].Id;_invoices[i]=inv;}else{inv.Id=_nextInvoiceId++;_invoices.Add(inv);}
            SaveJson("invoices.json",_invoices.TakeLast(2000).ToList());
        }
    }

    public ProcessedInvoice? GetInvoice(string id)
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand("SELECT * FROM processed_invoices WHERE parasut_invoice_id=@id",conn);
            cmd.Parameters.AddWithValue("@id",id);
            using var r = cmd.ExecuteReader();
            if (r.Read()) return ReadInvoice(r);
            return null;
        }
        return _invoices.FirstOrDefault(i=>i.ParasutInvoiceId==id);
    }

    // ── Mail Log ──────────────────────────────────────────
    public List<MailLogEntry> ListMailLog(int limit=100)
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand($"SELECT * FROM mail_log ORDER BY id DESC LIMIT {limit}",conn);
            using var r = cmd.ExecuteReader();
            var list = new List<MailLogEntry>();
            while (r.Read()) list.Add(new MailLogEntry{
                Id=r.GetInt32("id"),
                ParasutInvoiceId=r.GetString("parasut_invoice_id"),
                OrderNo=r.GetString("order_no"),
                Platform=r.GetString("platform"),
                ToAddress=r.GetString("to_address"),
                Success=r.GetBoolean("success"),
                SentAt=r.GetDateTime("sent_at"),
            });
            return list;
        }
        return _mailLog.OrderByDescending(m=>m.SentAt).Take(limit).ToList();
    }

    public void SaveMailLog(string parasutId, string to, bool ok)
    {
        var inv = GetInvoice(parasutId);
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand(
                "INSERT INTO mail_log (parasut_invoice_id,order_no,platform,to_address,success) VALUES (@pid,@on,@pl,@to,@ok)",conn);
            cmd.Parameters.AddWithValue("@pid",parasutId);
            cmd.Parameters.AddWithValue("@on",inv?.OrderNo??"");
            cmd.Parameters.AddWithValue("@pl",inv?.PlatformName??"");
            cmd.Parameters.AddWithValue("@to",to);
            cmd.Parameters.AddWithValue("@ok",ok?1:0);
            cmd.ExecuteNonQuery();
        }
        else
        {
            _mailLog.Add(new MailLogEntry{Id=_nextMailId++,ParasutInvoiceId=parasutId,OrderNo=inv?.OrderNo??"",Platform=inv?.PlatformName??"",ToAddress=to,Success=ok,SentAt=DateTime.Now});
            SaveJson("maillog.json",_mailLog.TakeLast(1000).ToList());
        }
    }

    public bool AlreadyMailed(string id)
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand("SELECT COUNT(*) FROM mail_log WHERE parasut_invoice_id=@id AND success=1",conn);
            cmd.Parameters.AddWithValue("@id",id);
            return (long)cmd.ExecuteScalar()! > 0;
        }
        return _mailLog.Any(m=>m.ParasutInvoiceId==id&&m.Success);
    }

    // ── Logs ──────────────────────────────────────────────
    public void AddLog(string level, string message)
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand("INSERT INTO job_logs (level,message) VALUES (@l,@m)",conn);
            cmd.Parameters.AddWithValue("@l",level); cmd.Parameters.AddWithValue("@m",message);
            cmd.ExecuteNonQuery();
            // Son 1000 logu tut
            new MySqlCommand("DELETE FROM job_logs WHERE id NOT IN (SELECT id FROM (SELECT id FROM job_logs ORDER BY id DESC LIMIT 1000) t)",conn).ExecuteNonQuery();
        }
        else
        {
            _logs.Add(new JobLog{Id=_nextLogId++,Level=level,Message=message,CreatedAt=DateTime.Now});
            if(_logs.Count>1000)_logs=_logs.TakeLast(1000).ToList();
            SaveJson("logs.json",_logs);
        }
    }

    public List<JobLog> ListLogs(int limit=300)
    {
        if (_mysqlOk)
        {
            using var conn = OpenMySQL(); conn.Open();
            using var cmd = new MySqlCommand($"SELECT * FROM job_logs ORDER BY id DESC LIMIT {limit}",conn);
            using var r = cmd.ExecuteReader();
            var list = new List<JobLog>();
            while (r.Read()) list.Add(new JobLog{Id=r.GetInt32("id"),Level=r.GetString("level"),Message=r.GetString("message"),CreatedAt=r.GetDateTime("created_at")});
            return list;
        }
        return _logs.OrderByDescending(l=>l.Id).Take(limit).ToList();
    }

    public void ClearLogs()
    {
        if (_mysqlOk) { using var conn=OpenMySQL();conn.Open();new MySqlCommand("DELETE FROM job_logs",conn).ExecuteNonQuery(); }
        else { _logs.Clear();_nextLogId=1;SaveJson("logs.json",_logs); }
    }
}

// ── Config Model ──────────────────────────────────────
public class DbConfig
{
    public bool UseMySQL { get; set; } = false;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class MailLogEntry
{
    public int Id { get; set; }
    public string ParasutInvoiceId { get; set; } = "";
    public string OrderNo { get; set; } = "";
    public string Platform { get; set; } = "";
    public string ToAddress { get; set; } = "";
    public bool Success { get; set; }
    public DateTime SentAt { get; set; }
}
