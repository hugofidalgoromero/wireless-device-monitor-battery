using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Reflection;

[assembly: AssemblyTitle("Wireless Device Monitor Battery")]
[assembly: AssemblyProduct("Wireless Device Monitor Battery")]
[assembly: AssemblyDescription("Battery level of your Bluetooth devices on the Windows taskbar")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct DEVPROPKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct BLEND { public byte op, flags, alpha, format; }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern int CM_Get_Device_ID_List_SizeW(out uint len, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern int CM_Get_Device_ID_ListW(string filter, byte[] buffer, uint len, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern int CM_Locate_DevNodeW(out uint devInst, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] public static extern int CM_Get_DevNode_PropertyW(uint devInst, ref DEVPROPKEY key, out uint type, byte[] buf, ref uint size, uint flags);

    [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pdst, ref SIZE psize, IntPtr src, ref POINT psrc, int key, ref BLEND blend, int flags);
    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")] public static extern bool MoveLayered(IntPtr h, IntPtr dst, ref POINT pdst, IntPtr psize, IntPtr src, IntPtr psrc, int key, IntPtr blend, int flags);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
}

class Item
{
    public string Name, Id, Mac, Link;
    public int Pct, Kind;
    public bool Connected = true, Charging;
    public float X0, X1;
}

// panel flotante (ventana independiente, transparente, siempre encima, sin robar foco)
class Panel : Form
{
    public Panel()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x80 | 0x8 | 0x08000000 | 0x20; // LAYERED | TOOLWINDOW | TOPMOST | NOACTIVATE | TRANSPARENT
            return cp;
        }
    }
}

class Widget : Form
{
    public Widget()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
    }
    public IntPtr ParentWnd;
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            // ventana hija de la barra de tareas: sigue su orden Z, se oculta y aparece con ella
            var cp = base.CreateParams;
            cp.ExStyle = 0x80000 | 0x80 | 0x08000000; // LAYERED | TOOLWINDOW | NOACTIVATE
            return cp;
        }
    }
}

class App : ApplicationContext
{
    static readonly Native.DEVPROPKEY Battery = new Native.DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
    static readonly Native.DEVPROPKEY FriendlyName = new Native.DEVPROPKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
    static readonly Native.DEVPROPKEY DeviceDesc = new Native.DEVPROPKEY { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 2 };

    Widget form;
    IntPtr attached = IntPtr.Zero;
    float trail;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    List<Item> current = new List<Item>();
    string tipText;
    Panel panel;
    IntPtr panelBmp = IntPtr.Zero;
    int panelW, panelH, panelX, panelY, fadeAlpha;
    readonly System.Windows.Forms.Timer fade = new System.Windows.Forms.Timer();
    DateTime lastRefresh = DateTime.MinValue;
    IntPtr hbmp = IntPtr.Zero;
    int width, height, ticks;

    // idioma: el de Windows (es -> espanol, resto -> ingles); se puede forzar con  --lang es|en
    static bool ES = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
    static string T(string es, string en) { return ES ? es : en; }

    [STAThread]
    static void Main(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--lang") ES = args[i + 1].StartsWith("es", StringComparison.OrdinalIgnoreCase);
        bool created;
        using (var m = new Mutex(true, "WirelessDeviceMonitorBattery_single", out created))
        {
            if (!created) return;
            Native.SetProcessDPIAware();
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Batch;
            Application.EnableVisualStyles();
            try { Application.Run(new App()); }
            catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WirelessDeviceMonitorBattery_error.txt"), ex.ToString()); }
        }
    }

    App()
    {
        timer.Interval = 1000;
        timer.Tick += (s, e) =>
        {
            ticks++;
            CheckHover();
            bool panelOpen = panel != null && !panel.IsDisposed && panel.Visible;
            if (ticks % 300 == 0 || (dirty && !panelOpen)) { dirty = false; Refresh(); }
            else
            {
                Place();
                if (trimPending && !panelOpen) { trimPending = false; Trim(); }   // devolver memoria tras cerrar el panel
                else if (ticks % 120 == 0) Trim();
            }
        };
        timer.Start();
        Refresh();
    }

    // crea (o recrea, si el explorador se reinicia) el widget como hijo de la barra de tareas
    bool Attach()
    {
        IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return false;
        if (form != null && tray == attached && !form.IsDisposed) return true;
        if (form != null) { try { form.Dispose(); } catch { } }
        form = new Widget { ParentWnd = tray };
        form.ContextMenuStrip = menu;
        form.MouseMove += (s, e) =>
        {
            Item hit = null;
            foreach (var it in current) if (e.X >= it.X0 && e.X < it.X1) hit = it;
            if (hit == null) { HidePanel(); return; }
            if (hit.Name != tipText) ShowPanel(hit);
        };
        form.MouseLeave += (s, e) => HidePanel();
        form.Show();
        // convertir en ventana hija de la barra: sigue su orden Z, se oculta y aparece con ella
        int st = Native.GetWindowLong(form.Handle, -16);
        Native.SetWindowLong(form.Handle, -16, (st & ~unchecked((int)0x80000000)) | 0x40000000 | 0x10000000);
        Native.SetParent(form.Handle, tray);
        attached = tray;
        return true;
    }

    void Exit()
    {
        timer.Stop();
        HidePanel();
        if (panel != null) panel.Close();
        if (form != null) form.Close();
        if (hbmp != IntPtr.Zero) Native.DeleteObject(hbmp);
        ExitThread();
    }

    // ---------- lectura de bateria ----------
    static List<string> Ids(string enumerator)
    {
        var list = new List<string>();
        uint len;
        if (Native.CM_Get_Device_ID_List_SizeW(out len, enumerator, 0x101) != 0 || len < 2) return list;
        var buf = new byte[len * 2];
        if (Native.CM_Get_Device_ID_ListW(enumerator, buf, len, 0x101) != 0) return list;
        foreach (var s in Encoding.Unicode.GetString(buf).Split('\0'))
            if (s.Length > 0) list.Add(s);
        return list;
    }

    static byte[] Prop(uint inst, Native.DEVPROPKEY key)
    {
        uint type, size = 0;
        Native.CM_Get_DevNode_PropertyW(inst, ref key, out type, null, ref size, 0);
        if (size == 0) return null;
        var b = new byte[size];
        return Native.CM_Get_DevNode_PropertyW(inst, ref key, out type, b, ref size, 0) == 0 ? b : null;
    }

    static string Str(byte[] b) { return b == null ? null : Encoding.Unicode.GetString(b).TrimEnd('\0'); }

    static readonly Native.DEVPROPKEY IsConnected = new Native.DEVPROPKEY { fmtid = new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), pid = 15 };
    static readonly Native.DEVPROPKEY Charging = new Native.DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 3 };
    static readonly Native.DEVPROPKEY Category = new Native.DEVPROPKEY { fmtid = new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), pid = 90 };

    static int KindFromCategory(byte[] b)
    {
        if (b == null) return -1;
        string c = Encoding.Unicode.GetString(b).Replace('\0', ' ').ToLowerInvariant();
        if (c.Contains("mouse")) return 1;
        if (c.Contains("keyboard")) return 2;
        if (c.Contains("headset") || c.Contains("headphone") || c.Contains("audio") || c.Contains("speaker")) return 3;
        return -1;
    }

    static string MacFromId(string id)
    {
        int i = id.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
        if (i < 0 || id.Length < i + 16) return "—";
        string m = id.Substring(i + 4, 12).ToUpperInvariant();
        var sb = new StringBuilder();
        for (int k = 0; k < 12; k += 2) { if (k > 0) sb.Append(':'); sb.Append(m, k, 2); }
        return sb.ToString();
    }

    static int Kind(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("headset") || n.Contains("headphone") || n.Contains("cascos") || n.Contains("auricular") || n.Contains("buds") || n.Contains("airpods") || n.Contains("arctis") || n.Contains("wh-") || n.Contains("wf-") || n.Contains("earbuds")) return 3;
        if (n.Contains("keyboard") || n.Contains("teclado") || n.Contains("ajazz") || n.Contains("keychron") || n.Contains("bt-1")) return 2;
        if (n.Contains("mouse") || n.Contains("raton") || n.Contains("ratón") || n.Contains("g502") || n.Contains("logi") || n.Contains("mx ")) return 1;
        return 0;
    }

    static bool LightTheme()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k != null && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 0)) == 1;
        }
        catch { return false; }
    }

    void Refresh()
    {
        var items = new List<Item>();
        var all = Ids("BTHLE"); all.AddRange(Ids("BTHENUM"));
        foreach (var id in all)
        {
            uint inst;
            if (Native.CM_Locate_DevNodeW(out inst, id, 0) != 0) continue;
            var b = Prop(inst, Battery);
            if (b == null || b.Length < 1 || b[0] > 100) continue;
            string name = Str(Prop(inst, FriendlyName));
            if (string.IsNullOrEmpty(name)) name = Str(Prop(inst, DeviceDesc));
            if (string.IsNullOrEmpty(name)) name = id;
            if (items.Exists(x => x.Name == name)) continue;
            var item = new Item { Name = name, Id = id, Pct = b[0], Mac = MacFromId(id) };
            int k = KindFromCategory(Prop(inst, Category));
            item.Kind = k >= 0 ? k : Kind(name);
            item.Link = id.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase) ? "Bluetooth LE" : "Bluetooth";
            var cn = Prop(inst, IsConnected); if (cn != null && cn.Length > 0) item.Connected = cn[0] != 0;
            var ch = Prop(inst, Charging); if (ch != null && ch.Length > 0) item.Charging = ch[0] != 0;
            items.Add(item);
        }
        lastRefresh = DateTime.Now;

        menu.Items.Clear();
        foreach (var it in items) menu.Items.Add(it.Name + ": " + it.Pct + "%").Enabled = false;
        if (items.Count > 0) menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T("Actualizar ahora", "Refresh now"), null, (s, e) => Refresh());
        menu.Items.Add(T("Salir", "Exit"), null, (s, e) => Exit());

        Render(items.FindAll(x => x.Connected)); // el widget solo muestra los dispositivos online
        Place();
        Trim();
    }

    static void Trim()
    {
        GC.Collect();
        Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
    }

    // ---------- dibujo ----------
    static GraphicsPath Round(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // glifo dibujado en una caja de 24x24 unidades
    static void Glyph(Graphics g, int kind, Color col, float ox, float oy, float s, float pw = 2f)
    {
        var st = g.Save();
        g.TranslateTransform(ox, oy);
        g.ScaleTransform(s / 24f, s / 24f);
        using (var pen = new Pen(col, pw) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var br = new SolidBrush(col))
        {
            if (kind == 1)
            {   // raton: cuerpo en pastilla, botones y rueda
                using (var p = Round(5.2f, 1.8f, 13.6f, 20.4f, 6.8f)) g.DrawPath(pen, p);
                g.DrawLine(pen, 5.2f, 10.6f, 18.8f, 10.6f);
                g.DrawLine(pen, 12f, 1.8f, 12f, 4.2f);
                using (var p = Round(10.9f, 4.9f, 2.2f, 3.8f, 1.1f)) g.FillPath(br, p);
            }
            else if (kind == 3)
            {   // auriculares: diadema y dos almohadillas
                g.DrawArc(pen, 3.6f, 2.6f, 16.8f, 16.8f, 180, 180);
                g.DrawLine(pen, 3.6f, 11f, 3.6f, 14.5f); g.DrawLine(pen, 20.4f, 11f, 20.4f, 14.5f);
                using (var p = Round(1.8f, 13.4f, 5.2f, 8.2f, 2.3f)) g.FillPath(br, p);
                using (var p = Round(17f, 13.4f, 5.2f, 8.2f, 2.3f)) g.FillPath(br, p);
            }
            else if (kind == 2)
            {   // teclado: marco, dos filas de teclas y barra espaciadora
                using (var p = Round(1.4f, 5.2f, 21.2f, 13.6f, 3.2f)) g.DrawPath(pen, p);
                for (int i = 0; i < 5; i++) using (var k = Round(4.2f + i * 3.5f, 8.3f, 2.2f, 2.2f, 0.6f)) g.FillPath(br, k);
                for (int i = 0; i < 4; i++) using (var k = Round(5.9f + i * 3.5f, 11.7f, 2.2f, 2.2f, 0.6f)) g.FillPath(br, k);
                using (var k = Round(7.4f, 15f, 9.2f, 1.9f, 0.95f)) g.FillPath(br, k);
            }
            else
            {   // bateria generica
                using (var p = Round(1.8f, 6.4f, 17.6f, 11.2f, 2.6f)) g.DrawPath(pen, p);
                using (var k = Round(20.4f, 10f, 2f, 4f, 1f)) g.FillPath(br, k);
                using (var k = Round(4.6f, 9.2f, 8f, 5.6f, 1.2f)) g.FillPath(br, k);
            }
        }
        g.Restore(st);
    }

    // ---- iconos y texto como contornos vectoriales: nitidos a cualquier resolucion, sin efectos ----
    static FontFamily Fam(params string[] names)
    {
        foreach (var n in names) { try { return new FontFamily(n); } catch { } }
        return FontFamily.GenericSansSerif;
    }

    // centra el contorno en (cx,cy); si maxSide > 0 lo escala para que su lado mayor mida maxSide
    static void CenterPath(GraphicsPath p, float cx, float cy, float maxSide)
    {
        var b = p.GetBounds();
        using (var m = new Matrix())
        {
            m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
            if (maxSide > 0) { float sc = maxSide / Math.Max(b.Width, b.Height); m.Scale(sc, sc, MatrixOrder.Append); }
            m.Translate(cx, cy, MatrixOrder.Append);
            p.Transform(m);
        }
    }

    static void MovePath(GraphicsPath p, float dx, float dy)
    {
        using (var m = new Matrix()) { m.Translate(dx, dy); p.Transform(m); }
    }

    // iconos de la fuente del sistema Segoe Fluent Icons (Windows 11) o Segoe MDL2 Assets (Windows 10)
    static GraphicsPath IconPath(int kind, float cx, float cy, float size)
    {
        string ch = kind == 1 ? "" : kind == 2 ? "" : kind == 3 ? "" : ""; // raton, teclado, auriculares, bluetooth
        var p = new GraphicsPath();
        using (var ff = Fam("Segoe Fluent Icons", "Segoe MDL2 Assets")) p.AddString(ch, ff, 0, 100f, PointF.Empty, StringFormat.GenericTypographic);
        CenterPath(p, cx, cy, size);
        return p;
    }

    static GraphicsPath TextPath(string s, float em)
    {
        var p = new GraphicsPath();
        using (var ff = Fam("Bahnschrift SemiBold", "Segoe UI Semibold", "Segoe UI")) p.AddString(s, ff, 0, em, PointF.Empty, StringFormat.GenericTypographic);
        return p;
    }

    void Render(List<Item> items)
    {
        if (hbmp != IntPtr.Zero) { Native.DeleteObject(hbmp); hbmp = IntPtr.Zero; }
        width = 0;
        current = items;
        if (!Attach()) return;
        if (items.Count == 0) { form.Hide(); return; }

        Native.RECT tb;
        Native.GetWindowRect(Native.FindWindow("Shell_TrayWnd", null), out tb);
        int h = Math.Max(24, tb.bottom - tb.top);
        float glyph = h * 0.5f, fs = h * 0.3f, gap = h * 0.2f, pad = h * 0.12f;
        bool light = LightTheme();

        // pasada 1: contornos de texto e icono, centrados en (0,0), para medir su ancho real
        var tps = new List<GraphicsPath>(); var ips = new List<GraphicsPath>();
        var tws = new List<float>(); var iws = new List<float>();
        float gapTI = gap * 0.75f, gapItems = gap * 1.7f, total = pad;
        foreach (var it in items)
        {
            var tp = TextPath(it.Pct + "%", fs * 1.08f); CenterPath(tp, 0, 0, 0);
            var ip = IconPath(it.Kind, 0, 0, glyph);
            tps.Add(tp); ips.Add(ip);
            tws.Add(tp.GetBounds().Width); iws.Add(ip.GetBounds().Width);
            total += tws[tws.Count - 1] + gapTI + iws[iws.Count - 1] + gapItems;
        }
        width = (int)Math.Ceiling(total);
        height = h;
        trail = gapItems;

        using (var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(1, 0, 0, 0)); // casi transparente: capta el clic derecho
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float x = pad, cy = height / 2f;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float itemW = tws[i] + gapTI + iws[i];
                it.X0 = x - gapItems * 0.3f;
                it.X1 = x + itemW + gapItems * 0.5f;
                // mismos colores de nivel que el panel (versiones mas oscuras en tema claro)
                Color pc;
                if (!it.Connected) pc = light ? Color.FromArgb(110, 120, 135) : Dim;
                else if (it.Charging) pc = light ? Color.FromArgb(10, 150, 80) : Grn;
                else if (it.Pct <= 20) pc = light ? Color.FromArgb(205, 30, 60) : Red;
                else if (it.Pct <= 50) pc = light ? Color.FromArgb(185, 135, 0) : Yel;
                else pc = light ? Color.FromArgb(0, 135, 160) : Cyan;
                // texto e icono centrados en el mismo eje vertical (centro de su trazo real)
                MovePath(tps[i], x + tws[i] / 2f, cy);
                MovePath(ips[i], x + tws[i] + gapTI + iws[i] / 2f, cy);
                // porcentaje con color de nivel, icono blanco (oscuro en tema claro)
                using (var b = new SolidBrush(pc)) g.FillPath(b, tps[i]);
                using (var b = new SolidBrush(light ? Color.FromArgb(32, 32, 32) : Color.White)) g.FillPath(b, ips[i]);
                tps[i].Dispose(); ips[i].Dispose();
                x += itemW + gapItems;
            }
            hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        }

        IntPtr screen = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(screen);
        IntPtr old = Native.SelectObject(mem, hbmp);
        var pt = Position(attached);
        var sz = new Native.SIZE { cx = width, cy = height };
        var src = new Native.POINT();
        var bl = new Native.BLEND { op = 0, flags = 0, alpha = 255, format = 1 };
        if (!form.Visible) form.Show();
        Native.UpdateLayeredWindow(form.Handle, screen, ref pt, ref sz, mem, ref src, 0, ref bl, 2);
        lastX = pt.x;
        Native.SelectObject(mem, old);
        Native.DeleteDC(mem);
        Native.ReleaseDC(IntPtr.Zero, screen);
    }

    // ---------- panel cyberpunk ----------
    static readonly Color Cyan = Color.FromArgb(0, 238, 255), Mag = Color.FromArgb(255, 32, 160), Yel = Color.FromArgb(255, 225, 0),
        Grn = Color.FromArgb(60, 255, 140), Red = Color.FromArgb(255, 64, 96), Dim = Color.FromArgb(120, 150, 175), Txt = Color.FromArgb(215, 238, 248);

    static Color A(Color c, int a) { return Color.FromArgb(a, c); }

    Bitmap DrawPanel(Item it, float s)
    {
        const int W = 300, H = 212, M = 12;
        var bmp = new Bitmap((int)Math.Ceiling((W + 2 * M) * s), (int)Math.Ceiling((H + 2 * M) * s), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.ScaleTransform(s, s);
            g.TranslateTransform(M, M);

            Color pc = it.Charging ? Grn : it.Pct <= 20 ? Red : it.Pct <= 50 ? Yel : Cyan;
            if (!it.Connected) pc = Dim;

            var pts = new[] { new PointF(0, 0), new PointF(W - 16, 0), new PointF(W, 16), new PointF(W, H), new PointF(16, H), new PointF(0, H - 16) };
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(pts);
                // resplandor exterior
                for (int k = 6; k >= 1; k--) using (var gp = new Pen(A(Cyan, 12), k * 2.2f) { LineJoin = LineJoin.Round }) g.DrawPath(gp, path);
                using (var bg = new SolidBrush(Color.FromArgb(238, 8, 10, 20))) g.FillPath(bg, path);
                g.SetClip(path);
                using (var gr = new LinearGradientBrush(new RectangleF(0, 0, W, 90), A(Cyan, 46), A(Cyan, 0), 90f)) g.FillRectangle(gr, 0, 0, W, 90);
                using (var gr = new LinearGradientBrush(new RectangleF(0, H - 50, W, 50), A(Mag, 0), A(Mag, 26), 90f)) g.FillRectangle(gr, 0, H - 50, W, 50);
                using (var sl = new Pen(Color.FromArgb(34, 0, 0, 0), 1f)) for (int y = 1; y < H; y += 3) g.DrawLine(sl, 0, y, W, y);
                g.ResetClip();
                using (var bd = new Pen(A(Cyan, 235), 1.4f) { LineJoin = LineJoin.Miter }) g.DrawPath(bd, path);
            }
            // acentos magenta / cian
            using (var b = new SolidBrush(Mag)) { g.FillRectangle(b, 0, 0, 54, 3); g.FillRectangle(b, W - 3, H - 22, 3, 22); }
            using (var b = new SolidBrush(Cyan)) { g.FillRectangle(b, 60, 0, 8, 3); g.FillRectangle(b, 16, H - 3, 30, 3); }

            using (var fSm = new Font("Consolas", 9f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fTi = new Font("Consolas", 8f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fRow = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fRowB = new Font("Consolas", 10f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fName = new Font("Bahnschrift SemiBold", 20f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fBig = new Font("Bahnschrift SemiBold", 46f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fPct = new Font("Bahnschrift SemiBold", 17f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                // cabecera
                using (var b = new SolidBrush(A(Cyan, 190))) g.DrawString(T("// ENLACE ACTIVO", "// ACTIVE LINK"), fSm, b, 16, 13, StringFormat.GenericTypographic);
                string st = it.Connected ? "ONLINE" : "OFFLINE";
                Color sc = it.Connected ? Grn : Red;
                float sw = g.MeasureString(st, fSm, 200, StringFormat.GenericTypographic).Width;
                using (var b = new SolidBrush(sc)) { g.DrawString(st, fSm, b, W - 26 - sw, 13, StringFormat.GenericTypographic); g.FillEllipse(b, W - 26 - sw - 11, 14.5f, 6, 6); }
                using (var b = new SolidBrush(A(sc, 60))) g.FillEllipse(b, W - 26 - sw - 14, 11.5f, 12, 12);

                // nombre con efecto glitch
                var nr = new RectangleF(16, 30, W - 32, 28);
                using (var b = new SolidBrush(A(Mag, 170))) g.DrawString(it.Name, fName, b, new RectangleF(nr.X - 1.6f, nr.Y, nr.Width, nr.Height), sf);
                using (var b = new SolidBrush(A(Cyan, 130))) g.DrawString(it.Name, fName, b, new RectangleF(nr.X + 1.6f, nr.Y, nr.Width, nr.Height), sf);
                using (var b = new SolidBrush(Color.FromArgb(245, 250, 255))) g.DrawString(it.Name, fName, b, nr, sf);

                // etiqueta de tipo + icono
                string[] tipos = ES ? new[] { "DISPOSITIVO", "RATÓN", "TECLADO", "AUDIO" } : new[] { "DEVICE", "MOUSE", "KEYBOARD", "AUDIO" };
                string tipo = tipos[Math.Max(0, Math.Min(3, it.Kind))];
                float tw = g.MeasureString(tipo, fSm, 200, StringFormat.GenericTypographic).Width;
                using (var p = new Pen(A(Mag, 220), 1f)) g.DrawRectangle(p, 16, 60, tw + 12, 15);
                using (var b = new SolidBrush(Mag)) g.DrawString(tipo, fSm, b, 22, 63, StringFormat.GenericTypographic);
                using (var ip = IconPath(it.Kind, W - 50 + 15, 48 + 15, 30))
                using (var b = new SolidBrush(Color.White)) g.FillPath(b, ip);

                // porcentaje grande
                string num = it.Pct.ToString();
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0) using (var b = new SolidBrush(A(pc, 28))) g.DrawString(num, fBig, b, 14 + dx * 1.5f, 80 + dy * 1.5f, StringFormat.GenericTypographic);
                using (var b = new SolidBrush(pc)) g.DrawString(num, fBig, b, 14, 80, StringFormat.GenericTypographic);
                float nw = g.MeasureString(num, fBig, 400, StringFormat.GenericTypographic).Width;
                using (var b = new SolidBrush(A(pc, 170))) g.DrawString("%", fPct, b, 14 + nw + 1, 98, StringFormat.GenericTypographic);

                // barra segmentada
                const float bx = 128, by = 92, bw = W - 16 - 128, bh = 26;
                int segs = 20; float gap = 2.2f, sgw = (bw - gap * (segs - 1)) / segs;
                int filled = it.Pct <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(it.Pct / 5.0));
                for (int i = 0; i < segs; i++)
                {
                    float x = bx + i * (sgw + gap);
                    if (i < filled)
                    {
                        using (var b = new SolidBrush(A(pc, 40))) g.FillRectangle(b, x - 1.2f, by - 1.5f, sgw + 2.4f, bh + 3);
                        using (var b = new SolidBrush(pc)) g.FillRectangle(b, x, by, sgw, bh);
                    }
                    else using (var b = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) g.FillRectangle(b, x, by, sgw, bh);
                }
                using (var b = new SolidBrush(A(Dim, 230))) g.DrawString(T("NIVEL DE ENERGÍA", "POWER LEVEL"), fTi, b, bx, 123, StringFormat.GenericTypographic);
                if (it.Charging)
                {
                    float lx = W - 16 - 66;
                    using (var b = new SolidBrush(Grn))
                    {
                        g.FillPolygon(b, new[] { new PointF(lx + 5, 122), new PointF(lx, 129), new PointF(lx + 3.6f, 129), new PointF(lx + 2.4f, 135), new PointF(lx + 8, 127.5f), new PointF(lx + 4.4f, 127.5f) });
                        g.DrawString(T("CARGANDO", "CHARGING"), fTi, b, lx + 11, 123, StringFormat.GenericTypographic);
                    }
                }

                // separador
                using (var p = new Pen(A(Cyan, 70), 1f)) g.DrawLine(p, 16, 150, W - 16, 150);
                using (var b = new SolidBrush(A(Cyan, 220))) g.FillRectangle(b, 16, 149, 34, 2);

                // datos
                string[] lab = ES ? new[] { "ENLACE", "DIRECCIÓN", "FUENTE" } : new[] { "LINK", "ADDRESS", "SOURCE" };
                string[] val = { it.Link, it.Mac, it.Charging ? T("CARGADOR", "CHARGER") : T("BATERÍA", "BATTERY") };
                for (int i = 0; i < lab.Length; i++)
                {
                    float y = 160 + i * 15.5f;
                    using (var b = new SolidBrush(A(Dim, 235))) g.DrawString(lab[i], fRow, b, 16, y, StringFormat.GenericTypographic);
                    using (var b = new SolidBrush(i == 2 && it.Charging ? Grn : Txt)) g.DrawString(val[i], fRowB, b, 112, y, StringFormat.GenericTypographic);
                }
                // decoracion inferior
                using (var p = new Pen(A(Mag, 200), 1.6f)) for (int i = 0; i < 3; i++) g.DrawLine(p, W - 40 + i * 6, H - 10, W - 34 + i * 6, H - 18);
            }
        }
        return bmp;
    }

    bool dirty, trimPending;
    int lastX = int.MinValue;

    // lectura ligera de un solo dispositivo (sin redibujar el widget)
    void UpdateItem(Item it)
    {
        uint inst;
        if (Native.CM_Locate_DevNodeW(out inst, it.Id, 0) != 0) return;
        var b = Prop(inst, Battery);
        if (b != null && b.Length > 0 && b[0] <= 100) { if (b[0] != it.Pct) dirty = true; it.Pct = b[0]; }
        var cn = Prop(inst, IsConnected); if (cn != null && cn.Length > 0) { bool c = cn[0] != 0; if (c != it.Connected) dirty = true; it.Connected = c; }
        var ch = Prop(inst, Charging); if (ch != null && ch.Length > 0) it.Charging = ch[0] != 0;
    }

    // seguridad: si el cursor ya no esta sobre el widget, ocultar el panel
    void CheckHover()
    {
        if (panel == null || panel.IsDisposed || !panel.Visible || form == null) return;
        Native.RECT wr; Native.GetWindowRect(form.Handle, out wr);
        var p = Control.MousePosition;
        if (p.X < wr.left || p.X >= wr.right || p.Y < wr.top || p.Y >= wr.bottom) HidePanel();
    }

    void ShowPanel(Item it)
    {
        if ((DateTime.Now - lastRefresh).TotalSeconds > 60) { UpdateItem(it); lastRefresh = DateTime.Now; }
        tipText = it.Name;
        float s = Math.Max(0.8f, height / 48f);
        Bitmap bmp = DrawPanel(it, s);
        if (panelBmp != IntPtr.Zero) Native.DeleteObject(panelBmp);
        panelW = bmp.Width; panelH = bmp.Height;
        panelBmp = bmp.GetHbitmap(Color.FromArgb(0));
        bmp.Dispose();

        Native.RECT wr; Native.GetWindowRect(form.Handle, out wr);
        int cx = wr.left + (int)((it.X0 + it.X1) / 2);
        int sw = Screen.PrimaryScreen.Bounds.Width;
        panelX = Math.Max(4, Math.Min(sw - panelW - 4, cx - panelW / 2));
        panelY = wr.top - panelH + (int)(6 * s);

        if (panel == null || panel.IsDisposed) panel = new Panel();
        fadeAlpha = 0;
        PanelDraw();
        if (!panel.Visible) panel.Show();
        Native.SetWindowPos(panel.Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x13);
        fade.Interval = 16;
        fade.Tick -= FadeStep; fade.Tick += FadeStep;
        fade.Start();
    }

    void FadeStep(object sender, EventArgs e)
    {
        fadeAlpha += 51;
        if (fadeAlpha >= 255) { fadeAlpha = 255; fade.Stop(); }
        PanelDraw();
    }

    void PanelDraw()
    {
        if (panel == null || panelBmp == IntPtr.Zero) return;
        IntPtr screen = Native.GetDC(IntPtr.Zero), mem = Native.CreateCompatibleDC(screen);
        IntPtr old = Native.SelectObject(mem, panelBmp);
        var pt = new Native.POINT { x = panelX, y = panelY - (int)((255 - fadeAlpha) / 255f * 8) }; // sube suavemente
        var sz = new Native.SIZE { cx = panelW, cy = panelH };
        var src = new Native.POINT();
        var bl = new Native.BLEND { op = 0, flags = 0, alpha = (byte)fadeAlpha, format = 1 };
        Native.UpdateLayeredWindow(panel.Handle, screen, ref pt, ref sz, mem, ref src, 0, ref bl, 2);
        Native.SelectObject(mem, old);
        Native.DeleteDC(mem);
        Native.ReleaseDC(IntPtr.Zero, screen);
    }

    void HidePanel()
    {
        tipText = null;
        fade.Stop();
        if (panel != null && !panel.IsDisposed && panel.Visible) panel.Hide();
        if (panelBmp != IntPtr.Zero) { Native.DeleteObject(panelBmp); panelBmp = IntPtr.Zero; trimPending = true; }
    }

    // posicion relativa a la barra de tareas (el widget es su ventana hija)
    Native.POINT Position(IntPtr tray)
    {
        Native.RECT tb; Native.GetWindowRect(tray, out tb);
        IntPtr notify = Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        Native.RECT nr;
        int right = tb.right - 320;
        if (notify != IntPtr.Zero && Native.GetWindowRect(notify, out nr) && nr.left > tb.left) right = nr.left;
        return new Native.POINT { x = right - tb.left - width + (int)trail - 8, y = 0 };
    }

    void Place()
    {
        if (current.Count == 0) return;
        IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return;
        Native.RECT tb; Native.GetWindowRect(tray, out tb);
        // el explorador se reinicio o la barra cambio de tamano: redibujar
        if (tray != attached || width == 0 || tb.bottom - tb.top != height) { Render(current); return; }
        var pt = Position(tray);
        // solo tocar la ventana si algo cambio (mover) o si algo se puso por encima (z-order)
        if (pt.x != lastX) { Native.MoveLayered(form.Handle, IntPtr.Zero, ref pt, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, 0); lastX = pt.x; }
        if (Native.GetWindow(form.Handle, 3) != IntPtr.Zero) // GW_HWNDPREV: hay algo encima
            Native.SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x13); // HWND_TOP entre los hijos de la barra
    }
}
