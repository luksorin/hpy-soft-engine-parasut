using System.Drawing.Drawing2D;
using HPYSoftEngine.Models;
using HPYSoftEngine.Services;

namespace HPYSoftEngine.Forms;

public partial class MainForm : Form
{
    private readonly DatabaseService _db;
    private readonly ParasutService  _parasut;
    private readonly MailService     _mail;
    private System.Windows.Forms.Timer _jobTimer = new();
    private System.Windows.Forms.Timer _uiTimer  = new();
    private NotifyIcon _trayIcon = new();
    private bool _jobRunning = false;

    // Colors — HPY Pazar logosundan alınan renkler
    static readonly Color BG     = Color.FromArgb(10,12,22);
    static readonly Color BG2    = Color.FromArgb(16,18,30);
    static readonly Color BG3    = Color.FromArgb(22,25,42);
    static readonly Color Border = Color.FromArgb(30,35,60);
    static readonly Color Accent = Color.FromArgb(0,174,239);    // Logo cyan #00AEEF
    static readonly Color Navy   = Color.FromArgb(43,45,158);    // Logo lacivert #2B2D9E
    static readonly Color Green  = Color.FromArgb(32,191,107);
    static readonly Color Red    = Color.FromArgb(235,59,90);
    static readonly Color Amber  = Color.FromArgb(247,183,49);
    static readonly Color Teal   = Color.FromArgb(15,185,177);
    static readonly Color FgPri  = Color.FromArgb(221,225,237);
    static readonly Color FgSec  = Color.FromArgb(120,128,160);
    static readonly Color FgDim  = Color.FromArgb(61,66,96);

    // Logo
    private static Image? _logo;
    private static Image? GetLogo()
    {
        if (_logo != null) return _logo;
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("hpy_logo.png"));
            if (name != null)
            {
                using var stream = asm.GetManifestResourceStream(name)!;
                _logo = Image.FromStream(stream);
            }
        }
        catch { }
        return _logo;
    }

    // Controls
    private Panel _sidebar = new(), _mainArea = new(), _titleBar = new();
    private Panel _pagePanel = new();
    private Label _pageTitle = new(), _pageSub = new();
    private Panel _topBar = new();

    // Pages
    private Panel _pgDashboard = new(), _pgInvoices = new(), _pgLog = new();
    private Panel _pgPlatforms = new(), _pgAccounts = new();
    private Panel _pgSettingsParasut = new(), _pgSettingsJob = new(), _pgSettingsMail = new();
    private Panel _pgSettingsDb = new();

    // DB Settings fields
    private CheckBox _chkUseMySQL = new();
    private TextBox _tDbHost=new(), _tDbName=new(), _tDbUser=new(), _tDbPass=new();
    private NumericUpDown _numDbPort = new();
    private Label _lblDbTest = new(), _lblDbStatus = new();

    // Sidebar labels
    private Label _lblJobStatus = new(), _lblNextRun = new();

    // Dashboard
    private Label _mTotal = new(), _mOk = new(), _mSkip = new(), _mErr = new();
    private DataGridView _dgDash = new();
    private DateTimePicker _dtFrom = new(), _dtTo = new();
    private Button _btnScan = new(), _btnRun = new(), _btnToggle = new();
    private Label _lblScanResult = new();

    // Invoices
    private DataGridView _dgInvoices = new();

    // Log
    private RichTextBox _rtbLog = new();

    // Platforms
    private DataGridView _dgPlatforms = new();

    // Accounts
    private DataGridView _dgAccounts = new();

    // Settings fields
    private TextBox _tClientId=new(),_tClientSecret=new(),_tUsername=new(),_tPassword=new(),_tCompanyId=new();
    private CheckBox _chkDemoMode=new();
    private NumericUpDown _numFreq=new(),_numStart=new(),_numEnd=new(),_numLookback=new();
    private TextBox _tSmtpHost=new(),_tSmtpUser=new(),_tSmtpPass=new(),_tSmtpFrom=new(),_tSmtpFromName=new();
    private NumericUpDown _numSmtpPort=new();
    private ComboBox _cmbTls=new();
    private TextBox _tMailSubject=new();
    private RichTextBox _rtbMailBody=new();
    private CheckBox _chkAutoMail=new();
    private Label _lblParasutTest=new(), _lblSmtpTest=new();

    public MainForm()
    {
        _db      = new DatabaseService();
        _parasut = new ParasutService(_db);
        _mail    = new MailService(_db);
        BuildUI();
        SetupTray();
        SetupJobTimer();
        SetupUiTimer();
        LoadDashboard();
    }

    // ── UI Build ─────────────────────────────────────────
    void BuildUI()
    {
        Text = "HPY Soft-Engine";
        Size = new Size(1280, 800);
        MinimumSize = new Size(900, 600);
        BackColor = BG;
        ForeColor = FgPri;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;

        // Titlebar — koyu
        _titleBar = MakePanel(BG2, new Rectangle(0, 0, Width, 38));
        _titleBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _titleBar.BorderStyle = BorderStyle.None;
        _titleBar.Paint += (s, e) => {
            using var brush = new LinearGradientBrush(
                new Rectangle(0, 0, _titleBar.Width, _titleBar.Height),
                BG2, Color.FromArgb(22,25,50), 0f);
            e.Graphics.FillRectangle(brush, 0, 0, _titleBar.Width, _titleBar.Height);
        };
        AddLabel(_titleBar, "HPY", new Rectangle(12,8,36,22), Color.White, 10f, FontStyle.Bold, new SolidBrush(Accent));
        AddLabel(_titleBar, "Soft-Engine", new Rectangle(54,10,130,18), FgPri, 10f, FontStyle.Bold);
        AddLabel(_titleBar, "/ Paraşüt Kasa Entegrasyonu", new Rectangle(190,12,280,16), FgSec, 8.5f);

        var btnMin  = MakeTitleBtn("─", new Rectangle(Width-114,0,38,38), (s,e) => WindowState = FormWindowState.Minimized);
        var btnMax  = MakeTitleBtn("□", new Rectangle(Width-76,0,38,38),  (s,e) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized);
        var btnClose= MakeTitleBtn("✕", new Rectangle(Width-38,0,38,38),  (s,e) => { Hide(); });
        btnClose.FlatAppearance.MouseOverBackColor = Red;
        _titleBar.Controls.AddRange(new Control[]{btnMin,btnMax,btnClose});

        // Drag
        bool drag=false; Point dragPt=default;
        _titleBar.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){drag=true;dragPt=e.Location;}};
        _titleBar.MouseMove+=(s,e)=>{if(drag){Location=new Point(Location.X+e.X-dragPt.X,Location.Y+e.Y-dragPt.Y);}};
        _titleBar.MouseUp+=(s,e)=>drag=false;

        // Shell
        _sidebar = MakePanel(BG2, new Rectangle(0,38,200,Height-38));
        _sidebar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;
        _sidebar.Paint += (s,e) => {
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0,0,_sidebar.Width,_sidebar.Height),
                Color.FromArgb(16,18,30), Color.FromArgb(18,20,40),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, 0, 0, _sidebar.Width, _sidebar.Height);
            using var pen = new Pen(Color.FromArgb(43,45,158,60));
            e.Graphics.DrawLine(pen, _sidebar.Width-1, 0, _sidebar.Width-1, _sidebar.Height);
        };
        BuildSidebar();

        _mainArea = MakePanel(BG, new Rectangle(200,38,Width-200,Height-38));
        _mainArea.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        // Topbar
        _topBar = MakePanel(BG2, new Rectangle(0,0,_mainArea.Width,50));
        _topBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _pageTitle = AddLabel(_topBar, "Dashboard", new Rectangle(16,8,400,20), FgPri, 12f, FontStyle.Bold);
        _pageSub   = AddLabel(_topBar, "Bugünün işlem özeti", new Rectangle(16,28,400,16), FgSec, 8.5f);

        // Topbar actions
        _btnToggle = MakeButton("⏸ Durdur", new Rectangle(_mainArea.Width-220,10,100,30), BG3, FgSec);
        _btnToggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnToggle.Click += async (s,e) => await ToggleJob();
        _btnRun = MakeButton("▶ Şimdi Çalıştır", new Rectangle(_mainArea.Width-110,10,100,30), Accent, Color.White);
        _btnRun.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnRun.Click += async (s,e) => await RunJobNow();
        _topBar.Controls.AddRange(new Control[]{_btnToggle,_btnRun});

        // Page panel
        _pagePanel = MakePanel(BG, new Rectangle(0,50,_mainArea.Width,_mainArea.Height-50));
        _pagePanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _pagePanel.Padding = new Padding(16);
        _pagePanel.AutoScroll = true;

        _mainArea.Controls.AddRange(new Control[]{_topBar,_pagePanel});

        BuildDashboardPage();
        BuildInvoicesPage();
        BuildLogPage();
        BuildPlatformsPage();
        BuildAccountsPage();
        BuildSettingsParasutPage();
        BuildSettingsJobPage();
        BuildSettingsMailPage();
        BuildSettingsDbPage();

        Controls.AddRange(new Control[]{_titleBar,_sidebar,_mainArea});
        Resize += (s,e) => {
            _sidebar.Height = Height-38;
            _mainArea.Width = Width-200; _mainArea.Height = Height-38;
            _topBar.Width = _mainArea.Width;
            _pagePanel.Width = _mainArea.Width; _pagePanel.Height = _mainArea.Height-50;
            _btnToggle.Left = _mainArea.Width-220;
            _btnRun.Left    = _mainArea.Width-110;
            _titleBar.Width = Width;
            btnMin.Left=Width-114; btnMax.Left=Width-76; btnClose.Left=Width-38;
        };

        ShowPage(_pgDashboard, "Dashboard", "Bugünün işlem özeti", true);
    }

    void BuildSidebar()
    {
        int y = 16;
        SidebarLabel("PARAŞÜT KASA", ref y);
        SidebarItem("◈  Dashboard",  ref y, ()=>{ ShowPage(_pgDashboard,"Dashboard","Bugünün işlem özeti",true); LoadDashboard(); });
        SidebarItem("▤  Fatura Listesi", ref y, ()=>{ ShowPage(_pgInvoices,"Fatura Listesi","Tüm işlenmiş faturalar",false); LoadInvoices(); });
        SidebarItem("≡  İşlem Logu",  ref y, ()=>{ ShowPage(_pgLog,"İşlem Logu","Tüm job kayıtları",false); LoadLogs(); });
        y += 8;
        SidebarLabel("BAKIM", ref y);
        SidebarItem("◼  Platformlar",  ref y, ()=>{ ShowPage(_pgPlatforms,"Platformlar","Sipariş ön eki eşleşme tablosu",false); LoadPlatforms(); });
        SidebarItem("◎  Kasa/Hesaplar",ref y, ()=>{ ShowPage(_pgAccounts,"Kasa / Hesap Tanımları","Paraşüt hesap ID eşleşmeleri",false); LoadAccounts(); });
        y += 8;
        SidebarLabel("AYARLAR", ref y);
        SidebarItem("⚙  Paraşüt API", ref y, ()=>{ ShowPage(_pgSettingsParasut,"Paraşüt API Ayarları","OAuth2 bağlantı bilgileri",false); LoadParasutSettings(); });
        SidebarItem("◷  Zamanlama",   ref y, ()=>{ ShowPage(_pgSettingsJob,"Zamanlama Ayarları","Job çalışma zamanı",false); LoadJobSettings(); });
        SidebarItem("✉  Mail",        ref y, ()=>{ ShowPage(_pgSettingsMail,"Mail Ayarları","SMTP ve fatura mail şablonu",false); LoadMailSettings(); });
        SidebarItem("🗄  Veritabanı",  ref y, ()=>{ ShowPage(_pgSettingsDb,"Veritabanı Ayarları","MySQL bağlantı bilgileri",false); LoadDbSettings(); });

        // Status card
        var card = MakePanel(BG3, new Rectangle(8, _sidebar.Height-90, 184, 60));
        card.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _lblJobStatus = AddLabel(card, "● Aktif", new Rectangle(8,8,160,16), Green, 8.5f, FontStyle.Bold);
        _lblNextRun   = AddLabel(card, "Sonraki: —", new Rectangle(8,28,160,16), FgSec, 8f);
        _sidebar.Controls.Add(card);

        // Powered by Luksorin
        var lblPowered = new Label {
            Text = "powered by Luksorin",
            Location = new Point(0, _sidebar.Height-24),
            Size = new Size(200, 20),
            ForeColor = Color.FromArgb(50,60,90),
            Font = new Font("Segoe UI", 7f, FontStyle.Italic),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Cursor = Cursors.Hand,
        };
        lblPowered.Click += (s,e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://luksorin.com") { UseShellExecute = true });
        _sidebar.Controls.Add(lblPowered);
    }

    void SidebarLabel(string text, ref int y)
    {
        var lbl = new Label { Text=text, Location=new Point(12,y), Size=new Size(176,14), ForeColor=FgDim, Font=new Font("Consolas",7.5f,FontStyle.Regular), BackColor=Color.Transparent };
        _sidebar.Controls.Add(lbl); y += 20;
    }

    private Button? _activeSideBtn;
    void SidebarItem(string text, ref int y, Action onClick)
    {
        var btn = new Button { Text=text, Location=new Point(0,y), Size=new Size(200,28), FlatStyle=FlatStyle.Flat, BackColor=Color.Transparent, ForeColor=FgSec, Font=new Font("Segoe UI",9f), TextAlign=ContentAlignment.MiddleLeft, Padding=new Padding(12,0,0,0), Cursor=Cursors.Hand };
        btn.FlatAppearance.BorderSize=0; btn.FlatAppearance.MouseOverBackColor=Color.FromArgb(25,255,255,255);
        btn.Click+=(s,e)=>{
            if(_activeSideBtn!=null){_activeSideBtn.BackColor=Color.Transparent;_activeSideBtn.ForeColor=FgSec;}
            btn.BackColor=Color.FromArgb(25,0,174,239); btn.ForeColor=Accent;
            _activeSideBtn=btn; onClick();
        };
        _sidebar.Controls.Add(btn); y+=28;
    }

    void ShowPage(Panel page, string title, string sub, bool showActions)
    {
        foreach (Control c in _pagePanel.Controls) c.Visible = false;
        page.Visible = true;
        _pageTitle.Text = title; _pageSub.Text = sub;
        _btnRun.Visible = showActions; _btnToggle.Visible = showActions;
    }

    // ── Dashboard Page ───────────────────────────────────
    void BuildDashboardPage()
    {
        _pgDashboard = MakePage();
        int y = 0;

        // Metrics row
        var metrics = new[] { (_mTotal,"—","TOPLAM (BUGÜN)",Accent), (_mOk,"—","BAŞARILI",Green), (_mSkip,"—","ATLANDI",FgSec), (_mErr,"—","HATA",Amber) };
        int mx = 0;
        foreach (var (lbl,val,name,col) in metrics)
        {
            var card = MakeCard(new Rectangle(mx,y,_pagePanel.Width/4-6,72)); mx += _pagePanel.Width/4-2;
            lbl.Text=val; lbl.Font=new Font("Consolas",20f,FontStyle.Bold); lbl.ForeColor=col;
            lbl.Location=new Point(12,8); lbl.Size=new Size(card.Width-24,32); lbl.BackColor=Color.Transparent;
            AddLabel(card,name,new Rectangle(12,44,card.Width-24,14),FgDim,7.5f);
            card.Controls.Add(lbl); _pgDashboard.Controls.Add(card);
        }
        y += 80;

        // Scan card
        var scanCard = MakeCard(new Rectangle(0,y,_pagePanel.Width-4,110));
        AddLabel(scanCard,"GEÇMİŞ FATURALARI TARA",new Rectangle(12,8,300,14),FgSec,7.5f);
        AddLabel(scanCard,"Kategorisi veya tahsilatı eksik faturaları tara, otomatik tamamla.",new Rectangle(12,24,500,14),FgSec,8f);

        // Hızlı tarih butonları
        var quickDates = new (string Label, Func<(DateTime,DateTime)> Range)[]
        {
            ("Bugün",      () => (DateTime.Today, DateTime.Today)),
            ("Bu Hafta",   () => {var d=DateTime.Today; return (d.AddDays(-(int)d.DayOfWeek+1), d);}),
            ("Bu Ay",      () => (new DateTime(DateTime.Today.Year,DateTime.Today.Month,1), DateTime.Today)),
            ("Son 1 Hafta",() => (DateTime.Today.AddDays(-7), DateTime.Today)),
            ("Son 1 Ay",   () => (DateTime.Today.AddMonths(-1), DateTime.Today)),
        };
        int bx = 12;
        foreach (var (label, range) in quickDates)
        {
            var btn = MakeButton(label, new Rectangle(bx,42,90,22), BG3, FgSec);
            btn.Font = new Font("Segoe UI", 7.5f);
            btn.Click += (s,e) => { var (f,t)=range(); _dtFrom.Value=f; _dtTo.Value=t; };
            scanCard.Controls.Add(btn);
            bx += 96;
        }

        _dtFrom = new DateTimePicker { Location=new Point(12,72), Size=new Size(130,24), Format=DateTimePickerFormat.Short, BackColor=BG3, ForeColor=FgPri, CalendarMonthBackground=BG3 };
        _dtTo   = new DateTimePicker { Location=new Point(152,72), Size=new Size(130,24), Format=DateTimePickerFormat.Short, BackColor=BG3, ForeColor=FgPri };
        _dtFrom.Value = DateTime.Now.AddDays(-30); _dtTo.Value = DateTime.Now;
        _btnScan = MakeButton("Tara ve Tamamla", new Rectangle(292,72,130,24), Accent, Color.White);
        _btnScan.Click += async (s,e) => await RunScan();
        _lblScanResult = AddLabel(scanCard,"",new Rectangle(432,72,400,24),Green,8.5f);
        scanCard.Controls.AddRange(new Control[]{_dtFrom,_dtTo,_btnScan,_lblScanResult});
        _pgDashboard.Controls.Add(scanCard);
        y += 118;

        // Recent invoices
        var tblCard = MakeCard(new Rectangle(0,y,_pagePanel.Width-4,_pagePanel.Height-y-4));
        AddLabel(tblCard,"SON İŞLEMLER",new Rectangle(12,8,200,14),FgSec,7.5f);
        _dgDash = MakeGrid(new Rectangle(0,28,tblCard.Width,tblCard.Height-28));
        _dgDash.Columns.AddRange(MakeColumns("Sipariş No","Platform","Kategori","Fatura","Tahsilat Hesabı","Tutar","Durum","Zaman"));
        tblCard.Controls.Add(_dgDash);
        _pgDashboard.Controls.Add(tblCard);
        _pagePanel.Controls.Add(_pgDashboard);
    }

    // ── Invoices Page ────────────────────────────────────
    void BuildInvoicesPage()
    {
        _pgInvoices = MakePage();
        var card = MakeCard(new Rectangle(0,0,_pagePanel.Width-4,_pagePanel.Height-4));
        _dgInvoices = MakeGrid(new Rectangle(0,0,card.Width,card.Height));
        _dgInvoices.Columns.AddRange(MakeColumns("Sipariş No","Platform","Kategori","Fatura","Tahsilat Hesabı","Mail","Tutar","Durum","Zaman"));
        card.Controls.Add(_dgInvoices);
        _pgInvoices.Controls.Add(card);
        _pagePanel.Controls.Add(_pgInvoices);
        _pgInvoices.Visible = false;
    }

    // ── Log Page ─────────────────────────────────────────
    void BuildLogPage()
    {
        _pgLog = MakePage();
        var card = MakeCard(new Rectangle(0,0,_pagePanel.Width-4,_pagePanel.Height-4));
        _rtbLog = new RichTextBox { Location=new Point(0,0), Size=card.Size, BackColor=BG3, ForeColor=FgSec, BorderStyle=BorderStyle.None, Font=new Font("Consolas",8.5f), ReadOnly=true, ScrollBars=RichTextBoxScrollBars.Vertical };
        card.Controls.Add(_rtbLog);
        _pgLog.Controls.Add(card);
        _pagePanel.Controls.Add(_pgLog);
        _pgLog.Visible = false;
    }

    // ── Platforms Page ───────────────────────────────────
    void BuildPlatformsPage()
    {
        _pgPlatforms = MakePage();
        var addBtn = MakeButton("+ Platform Ekle", new Rectangle(_pagePanel.Width-130,0,120,28), Accent, Color.White);
        addBtn.Click += (s,e) => EditPlatform(null);
        _pgPlatforms.Controls.Add(addBtn);
        var card = MakeCard(new Rectangle(0,36,_pagePanel.Width-4,_pagePanel.Height-40));
        _dgPlatforms = MakeGrid(new Rectangle(0,0,card.Width,card.Height));
        _dgPlatforms.Columns.AddRange(MakeColumns("Ön Ek","Kategori","Fatura?","Tahsilat Hesabı","Mail?","Aktif","Düzenle","Sil"));
        card.Controls.Add(_dgPlatforms);
        _pgPlatforms.Controls.Add(card);
        _pagePanel.Controls.Add(_pgPlatforms);
        _pgPlatforms.Visible = false;
    }

    // ── Accounts Page ────────────────────────────────────
    void BuildAccountsPage()
    {
        _pgAccounts = MakePage();
        var addBtn = MakeButton("+ Hesap Ekle", new Rectangle(_pagePanel.Width-130,0,120,28), Accent, Color.White);
        addBtn.Click += (s,e) => EditAccount(null);
        _pgAccounts.Controls.Add(addBtn);
        var card = MakeCard(new Rectangle(0,36,_pagePanel.Width-4,_pagePanel.Height-40));
        _dgAccounts = MakeGrid(new Rectangle(0,0,card.Width,card.Height));
        _dgAccounts.Columns.AddRange(MakeColumns("Hesap Adı","Paraşüt ID","Tür","Düzenle","Sil"));
        card.Controls.Add(_dgAccounts);
        _pgAccounts.Controls.Add(card);
        _pagePanel.Controls.Add(_pgAccounts);
        _pgAccounts.Visible = false;
    }

    // ── Settings: Theme ──────────────────────────────────
    // ── Settings: Database ───────────────────────────────
    void BuildSettingsDbPage()
    {
        _pgSettingsDb = MakePage();
        var card = MakeCard(new Rectangle(0,0,_pagePanel.Width-4,260));
        AddLabel(card,"MYSQL BAĞLANTI AYARLARI",new Rectangle(12,8,400,14),FgSec,7.5f);
        _lblDbStatus = AddLabel(card, _db.IsMySQLActive() ? "● MySQL Aktif" : "● JSON Mod (MySQL bağlı değil)",
            new Rectangle(12,26,500,16), _db.IsMySQLActive() ? Green : Amber, 8.5f, FontStyle.Bold);
        int y = 46;
        _chkUseMySQL = MakeCheckBox(card,"MySQL kullan (işaretlenmezse JSON dosya modu kullanılır)",new Rectangle(12,y,500,20)); y+=32;
        _tDbHost   = MakeTextBox(card,"HOST",            new Rectangle(12,y,200,44));
        _numDbPort = MakeNumeric(card,"PORT",            new Rectangle(222,y,80,44),1,65535,3306);
        _tDbName   = MakeTextBox(card,"VERİTABANI ADI", new Rectangle(312,y,220,44)); y+=52;
        _tDbUser   = MakeTextBox(card,"KULLANICI ADI",   new Rectangle(12,y,220,44));
        _tDbPass   = MakeTextBox(card,"ŞİFRE",           new Rectangle(242,y,220,44),true); y+=52;
        var btnTest = MakeButton("Bağlantıyı Test Et",       new Rectangle(12,y,140,28),BG3,FgSec);
        var btnSave = MakeButton("Kaydet ve Yeniden Başlat", new Rectangle(162,y,180,28),Accent,Color.White);
        _lblDbTest  = AddLabel(card,"",new Rectangle(12,y+34,600,18),Green,8.5f);
        btnTest.Click += (s,e) => {
            var (ok,msg) = _db.TestMySQL(ReadDbForm());
            _lblDbTest.ForeColor = ok ? Green : Red;
            _lblDbTest.Text = (ok?"✓ ":"✕ ") + msg;
        };
        btnSave.Click += (s,e) => {
            _db.SaveConfig(ReadDbForm());
            ShowMsg("Ayarlar kaydedildi. Uygulama yeniden başlatılıyor...","Kaydedildi");
            Application.Restart();
        };
        card.Controls.AddRange(new Control[]{btnTest,btnSave});
        _pgSettingsDb.Controls.Add(card);
        _pagePanel.Controls.Add(_pgSettingsDb);
        _pgSettingsDb.Visible = false;
    }

    void LoadDbSettings()
    {
        var cfg = _db.GetConfig();
        _chkUseMySQL.Checked = cfg.UseMySQL;
        _tDbHost.Text = cfg.Host;
        _numDbPort.Value = cfg.Port;
        _tDbName.Text = cfg.Database;
        _tDbUser.Text = cfg.Username;
        _lblDbStatus.Text = _db.IsMySQLActive() ? "● MySQL Aktif" : "● JSON Mod (MySQL bağlı değil)";
        _lblDbStatus.ForeColor = _db.IsMySQLActive() ? Green : Amber;
    }

    DbConfig ReadDbForm() => new DbConfig {
        UseMySQL = _chkUseMySQL.Checked,
        Host     = _tDbHost.Text.Trim(),
        Port     = (int)_numDbPort.Value,
        Database = _tDbName.Text.Trim(),
        Username = _tDbUser.Text.Trim(),
        Password = _tDbPass.Text,
    };

    void BuildSettingsParasutPage()
    {
        _pgSettingsParasut = MakePage();
        var card = MakeCard(new Rectangle(0,0,_pagePanel.Width-4,320));
        AddLabel(card,"PARASüT API BİLGİLERİ",new Rectangle(12,8,300,14),FgSec,7.5f);
        int y=28;
        _tClientId     = MakeTextBox(card,"CLIENT ID",new Rectangle(12,y,300,44));y+=52;
        _tClientSecret = MakeTextBox(card,"CLIENT SECRET",new Rectangle(12,y,300,44),true);
        _tUsername     = MakeTextBox(card,"E-POSTA",new Rectangle(322,y-52,300,44));
        _tPassword     = MakeTextBox(card,"ŞİFRE",new Rectangle(322,y,300,44),true);y+=52;
        _tCompanyId    = MakeTextBox(card,"ŞİRKET ID",new Rectangle(12,y,180,44));y+=52;
        _chkDemoMode   = MakeCheckBox(card,"Demo Modu (gerçek API çağrısı yapılmaz)",new Rectangle(12,y,400,20));y+=28;
        var btnSave = MakeButton("Kaydet",new Rectangle(12,y,80,28),Accent,Color.White);
        var btnTest = MakeButton("Bağlantıyı Test Et",new Rectangle(100,y,140,28),BG3,FgSec);
        btnSave.Click+=(s,e)=>SaveParasutSettings();
        btnTest.Click+=async(s,e)=>await TestParasutConn();
        _lblParasutTest=AddLabel(card,"",new Rectangle(250,y+5,500,18),Green,8.5f);
        card.Controls.AddRange(new Control[]{btnSave,btnTest});
        _pgSettingsParasut.Controls.Add(card);
        _pagePanel.Controls.Add(_pgSettingsParasut);
        _pgSettingsParasut.Visible=false;
    }

    // ── Settings: Job ────────────────────────────────────
    void BuildSettingsJobPage()
    {
        _pgSettingsJob = MakePage();
        var card = MakeCard(new Rectangle(0,0,_pagePanel.Width-4,140));
        AddLabel(card,"ÇALIŞMA PARAMETRELERİ",new Rectangle(12,8,300,14),FgSec,7.5f);
        _numFreq     = MakeNumeric(card,"SIKLIK (DAKİKA)",  new Rectangle(12,28,130,44),5,1440,60);
        _numStart    = MakeNumeric(card,"BAŞLANGIÇ SAATİ",  new Rectangle(152,28,110,44),0,23,9);
        _numEnd      = MakeNumeric(card,"BİTİŞ SAATİ",      new Rectangle(272,28,110,44),1,24,23);
        _numLookback = MakeNumeric(card,"GERİYE BAK (SAAT)",new Rectangle(392,28,110,44),1,72,2);
        var btnSave = MakeButton("Kaydet",new Rectangle(12,88,80,28),Accent,Color.White);
        btnSave.Click+=(s,e)=>SaveJobSettings();
        card.Controls.Add(btnSave);
        _pgSettingsJob.Controls.Add(card);
        _pagePanel.Controls.Add(_pgSettingsJob);
        _pgSettingsJob.Visible=false;
    }

    // ── Settings: Mail ───────────────────────────────────
    void BuildSettingsMailPage()
    {
        _pgSettingsMail = MakePage();
        var card = MakeCard(new Rectangle(0,0,_pagePanel.Width-4,480));
        AddLabel(card,"SMTP BAĞLANTI",new Rectangle(12,8,300,14),FgSec,7.5f);
        AddLabel(card,"Natro için: Host mail.natro.com · Port 587 · STARTTLS",new Rectangle(12,24,500,14),Amber,8f);
        int y=42;
        _tSmtpHost    = MakeTextBox(card,"SMTP HOST",       new Rectangle(12,y,200,44));
        _numSmtpPort  = MakeNumeric(card,"PORT",            new Rectangle(222,y,80,44),1,65535,587);
        _cmbTls       = MakeCombo(card,"ŞİFRELEME",         new Rectangle(312,y,140,44), new[]{"STARTTLS (587)","SSL (465)"});
        _tSmtpUser    = MakeTextBox(card,"KULLANICI ADI",   new Rectangle(462,y,250,44));y+=52;
        _tSmtpPass    = MakeTextBox(card,"ŞİFRE",           new Rectangle(12,y,200,44),true);
        _tSmtpFromName= MakeTextBox(card,"GÖNDEREN ADI",    new Rectangle(222,y,200,44));
        _tSmtpFrom    = MakeTextBox(card,"GÖNDEREN MAİL",   new Rectangle(432,y,280,44));y+=52;
        var btnTest = MakeButton("Bağlantıyı Test Et",new Rectangle(12,y,140,28),BG3,FgSec);
        btnTest.Click+=async(s,e)=>await TestSmtpConn();
        _lblSmtpTest=AddLabel(card,"",new Rectangle(162,y+5,400,18),Green,8.5f);y+=36;
        _tMailSubject = MakeTextBox(card,"KONU ({order_no}, {customer_name})",new Rectangle(12,y,700,44));y+=52;
        AddLabel(card,"MESAJ",new Rectangle(12,y,200,14),FgSec,7.5f);y+=16;
        _rtbMailBody = new RichTextBox { Location=new Point(12,y), Size=new Size(700,80), BackColor=BG3, ForeColor=FgPri, BorderStyle=BorderStyle.FixedSingle, Font=new Font("Consolas",8.5f) };
        card.Controls.Add(_rtbMailBody);y+=88;
        _chkAutoMail = MakeCheckBox(card,"Fatura işlenince otomatik mail gönder",new Rectangle(12,y,400,20));y+=32;
        var btnSave = MakeButton("Tüm Ayarları Kaydet",new Rectangle(12,y,140,30),Accent,Color.White);
        btnSave.Click+=(s,e)=>SaveMailSettings();
        card.Controls.AddRange(new Control[]{btnTest,btnSave});
        _pgSettingsMail.Controls.Add(card);
        _pagePanel.Controls.Add(_pgSettingsMail);
        _pgSettingsMail.Visible=false;
    }

    // ── Load Data ────────────────────────────────────────
    void LoadDashboard()
    {
        var m = _db.TodayMetrics();
        _mTotal.Text = m.Total.ToString(); _mOk.Text = m.Ok.ToString();
        _mSkip.Text  = m.Skip.ToString();  _mErr.Text = m.Err.ToString();
        var invs = _db.ListInvoices(20);
        _dgDash.Rows.Clear();
        foreach (var inv in invs) _dgDash.Rows.Add(inv.OrderNo, inv.PlatformName, inv.Category, inv.InvoiceCreated?"Evet":"—", inv.AccountName, inv.Amount.ToString("F2")+" ₺", inv.Status=="ok"?"Başarılı":inv.Status=="skip"?"Atlandı":"Hata", inv.ProcessedAt.ToString("dd.MM HH:mm"));
        UpdateJobStatusLabel();
    }

    void LoadInvoices()
    {
        var invs = _db.ListInvoices(500);
        _dgInvoices.Rows.Clear();
        foreach (var inv in invs) _dgInvoices.Rows.Add(inv.OrderNo, inv.PlatformName, inv.Category, inv.InvoiceCreated?"Evet":"—", inv.AccountName, inv.MailSent?"Gönderildi":"—", inv.Amount.ToString("F2")+" ₺", inv.Status=="ok"?"Başarılı":inv.Status=="skip"?"Atlandı":"Hata", inv.ProcessedAt.ToString("dd.MM.yy HH:mm"));
    }

    void LoadLogs()
    {
        var logs = _db.ListLogs(500);
        _rtbLog.Clear();
        foreach (var log in logs)
        {
            var color = log.Level == "ok" ? Green : log.Level == "err" ? Red : log.Level == "warn" ? Amber : Accent;
            _rtbLog.SelectionColor = FgDim;
            _rtbLog.AppendText(log.CreatedAt.ToString("dd.MM HH:mm:ss") + "  ");
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText($"[{log.Level.ToUpper()}] {log.Message}\n");
        }
    }

    void LoadPlatforms()
    {
        var plats = _db.ListPlatforms();
        _dgPlatforms.Rows.Clear();
        foreach (var p in plats)
        {
            var i = _dgPlatforms.Rows.Add(p.Prefix, p.Category, p.InvoiceEnabled?"Evet":"Hayır", p.AccountName??"— Tahsilat Yok —", p.MailEnabled?"Evet":"Hayır", p.Active?"Aktif":"Pasif","✎ Düzenle","✖ Sil");
            _dgPlatforms.Rows[i].Tag = p;
        }
    }

    void LoadAccounts()
    {
        var accs = _db.ListAccounts();
        _dgAccounts.Rows.Clear();
        foreach (var a in accs)
        {
            var i = _dgAccounts.Rows.Add(a.Name, a.ParasutId, a.AccountType,"✎ Düzenle","✖ Sil");
            _dgAccounts.Rows[i].Tag = a;
        }
    }

    void LoadParasutSettings()
    {
        _tClientId.Text   = _db.GetSetting("parasut_client_id");
        _tUsername.Text   = _db.GetSetting("parasut_username");
        _tCompanyId.Text  = _db.GetSetting("parasut_company_id");
        _chkDemoMode.Checked = _db.GetSetting("demo_mode","1")=="1";
    }

    void LoadJobSettings()
    {
        _numFreq.Value     = decimal.Parse(_db.GetSetting("job_freq_minutes","60"));
        _numStart.Value    = decimal.Parse(_db.GetSetting("job_start_hour","9"));
        _numEnd.Value      = decimal.Parse(_db.GetSetting("job_end_hour","23"));
        _numLookback.Value = decimal.Parse(_db.GetSetting("lookback_hours","2"));
    }

    void LoadMailSettings()
    {
        _tSmtpHost.Text      = _db.GetSetting("smtp_host","mail.natro.com");
        _numSmtpPort.Value   = decimal.Parse(_db.GetSetting("smtp_port","587"));
        _cmbTls.SelectedIndex= _db.GetSetting("smtp_tls","1")=="1"?0:1;
        _tSmtpUser.Text      = _db.GetSetting("smtp_user");
        _tSmtpFrom.Text      = _db.GetSetting("smtp_from");
        _tSmtpFromName.Text  = _db.GetSetting("smtp_from_name","Fatura Sistemi");
        _tMailSubject.Text   = _db.GetSetting("mail_subject","Faturanız hazır - {order_no}");
        _rtbMailBody.Text    = _db.GetSetting("mail_body");
        _chkAutoMail.Checked = _db.GetSetting("mail_auto_send","0")=="1";
    }

    // ── Save Settings ────────────────────────────────────
    void SaveParasutSettings()
    {
        _db.SetSetting("parasut_client_id",     _tClientId.Text.Trim());
        _db.SetSetting("parasut_client_secret", _tClientSecret.Text.Trim());
        _db.SetSetting("parasut_username",      _tUsername.Text.Trim());
        _db.SetSetting("parasut_password",      _tPassword.Text);
        _db.SetSetting("parasut_company_id",    _tCompanyId.Text.Trim());
        _db.SetSetting("demo_mode",             _chkDemoMode.Checked?"1":"0");
        ShowMsg("API bilgileri kaydedildi.", "Kaydedildi");
    }

    void SaveJobSettings()
    {
        _db.SetSetting("job_freq_minutes", ((int)_numFreq.Value).ToString());
        _db.SetSetting("job_start_hour",   ((int)_numStart.Value).ToString());
        _db.SetSetting("job_end_hour",     ((int)_numEnd.Value).ToString());
        _db.SetSetting("lookback_hours",   ((int)_numLookback.Value).ToString());
        SetupJobTimer();
        ShowMsg("Zamanlama ayarları kaydedildi.", "Kaydedildi");
    }

    void SaveMailSettings()
    {
        _db.SetSetting("smtp_host",       _tSmtpHost.Text.Trim());
        _db.SetSetting("smtp_port",       ((int)_numSmtpPort.Value).ToString());
        _db.SetSetting("smtp_tls",        _cmbTls.SelectedIndex==0?"1":"0");
        _db.SetSetting("smtp_user",       _tSmtpUser.Text.Trim());
        _db.SetSetting("smtp_from",       _tSmtpFrom.Text.Trim());
        _db.SetSetting("smtp_from_name",  _tSmtpFromName.Text.Trim());
        _db.SetSetting("mail_subject",    _tMailSubject.Text);
        _db.SetSetting("mail_body",       _rtbMailBody.Text);
        _db.SetSetting("mail_auto_send",  _chkAutoMail.Checked?"1":"0");
        if (_tSmtpPass.Text.Length > 0) _db.SetSetting("smtp_password", _tSmtpPass.Text);
        ShowMsg("Mail ayarları kaydedildi.", "Kaydedildi");
    }

    // ── Job ──────────────────────────────────────────────
    void SetupJobTimer()
    {
        _jobTimer.Stop();
        var freq = int.Parse(_db.GetSetting("job_freq_minutes","60"));
        _jobTimer.Interval = freq * 60 * 1000;
        _jobTimer.Tick += async (s,e) => await RunJobNow();
        if (_db.GetSetting("job_active","1")=="1") _jobTimer.Start();
    }

    void SetupUiTimer()
    {
        _uiTimer.Interval = 30000;
        _uiTimer.Tick += (s,e) => { UpdateJobStatusLabel(); if (_pgDashboard.Visible) LoadDashboard(); };
        _uiTimer.Start();
    }

    async Task RunJobNow()
    {
        if (_jobRunning) return;
        _jobRunning = true;
        _btnRun.Text = "⟳ Çalışıyor..."; _btnRun.Enabled = false;
        try
        {
            var r = await _parasut.RunJob();
            if (_pgDashboard.Visible) LoadDashboard();
            if (_pgLog.Visible) LoadLogs();
            _trayIcon.ShowBalloonTip(3000,"HPY Soft-Engine",$"Job tamamlandı. {r.Ok} başarılı, {r.Err} hata.",ToolTipIcon.Info);
        }
        catch (Exception ex) { ShowMsg("Job hatası: " + ex.Message,"Hata"); }
        finally { _jobRunning=false; _btnRun.Text="▶ Şimdi Çalıştır"; _btnRun.Enabled=true; }
    }

    async Task ToggleJob()
    {
        var active = _db.GetSetting("job_active","1")=="1";
        _db.SetSetting("job_active", active?"0":"1");
        if (active) _jobTimer.Stop(); else { _jobTimer.Start(); }
        UpdateJobStatusLabel();
    }

    void UpdateJobStatusLabel()
    {
        var active = _db.GetSetting("job_active","1")=="1";
        _lblJobStatus.Text = active ? "● Aktif" : "● Durduruldu";
        _lblJobStatus.ForeColor = active ? Green : Amber;
        _btnToggle.Text = active ? "⏸ Durdur" : "▶ Başlat";
        if (_jobTimer.Enabled)
        {
            var freq = int.Parse(_db.GetSetting("job_freq_minutes","60"));
            _lblNextRun.Text = $"Sonraki: ~{freq}dk";
        }
    }

    async Task RunScan()
    {
        _btnScan.Enabled=false; _btnScan.Text="Taranıyor..."; _lblScanResult.Text="";
        try
        {
            var r = await _parasut.RunScan(_dtFrom.Value.ToString("yyyy-MM-dd"), _dtTo.Value.ToString("yyyy-MM-dd"));
            _lblScanResult.Text = $"✓ {r.Total} kontrol, {r.Ok} işlendi, {r.Skip} atlandı, {r.Err} hata, {r.Already} tamam";
            LoadDashboard();
        }
        catch(Exception ex) { _lblScanResult.ForeColor=Red; _lblScanResult.Text="Hata: "+ex.Message; }
        finally { _btnScan.Enabled=true; _btnScan.Text="Tara ve Tamamla"; }
    }

    // ── Platform/Account Edit ────────────────────────────
    void EditPlatform(Platform? p)
    {
        var form = new PlatformForm(_db, _parasut, p);
        if (form.ShowDialog() == DialogResult.OK) LoadPlatforms();
    }

    void EditAccount(Account? a)
    {
        var form = new AccountForm(_db, a);
        if (form.ShowDialog() == DialogResult.OK) LoadAccounts();
    }

    // ── Test connections ─────────────────────────────────
    async Task TestParasutConn()
    {
        _lblParasutTest.Text = "Test ediliyor..."; _lblParasutTest.ForeColor = Amber;
        var (ok,msg) = await _parasut.TestConnection();
        _lblParasutTest.ForeColor = ok ? Green : Red;
        _lblParasutTest.Text = (ok?"✓ ":"✕ ") + msg;
    }

    async Task TestSmtpConn()
    {
        _lblSmtpTest.Text = "Test ediliyor..."; _lblSmtpTest.ForeColor = Amber;
        var (ok,msg) = await _mail.TestSmtp();
        _lblSmtpTest.ForeColor = ok ? Green : Red;
        _lblSmtpTest.Text = (ok?"✓ ":"✕ ") + msg;
    }

    // ── Tray ─────────────────────────────────────────────
    void SetupTray()
    {
        _trayIcon = new NotifyIcon { Text="HPY Soft-Engine", Visible=true };
        try { _trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch {}
        var menu = new ContextMenuStrip();
        menu.Items.Add("Aç", null, (s,e)=>{ Show(); WindowState=FormWindowState.Normal; BringToFront(); });
        menu.Items.Add("Şimdi Çalıştır", null, async (s,e)=> await RunJobNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (s,e)=>{ _trayIcon.Visible=false; Application.Exit(); });
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (s,e)=>{ Show(); WindowState=FormWindowState.Normal; BringToFront(); };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel=true; Hide(); return; }
        _trayIcon.Visible=false; base.OnFormClosing(e);
    }

    // ── UI Helpers ───────────────────────────────────────
    Panel MakePage()
    {
        var p = new Panel { Location=Point.Empty, Size=_pagePanel.ClientSize, BackColor=Color.Transparent };
        p.Anchor = AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Bottom;
        return p;
    }

    Panel MakePanel(Color bg, Rectangle bounds)
    {
        var p = new Panel { BackColor=bg, Location=new Point(bounds.X,bounds.Y), Size=new Size(bounds.Width,bounds.Height) };
        Controls.Add(p); return p;
    }

    Panel MakeCard(Rectangle bounds)
    {
        var p = new Panel { BackColor=BG2, Location=new Point(bounds.X,bounds.Y), Size=new Size(bounds.Width,bounds.Height), Padding=new Padding(0) };
        p.Paint += (s,e)=>{
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0,0,p.Width-1,p.Height-1);
        };
        return p;
    }

    Label AddLabel(Control parent, string text, Rectangle bounds, Color color, float size=8.5f, FontStyle style=FontStyle.Regular, Brush? bg=null)
    {
        var l = new Label { Text=text, Location=new Point(bounds.X,bounds.Y), Size=new Size(bounds.Width,bounds.Height), ForeColor=color, Font=new Font("Segoe UI",size,style), BackColor=Color.Transparent, AutoSize=false };
        if (bg != null) { l.BackColor = Color.Transparent; l.Paint+=(s,e)=>{ e.Graphics.FillRectangle(bg,0,0,l.Width,l.Height); e.Graphics.DrawString(l.Text,l.Font,new SolidBrush(color),0,3); }; }
        parent.Controls.Add(l); return l;
    }

    Button MakeButton(string text, Rectangle bounds, Color bg, Color fg)
    {
        var b = new Button { Text=text, Location=new Point(bounds.X,bounds.Y), Size=new Size(bounds.Width,bounds.Height), FlatStyle=FlatStyle.Flat, BackColor=bg, ForeColor=fg, Font=new Font("Segoe UI",8.5f,FontStyle.Bold), Cursor=Cursors.Hand };
        b.FlatAppearance.BorderSize=0; return b;
    }

    Button MakeTitleBtn(string text, Rectangle bounds, EventHandler onClick)
    {
        var b = MakeButton(text, bounds, Color.Transparent, FgSec);
        b.Font = new Font("Segoe UI",10f); b.Click+=onClick; return b;
    }

    DataGridView MakeGrid(Rectangle bounds)
    {
        var g = new DataGridView { Location=new Point(bounds.X,bounds.Y), Size=new Size(bounds.Width,bounds.Height), BackgroundColor=BG2, GridColor=Border, BorderStyle=BorderStyle.None, RowHeadersVisible=false, AllowUserToAddRows=false, AllowUserToDeleteRows=false, ReadOnly=true, SelectionMode=DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill, Font=new Font("Segoe UI",8.5f), ForeColor=FgPri, ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=BG3,ForeColor=FgSec,Font=new Font("Consolas",7.5f),SelectionBackColor=BG3}, DefaultCellStyle=new DataGridViewCellStyle{BackColor=BG2,ForeColor=FgPri,SelectionBackColor=Color.FromArgb(40,76,110,245),SelectionForeColor=FgPri}, AlternatingRowsDefaultCellStyle=new DataGridViewCellStyle{BackColor=Color.FromArgb(20,255,255,255)}, ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight=28, RowTemplate={Height=28} };
        g.EnableHeadersVisualStyles=false;
        g.CellClick += OnGridCellClick;
        return g;
    }

    DataGridViewTextBoxColumn[] MakeColumns(params string[] names)
        => names.Select(n => new DataGridViewTextBoxColumn { HeaderText=n.ToUpper(), SortMode=DataGridViewColumnSortMode.NotSortable }).ToArray();

    TextBox MakeTextBox(Control parent, string label, Rectangle bounds, bool password=false)
    {
        AddLabel(parent, label, new Rectangle(bounds.X,bounds.Y,bounds.Width,14), FgDim, 7.5f);
        var t = new TextBox { Location=new Point(bounds.X,bounds.Y+16), Size=new Size(bounds.Width,24), BackColor=BG3, ForeColor=FgPri, BorderStyle=BorderStyle.FixedSingle, Font=new Font("Segoe UI",9f) };
        if (password) t.UseSystemPasswordChar=true;
        parent.Controls.Add(t); return t;
    }

    NumericUpDown MakeNumeric(Control parent, string label, Rectangle bounds, int min, int max, int val)
    {
        AddLabel(parent, label, new Rectangle(bounds.X,bounds.Y,bounds.Width,14), FgDim, 7.5f);
        var n = new NumericUpDown { Location=new Point(bounds.X,bounds.Y+16), Size=new Size(bounds.Width,24), BackColor=BG3, ForeColor=FgPri, BorderStyle=BorderStyle.FixedSingle, Minimum=min, Maximum=max, Value=val, Font=new Font("Segoe UI",9f) };
        parent.Controls.Add(n); return n;
    }

    ComboBox MakeCombo(Control parent, string label, Rectangle bounds, string[] items)
    {
        AddLabel(parent, label, new Rectangle(bounds.X,bounds.Y,bounds.Width,14), FgDim, 7.5f);
        var c = new ComboBox { Location=new Point(bounds.X,bounds.Y+16), Size=new Size(bounds.Width,24), BackColor=BG3, ForeColor=FgPri, FlatStyle=FlatStyle.Flat, Font=new Font("Segoe UI",9f), DropDownStyle=ComboBoxStyle.DropDownList };
        c.Items.AddRange(items); c.SelectedIndex=0;
        parent.Controls.Add(c); return c;
    }

    CheckBox MakeCheckBox(Control parent, string text, Rectangle bounds)
    {
        var c = new CheckBox { Text=text, Location=new Point(bounds.X,bounds.Y), Size=new Size(bounds.Width,bounds.Height), ForeColor=FgSec, BackColor=Color.Transparent, Font=new Font("Segoe UI",9f) };
        parent.Controls.Add(c); return c;
    }

    void OnGridCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || sender is not DataGridView grid) return;
        var row = grid.Rows[e.RowIndex];
        var tag = row.Tag;
        var hdr = grid.Columns[e.ColumnIndex].HeaderText;
        if (hdr == "DÜZENLE" && tag is Platform p) EditPlatform(p);
        else if (hdr == "SİL" && tag is Platform pd) { if(ShowConfirm("Bu platformu silmek istediğinizden emin misiniz?")){_db.DeletePlatform(pd.Id);LoadPlatforms();} }
        else if (hdr == "DÜZENLE" && tag is Account a) EditAccount(a);
        else if (hdr == "SİL" && tag is Account ad) { if(ShowConfirm("Bu hesabı silmek istediğinizden emin misiniz?")){_db.DeleteAccount(ad.Id);LoadAccounts();} }
    }

    static Color HexColor(string hex, Color fallback)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
                return Color.FromArgb(
                    Convert.ToInt32(hex[..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..6], 16));
        }
        catch { }
        return fallback;
    }

    static void ShowMsg(string msg, string title) => MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    static bool ShowConfirm(string msg) => MessageBox.Show(msg,"Onay",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;
}
