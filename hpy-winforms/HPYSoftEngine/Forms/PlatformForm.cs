using HPYSoftEngine.Models;
using HPYSoftEngine.Services;

namespace HPYSoftEngine.Forms;

public class PlatformForm : Form
{
    private readonly DatabaseService _db;
    private readonly ParasutService? _parasut;
    private readonly Platform? _platform;
    private TextBox _tPrefix = new();
    private ComboBox _cmbCategory = new(), _cmbAccount = new(), _cmbInvoice = new(), _cmbMail = new(), _cmbActive = new();
    private List<(string Id, string Name)> _categories = new();
    private List<(string Id, string Name, string Type)> _bankAccounts = new();
    private Label _lblCatStatus = new(), _lblAccStatus = new();

    static readonly Color BG2    = Color.FromArgb(19,21,31);
    static readonly Color BG3    = Color.FromArgb(26,29,46);
    static readonly Color Accent = Color.FromArgb(76,110,245);
    static readonly Color Amber  = Color.FromArgb(247,183,49);
    static readonly Color Green  = Color.FromArgb(32,191,107);
    static readonly Color Red    = Color.FromArgb(235,59,90);
    static readonly Color FgPri  = Color.FromArgb(221,225,237);
    static readonly Color FgSec  = Color.FromArgb(120,128,160);
    static readonly Color FgDim  = Color.FromArgb(61,66,96);

    public PlatformForm(DatabaseService db, ParasutService? parasut, Platform? platform)
    {
        _db = db; _parasut = parasut; _platform = platform;
        Text = platform == null ? "Platform Ekle" : "Platform Düzenle";
        Size = new Size(560, 500);
        BackColor = BG2; ForeColor = FgPri;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false;

        int y = 16;
        _tPrefix = Field("ÖN EK (tire öncesi kısım — örn: Amazon Türkiye)", new Rectangle(16,y,510,44)); y+=52;

        // Kategori — Paraşüt'ten
        Lbl("PARAŞüT KATEGORİSİ", new Rectangle(16,y,400,14));
        _cmbCategory = new ComboBox { Location=new Point(16,y+16), Size=new Size(400,24), BackColor=BG3, ForeColor=FgPri, FlatStyle=FlatStyle.Flat, DropDownStyle=ComboBoxStyle.DropDownList };
        _cmbCategory.Items.Add("Yükleniyor...");
        _cmbCategory.SelectedIndex = 0;
        Controls.Add(_cmbCategory);
        var btnRefCat = Btn("↻",new Rectangle(426,y+16,50,24),BG3,FgSec);
        btnRefCat.Click += async (s,e) => await LoadCategories();
        _lblCatStatus = new Label { Location=new Point(16,y+44), Size=new Size(510,30), ForeColor=Amber, Font=new Font("Segoe UI",7.5f), BackColor=Color.Transparent };
        Controls.Add(_lblCatStatus);
        y += 80;

        // Hesap — Paraşüt'ten
        Lbl("TAHSİLAT HESABI (Paraşüt Banka/Kasa)", new Rectangle(16,y,400,14));
        _cmbAccount = new ComboBox { Location=new Point(16,y+16), Size=new Size(400,24), BackColor=BG3, ForeColor=FgPri, FlatStyle=FlatStyle.Flat, DropDownStyle=ComboBoxStyle.DropDownList };
        _cmbAccount.Items.Add("— Tahsilat Yapılmasın —");
        _cmbAccount.SelectedIndex = 0;
        Controls.Add(_cmbAccount);
        var btnRefAcc = Btn("↻",new Rectangle(426,y+16,50,24),BG3,FgSec);
        btnRefAcc.Click += async (s,e) => await LoadBankAccounts();
        _lblAccStatus = new Label { Location=new Point(16,y+44), Size=new Size(510,30), ForeColor=Amber, Font=new Font("Segoe UI",7.5f), BackColor=Color.Transparent };
        Controls.Add(_lblAccStatus);
        y += 80;

        Lbl("FATURA KESİLECEK Mİ?", new Rectangle(16,y,220,14));
        _cmbInvoice = Combo(new Rectangle(16,y+16,220,24),"Evet","Hayır");
        Lbl("OTOMATİK MAİL", new Rectangle(246,y,220,14));
        _cmbMail = Combo(new Rectangle(246,y+16,220,24),"Hayır","Evet"); y+=52;

        Lbl("AKTİF", new Rectangle(16,y,220,14));
        _cmbActive = Combo(new Rectangle(16,y+16,220,24),"Aktif","Pasif"); y+=52;

        var btnSave   = Btn("Kaydet",new Rectangle(16,y,100,32),Accent,Color.White);
        var btnCancel = Btn("İptal", new Rectangle(126,y,90,32),BG3,FgSec);
        btnSave.Click   += (s,e) => Save();
        btnCancel.Click += (s,e) => { DialogResult=DialogResult.Cancel; Close(); };
        Controls.AddRange(new Control[]{btnSave,btnCancel});

        Load += async (s,e) => await LoadAll();
    }

    private async Task LoadAll()
    {
        await Task.WhenAll(LoadCategories(), LoadBankAccounts());
        if (_platform != null) Populate();
    }

    private async Task LoadCategories()
    {
        _lblCatStatus.Text = "Yükleniyor..."; _lblCatStatus.ForeColor = Amber;
        try
        {
            if (_parasut == null) throw new Exception("API bilgileri girilmemiş.");
            var sel = _cmbCategory.SelectedIndex > 0 ? _cmbCategory.SelectedItem?.ToString() : null;
            _categories = await _parasut.GetCategories();
            _cmbCategory.Items.Clear();
            _cmbCategory.Items.Add("— Kategori Seçin —");
            foreach (var (id, name) in _categories) _cmbCategory.Items.Add(name);
            _cmbCategory.SelectedIndex = 0;
            if (sel != null) { var i = _categories.FindIndex(c=>c.Name==sel); if(i>=0) _cmbCategory.SelectedIndex=i+1; }
            _lblCatStatus.Text = $"✓ {_categories.Count} kategori"; _lblCatStatus.ForeColor = Green;
        }
        catch (Exception ex) { _lblCatStatus.Text = "✕ " + ex.Message[..Math.Min(60,ex.Message.Length)]; _lblCatStatus.ForeColor = Red; }
    }

    private async Task LoadBankAccounts()
    {
        _lblAccStatus.Text = "Yükleniyor..."; _lblAccStatus.ForeColor = Amber;
        try
        {
            if (_parasut == null) throw new Exception("API bilgileri girilmemiş.");
            var sel = _cmbAccount.SelectedIndex > 0 ? _cmbAccount.SelectedItem?.ToString() : null;
            _bankAccounts = await _parasut.GetBankAccounts();
            _cmbAccount.Items.Clear();
            _cmbAccount.Items.Add("— Tahsilat Yapılmasın —");
            foreach (var (id, name, type) in _bankAccounts) _cmbAccount.Items.Add($"{name}  [{type}]");
            _cmbAccount.SelectedIndex = 0;
            if (sel != null) { var i = _bankAccounts.FindIndex(a=>$"{a.Name}  [{a.Type}]"==sel); if(i>=0) _cmbAccount.SelectedIndex=i+1; }
            _lblAccStatus.Text = $"✓ {_bankAccounts.Count} hesap"; _lblAccStatus.ForeColor = Green;
        }
        catch (Exception ex) { _lblAccStatus.Text = "✕ " + ex.Message[..Math.Min(60,ex.Message.Length)]; _lblAccStatus.ForeColor = Red; }
    }

    private void Populate()
    {
        if (_platform == null) return;
        _tPrefix.Text = _platform.Prefix;
        _cmbInvoice.SelectedIndex = _platform.InvoiceEnabled ? 0 : 1;
        _cmbMail.SelectedIndex    = _platform.MailEnabled    ? 1 : 0;
        _cmbActive.SelectedIndex  = _platform.Active         ? 0 : 1;

        if (!string.IsNullOrEmpty(_platform.CategoryId))
        { var i=_categories.FindIndex(c=>c.Id==_platform.CategoryId); if(i>=0) _cmbCategory.SelectedIndex=i+1; }
        else if (!string.IsNullOrEmpty(_platform.Category))
        { var i=_categories.FindIndex(c=>string.Equals(c.Name,_platform.Category,StringComparison.OrdinalIgnoreCase)); if(i>=0) _cmbCategory.SelectedIndex=i+1; }

        if (!string.IsNullOrEmpty(_platform.AccountParasutId))
        { var i=_bankAccounts.FindIndex(a=>a.Id==_platform.AccountParasutId); if(i>=0) _cmbAccount.SelectedIndex=i+1; }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_tPrefix.Text)) { MessageBox.Show("Ön ek zorunlu!","Hata"); return; }

        string catId="", catName="";
        if (_cmbCategory.SelectedIndex>0 && _cmbCategory.SelectedIndex-1<_categories.Count)
        { var c=_categories[_cmbCategory.SelectedIndex-1]; catId=c.Id; catName=c.Name; }

        string accParasutId=""; string accName="";
        if (_cmbAccount.SelectedIndex>0 && _cmbAccount.SelectedIndex-1<_bankAccounts.Count)
        { var a=_bankAccounts[_cmbAccount.SelectedIndex-1]; accParasutId=a.Id; accName=a.Name; }

        // DB'deki hesap ID'sini bul veya oluştur
        int? accountId = null;
        if (!string.IsNullOrEmpty(accParasutId))
        {
            var existing = _db.ListAccounts().FirstOrDefault(a=>a.ParasutId==accParasutId);
            if (existing != null) accountId = existing.Id;
            else
            {
                var acc = new Account { Name=accName, ParasutId=accParasutId, AccountType="kasa" };
                _db.SaveAccount(acc);
                accountId = _db.ListAccounts().FirstOrDefault(a=>a.ParasutId==accParasutId)?.Id;
            }
        }

        _db.SavePlatform(new Platform {
            Id             = _platform?.Id ?? 0,
            Prefix         = _tPrefix.Text.Trim(),
            Category       = catName,
            CategoryId     = catId,
            AccountId      = accountId,
            AccountParasutId = accParasutId,
            InvoiceEnabled = _cmbInvoice.SelectedIndex == 0,
            MailEnabled    = _cmbMail.SelectedIndex == 1,
            Active         = _cmbActive.SelectedIndex == 0,
        });
        DialogResult = DialogResult.OK; Close();
    }

    TextBox Field(string lbl, Rectangle bounds)
    {
        Lbl(lbl, new Rectangle(bounds.X,bounds.Y,bounds.Width,14));
        var t = new TextBox { Location=new Point(bounds.X,bounds.Y+16), Size=new Size(bounds.Width,24), BackColor=BG3, ForeColor=FgPri, BorderStyle=BorderStyle.FixedSingle, Font=new Font("Segoe UI",9f) };
        Controls.Add(t); return t;
    }
    ComboBox Combo(Rectangle bounds, params string[] items)
    {
        var c = new ComboBox { Location=new Point(bounds.X,bounds.Y), Size=new Size(bounds.Width,bounds.Height), BackColor=BG3, ForeColor=FgPri, FlatStyle=FlatStyle.Flat, DropDownStyle=ComboBoxStyle.DropDownList };
        c.Items.AddRange(items); c.SelectedIndex=0; Controls.Add(c); return c;
    }
    Button Btn(string text, Rectangle bounds, Color bg, Color fg)
    {
        var b = new Button { Text=text, Location=new Point(bounds.X,bounds.Y), Size=new Size(bounds.Width,bounds.Height), FlatStyle=FlatStyle.Flat, BackColor=bg, ForeColor=fg, Font=new Font("Segoe UI",9f,FontStyle.Bold), Cursor=Cursors.Hand };
        b.FlatAppearance.BorderSize=0; Controls.Add(b); return b;
    }
    void Lbl(string text, Rectangle bounds) =>
        Controls.Add(new Label { Text=text, Location=new Point(bounds.X,bounds.Y), Size=new Size(bounds.Width,bounds.Height), ForeColor=FgDim, Font=new Font("Consolas",7.5f), BackColor=Color.Transparent });
}

public class AccountForm : Form
{
    private readonly DatabaseService _db;
    private readonly Account? _account;
    private TextBox _tName=new(), _tPid=new();
    private ComboBox _cmbType=new();

    static readonly Color BG2   = Color.FromArgb(19,21,31);
    static readonly Color BG3   = Color.FromArgb(26,29,46);
    static readonly Color Accent = Color.FromArgb(76,110,245);
    static readonly Color FgPri = Color.FromArgb(221,225,237);
    static readonly Color FgSec = Color.FromArgb(120,128,160);
    static readonly Color FgDim = Color.FromArgb(61,66,96);

    public AccountForm(DatabaseService db, Account? account)
    {
        _db=db; _account=account;
        Text=account==null?"Hesap Ekle":"Hesap Düzenle";
        Size=new Size(420,240); BackColor=BG2; ForeColor=FgPri;
        FormBorderStyle=FormBorderStyle.FixedDialog;
        StartPosition=FormStartPosition.CenterParent;
        MaximizeBox=false; MinimizeBox=false;

        int y=16;
        _tName=Field("HESAP ADI",new Rectangle(16,y,370,44));y+=52;
        _tPid=Field("PARAŞüT HESAP ID",new Rectangle(16,y,180,44));
        Lbl("TÜR",new Rectangle(206,y,160,14));
        _cmbType=new ComboBox{Location=new Point(206,y+16),Size=new Size(180,24),BackColor=BG3,ForeColor=FgPri,FlatStyle=FlatStyle.Flat,DropDownStyle=ComboBoxStyle.DropDownList};
        _cmbType.Items.AddRange(new[]{"kasa","banka","kredi"});_cmbType.SelectedIndex=0;
        Controls.Add(_cmbType);y+=52;

        var btnSave=Btn("Kaydet",new Rectangle(16,y,90,30),Accent,Color.White);
        var btnCancel=Btn("İptal",new Rectangle(116,y,90,30),BG3,FgSec);
        btnSave.Click+=(s,e)=>Save();
        btnCancel.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};
        Controls.AddRange(new Control[]{btnSave,btnCancel});

        if(account!=null){_tName.Text=account.Name;_tPid.Text=account.ParasutId;_cmbType.SelectedItem=account.AccountType;}
    }

    void Save()
    {
        if(string.IsNullOrWhiteSpace(_tName.Text)||string.IsNullOrWhiteSpace(_tPid.Text))
        {MessageBox.Show("Ad ve ID zorunlu!","Hata");return;}
        _db.SaveAccount(new Account{Id=_account?.Id??0,Name=_tName.Text.Trim(),ParasutId=_tPid.Text.Trim(),AccountType=_cmbType.SelectedItem?.ToString()??"kasa"});
        DialogResult=DialogResult.OK;Close();
    }

    TextBox Field(string lbl,Rectangle bounds)
    {
        Lbl(lbl,new Rectangle(bounds.X,bounds.Y,bounds.Width,14));
        var t=new TextBox{Location=new Point(bounds.X,bounds.Y+16),Size=new Size(bounds.Width,24),BackColor=Color.FromArgb(26,29,46),ForeColor=Color.FromArgb(221,225,237),BorderStyle=BorderStyle.FixedSingle,Font=new Font("Segoe UI",9f)};
        Controls.Add(t);return t;
    }
    Button Btn(string text,Rectangle bounds,Color bg,Color fg)
    {
        var b=new Button{Text=text,Location=new Point(bounds.X,bounds.Y),Size=new Size(bounds.Width,bounds.Height),FlatStyle=FlatStyle.Flat,BackColor=bg,ForeColor=fg,Font=new Font("Segoe UI",9f,FontStyle.Bold),Cursor=Cursors.Hand};
        b.FlatAppearance.BorderSize=0;Controls.Add(b);return b;
    }
    void Lbl(string text,Rectangle bounds)=>
        Controls.Add(new Label{Text=text,Location=new Point(bounds.X,bounds.Y),Size=new Size(bounds.Width,bounds.Height),ForeColor=Color.FromArgb(61,66,96),Font=new Font("Consolas",7.5f),BackColor=Color.Transparent});
}
