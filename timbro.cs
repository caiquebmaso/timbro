// Timbro: equalizador simples para o Windows, com motor de som próprio (motor\timbro_apo.dll).
// Compila com o csc do .NET Framework (C# 5): ver timbro.bat.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Timbro")]
[assembly: System.Reflection.AssemblyProduct("Timbro")]
[assembly: System.Reflection.AssemblyVersion("0.1.0.0")]

namespace Timbro
{
    // ---------------------------------------------------------------- COM do áudio do Windows
    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT { public short vt; public short r1, r2, r3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int i, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PROPERTYKEY key);
    }

    // Interface não documentada que o próprio Windows usa para trocar a saída padrão (Windows 10 e 11).
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c3bc9")]
    class PolicyConfigCo { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visivel);
    }

    // Versão antiga da mesma interface (Vista em diante), registrada em todo Windows.
    [ComImport, Guid("294935CE-F637-4E7C-A41B-AB255460B862")]
    class PolicyConfigVistaCo { }

    [ComImport, Guid("568b9108-44bf-40b4-9006-86afe5b5a620"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfigVista
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visivel);
    }

    class Notificador : IMMNotificationClient
    {
        readonly Action aviso;
        public Notificador(Action a) { aviso = a; }
        public void OnDeviceStateChanged(string id, int state) { aviso(); }
        public void OnDeviceAdded(string id) { aviso(); }
        public void OnDeviceRemoved(string id) { aviso(); }
        public void OnDefaultDeviceChanged(int flow, int role, string id) { if (flow == 0 && role == 0) aviso(); }
        public void OnPropertyValueChanged(string id, PROPERTYKEY key) { }
    }

    class Dispositivo
    {
        public string Id, Nome, Guid;
        public Guid Container;
        public override string ToString() { return Nome; }
    }

    static class Audio
    {
        static readonly IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);

        public static string GuidDe(string id)
        {
            int i = id.LastIndexOf('{');
            return (i >= 0 ? id.Substring(i) : id).Trim('{', '}').ToLowerInvariant();
        }

        static string Nome(IMMDevice d) { return Texto(d, "a45c254e-df1c-4efd-8020-67d146a850e0", 14) ?? "?"; }

        static string Texto(IMMDevice d, string fmt, int pid)
        {
            IPropertyStore ps;
            if (d.OpenPropertyStore(0, out ps) != 0) return null;
            var key = new PROPERTYKEY { fmtid = new Guid(fmt), pid = pid };
            PROPVARIANT v;
            if (ps.GetValue(ref key, out v) != 0) return null;
            string s = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : null;
            PropVariantClear(ref v);
            return s;
        }

        // Aparelho físico ao qual o ponto de áudio pertence (o mesmo fone tem estéreo e viva-voz).
        static Guid Container(IMMDevice d)
        {
            IPropertyStore ps;
            if (d.OpenPropertyStore(0, out ps) != 0) return Guid.Empty;
            var key = new PROPERTYKEY { fmtid = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), pid = 2 };
            PROPVARIANT v;
            if (ps.GetValue(ref key, out v) != 0) return Guid.Empty;
            Guid g = v.vt == 72 && v.p != IntPtr.Zero ? (Guid)Marshal.PtrToStructure(v.p, typeof(Guid)) : Guid.Empty;
            PropVariantClear(ref v);
            return g;
        }

        // Pontos de viva-voz (Bluetooth Hands-Free: som e microfone) do mesmo fone, em qualquer estado.
        public static List<KeyValuePair<string, int>> VivaVoz(Dispositivo fone)
        {
            var r = new List<KeyValuePair<string, int>>();
            if (fone == null || fone.Container == Guid.Empty) return r;
            for (int fluxo = 0; fluxo < 2; fluxo++)
            {
                IMMDeviceCollection col;
                if (en.EnumAudioEndpoints(fluxo, 0xF, out col) != 0) continue;
                int n; col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d; col.Item(i, out d);
                    if (Container(d) != fone.Container) continue;
                    if (!"BTHHFENUM".Equals(Texto(d, "a45c254e-df1c-4efd-8020-67d146a850e0", 24), StringComparison.OrdinalIgnoreCase)) continue;
                    string id; d.GetId(out id);
                    int s; d.GetState(out s);
                    r.Add(new KeyValuePair<string, int>(id, s));
                }
            }
            return r;
        }

        // Dispositivo do Gerenciador de Dispositivos dono do ponto de áudio (ex.: BTHHFENUM\BTHHFPAUDIO\...).
        public static string Instancia(string pontoId)
        {
            IMMDevice d;
            if (en.GetDevice(pontoId, out d) != 0) return null;
            string s = Texto(d, "b3f8fa53-0004-438e-9003-51a46e139bfc", 2);
            if (s == null) return null;
            int i = s.IndexOf("}.");
            return i >= 0 ? s.Substring(i + 2) : s;
        }
        // Liga ou desliga um ponto de áudio, como o "Desabilitar" do painel de Som.
        public static bool Mostrar(string id, bool visivel)
        {
            try { return ((IPolicyConfig)new PolicyConfigCo()).SetEndpointVisibility(id, visivel ? 1 : 0) == 0; }
            catch { }
            try { return ((IPolicyConfigVista)new PolicyConfigVistaCo()).SetEndpointVisibility(id, visivel ? 1 : 0) == 0; }
            catch { return false; }
        }

        public static List<Dispositivo> Saidas()
        {
            var lista = new List<Dispositivo>();
            IMMDeviceCollection col;
            if (en.EnumAudioEndpoints(0, 1, out col) != 0) return lista;
            int n; col.GetCount(out n);
            for (int i = 0; i < n; i++)
            {
                IMMDevice d; col.Item(i, out d);
                string id; d.GetId(out id);
                lista.Add(new Dispositivo { Id = id, Nome = Nome(d), Guid = GuidDe(id), Container = Container(d) });
            }
            return lista.OrderBy(x => x.Nome).ToList();
        }

        public static string Padrao()
        {
            IMMDevice d;
            if (en.GetDefaultAudioEndpoint(0, 0, out d) != 0) return null;
            string id; d.GetId(out id);
            return id;
        }

        public static bool Ativo(string id)
        {
            IMMDevice d;
            if (string.IsNullOrEmpty(id) || en.GetDevice(id, out d) != 0) return false;
            int s; d.GetState(out s);
            return s == 1;
        }

        public static void TornarPadrao(string id)
        {
            try
            {
                var pc = (IPolicyConfig)new PolicyConfigCo();
                for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);
                return;
            }
            catch { }
            try
            {
                var pv = (IPolicyConfigVista)new PolicyConfigVistaCo();
                for (int role = 0; role < 3; role++) pv.SetDefaultEndpoint(id, role);
            }
            catch { }
        }

        public static void Avisar(IMMNotificationClient c) { en.RegisterEndpointNotificationCallback(c); }
    }

    // ---------------------------------------------------------------- configuração
    class Perfil
    {
        public string Guid, Nome;
        public double[] Ganho = new double[10];
    }

    class Config
    {
        public Dictionary<string, Perfil> Perfis = new Dictionary<string, Perfil>(StringComparer.OrdinalIgnoreCase);
        public bool Ligado = true;
        public string SaidaFixa = "";
        public HashSet<string> Estereo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Pasta { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Timbro"); } }
        static string Arquivo { get { return Path.Combine(Pasta, "timbro.ini"); } }

        public Perfil PerfilDe(Dispositivo d)
        {
            Perfil p;
            if (!Perfis.TryGetValue(d.Guid, out p)) { p = new Perfil { Guid = d.Guid, Nome = d.Nome }; Perfis[d.Guid] = p; }
            p.Nome = d.Nome;
            return p;
        }

        public static Config Carregar()
        {
            var c = new Config();
            if (!File.Exists(Arquivo)) return c;
            foreach (var linha in File.ReadAllLines(Arquivo, Encoding.UTF8))
            {
                int i = linha.IndexOf('=');
                if (i < 0) continue;
                string k = linha.Substring(0, i).Trim(), v = linha.Substring(i + 1).Trim();
                if (k == "ligado") c.Ligado = v == "1";
                else if (k == "saida_fixa") c.SaidaFixa = v;
                else if (k == "estereo") foreach (var item in v.Split(',')) if (item.Trim() != "") c.Estereo.Add(item.Trim());
                else if (k == "perfil")
                {
                    var p = v.Split('|');
                    if (p.Length < 3) continue;
                    var g = p[2].Split(',').Select(x => double.Parse(x, Inv)).ToArray();
                    if (g.Length != 10) continue;
                    c.Perfis[p[0]] = new Perfil { Guid = p[0], Nome = p[1], Ganho = g };
                }
            }
            return c;
        }

        public void Salvar()
        {
            Directory.CreateDirectory(Pasta);
            var sb = new StringBuilder();
            sb.AppendLine("ligado=" + (Ligado ? "1" : "0"));
            sb.AppendLine("saida_fixa=" + SaidaFixa);
            sb.AppendLine("estereo=" + string.Join(",", Estereo));
            foreach (var p in Perfis.Values)
                sb.AppendLine("perfil=" + p.Guid + "|" + p.Nome.Replace("|", "/") + "|" + string.Join(",", p.Ganho.Select(x => x.ToString("0.0", Inv))));
            File.WriteAllText(Arquivo, sb.ToString(), new UTF8Encoding(false));
        }
    }

    // ---------------------------------------------------------------- motor de som próprio (APO timbro_apo.dll)
    static class Motor
    {
        public const string Clsid = "{162BAFBE-06D6-43A3-BE19-8272BD6BCF80}";
        const string FxEfx = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},7";
        const string ModosEfx = "{d3993a3f-99c2-4402-b5ec-a92a0367664b},7";
        const string SysFx = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";
        const string ModoPadrao = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";
        const string Render = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\";
        const string Chave = @"SOFTWARE\Timbro";
        const string Copia = @"SOFTWARE\TimbroBackup";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static RegistryKey Hklm { get { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); } }
        public static string PastaPrograma
        {
            get
            {
                string pf = Environment.GetEnvironmentVariable("ProgramW6432");
                if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                return Path.Combine(pf, "Timbro");
            }
        }
        public static string DllOrigem { get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "timbro_apo.dll"); } }

        // ------------------------------------------------ leitura (qualquer usuário)
        public static bool InstaladoNo(string guid)
        {
            try
            {
                using (var k = Hklm.OpenSubKey(Render + "{" + guid + "}\\FxProperties"))
                    return k != null && Clsid.Equals(k.GetValue(FxEfx) as string, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static string Estado(string guid)
        {
            try
            {
                using (var k = Hklm.OpenSubKey(Chave + "\\Estado")) return k == null ? null : k.GetValue(guid) as string;
            }
            catch { return null; }
        }

        public static string EstadosTodos()
        {
            try
            {
                using (var k = Hklm.OpenSubKey(Chave + "\\Estado"))
                    return k == null ? "" : string.Join("; ", k.GetValueNames().Select(n => n + " = " + k.GetValue(n)));
            }
            catch { return ""; }
        }

        // Grava os ajustes que o motor lê. Sem o motor instalado a chave não existe e nada acontece.
        public static void EscreverParametros(Config c)
        {
            try
            {
                using (var k = Hklm.OpenSubKey(Chave, true))
                {
                    if (k == null) return;
                    k.SetValue("Ligado", c.Ligado ? 1 : 0, RegistryValueKind.DWord);
                    using (var p = k.CreateSubKey("Perfis"))
                        foreach (var perfil in c.Perfis.Values)
                        {
                            string v = string.Join(",", perfil.Ganho.Select(x => x.ToString("0.0", Inv)));
                            if (!v.Equals(p.GetValue(perfil.Guid) as string)) p.SetValue(perfil.Guid, v, RegistryValueKind.String);
                        }
                }
            }
            catch { }
        }

        // ------------------------------------------------ instalação (roda como administrador)
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, int acesso, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string sis, string nome, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool tudo, ref TOKEN_PRIV novo, int tam, IntPtr ant, IntPtr tamAnt);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool MoveFileEx(string de, string para, int flags);
        [StructLayout(LayoutKind.Sequential, Pack = 4)] struct TOKEN_PRIV { public int Count; public long Luid; public int Attr; }

        static void Privilegio(string nome)
        {
            IntPtr t;
            if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out t)) return;
            var tp = new TOKEN_PRIV { Count = 1, Attr = 2 };
            if (LookupPrivilegeValue(null, nome, out tp.Luid)) AdjustTokenPrivileges(t, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }

        static readonly System.Security.Principal.SecurityIdentifier Admins =
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);

        const System.Security.AccessControl.AccessControlSections Secoes =
            System.Security.AccessControl.AccessControlSections.Owner | System.Security.AccessControl.AccessControlSections.Group |
            System.Security.AccessControl.AccessControlSections.Access;

        // As chaves do aparelho pertencem ao sistema. Guarda a permissão original e dá controle aos administradores.
        static void Liberar(string caminho, RegistryKey copia, string nome)
        {
            using (var k = Hklm.OpenSubKey(caminho, RegistryKeyPermissionCheck.ReadSubTree, System.Security.AccessControl.RegistryRights.ReadPermissions))
            {
                if (k == null) return;
                if (copia.GetValue("sddl|" + nome) == null)
                    copia.SetValue("sddl|" + nome, k.GetAccessControl(Secoes).GetSecurityDescriptorSddlForm(Secoes));
            }
            using (var k = Hklm.OpenSubKey(caminho, RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.TakeOwnership))
            {
                var s = new System.Security.AccessControl.RegistrySecurity();
                s.SetOwner(Admins);
                k.SetAccessControl(s);
            }
            using (var k = Hklm.OpenSubKey(caminho, RegistryKeyPermissionCheck.ReadWriteSubTree,
                System.Security.AccessControl.RegistryRights.ChangePermissions | System.Security.AccessControl.RegistryRights.ReadPermissions))
            {
                var s = k.GetAccessControl(System.Security.AccessControl.AccessControlSections.Access);
                s.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(Admins, System.Security.AccessControl.RegistryRights.FullControl,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit, System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow));
                k.SetAccessControl(s);
            }
        }

        // Devolve as permissões guardadas. Primeiro a lista de acesso, depois o dono, cada um por si:
        // uma falha fica no log e não impede o resto da remoção.
        static void DevolverPermissao(string caminho, RegistryKey copia, string nome, StringBuilder log)
        {
            string sddl = copia.GetValue("sddl|" + nome) as string;
            if (sddl == null) return;
            // A regra que o instalador põe nunca é "original", e as chaves de áudio são do sistema.
            sddl = sddl.Replace("(A;CI;KA;;;BA)", "");
            if (sddl.StartsWith("O:BA")) sddl = "O:SY" + sddl.Substring(4);
            var s = new System.Security.AccessControl.RegistrySecurity();
            s.SetSecurityDescriptorSddlForm(sddl, Secoes);
            try
            {
                using (var k = Hklm.OpenSubKey(caminho, RegistryKeyPermissionCheck.ReadWriteSubTree,
                    System.Security.AccessControl.RegistryRights.ChangePermissions | System.Security.AccessControl.RegistryRights.ReadPermissions))
                {
                    if (k == null) return;
                    var acesso = new System.Security.AccessControl.RegistrySecurity();
                    acesso.SetSecurityDescriptorSddlForm(s.GetSecurityDescriptorSddlForm(System.Security.AccessControl.AccessControlSections.Access),
                        System.Security.AccessControl.AccessControlSections.Access);
                    k.SetAccessControl(acesso);
                }
            }
            catch (Exception e) { log.AppendLine("permissões de " + nome + ": " + e.Message); }
            try
            {
                using (var k = Hklm.OpenSubKey(caminho, RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.TakeOwnership))
                {
                    if (k == null) return;
                    var dono = new System.Security.AccessControl.RegistrySecurity();
                    dono.SetOwner(s.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)));
                    k.SetAccessControl(dono);
                }
            }
            catch (Exception e) { log.AppendLine("dono de " + nome + ": " + e.Message); }
        }

        static void GuardarValor(RegistryKey origem, string valor, RegistryKey copia, string prefixo)
        {
            string nome = prefixo + "|" + valor;
            if (copia.GetValue(nome) != null || copia.GetValue(nome + "|ausente") != null) return; // já guardado na primeira instalação
            object v = origem == null ? null : origem.GetValue(valor, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (v == null) copia.SetValue(nome + "|ausente", 1, RegistryValueKind.DWord);
            else copia.SetValue(nome, v, origem.GetValueKind(valor));
        }

        static void DevolverValor(RegistryKey destino, string valor, RegistryKey copia, string prefixo)
        {
            string nome = prefixo + "|" + valor;
            object v = copia.GetValue(nome, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (v != null) destino.SetValue(valor, v, copia.GetValueKind(nome));
            else if (copia.GetValue(nome + "|ausente") != null) destino.DeleteValue(valor, false);
        }

        static void Servicos(bool ligar, StringBuilder log)
        {
            foreach (var nome in ligar ? new[] { "AudioEndpointBuilder", "Audiosrv" } : new[] { "Audiosrv", "AudioEndpointBuilder" })
            {
                try
                {
                    using (var s = new System.ServiceProcess.ServiceController(nome))
                    {
                        if (ligar && s.Status != System.ServiceProcess.ServiceControllerStatus.Running) { s.Start(); s.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(20)); }
                        if (!ligar && s.Status != System.ServiceProcess.ServiceControllerStatus.Stopped) { s.Stop(); s.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); }
                    }
                    log.AppendLine((ligar ? "ligou " : "parou ") + nome);
                }
                catch (Exception e) { log.AppendLine("servico " + nome + ": " + e.Message); }
            }
        }

        public static void Instalar(string guid, StringBuilder log)
        {
            Privilegio("SeTakeOwnershipPrivilege");
            Privilegio("SeRestorePrivilege");
            if (!File.Exists(DllOrigem)) throw new Exception("Não achei o motor em " + DllOrigem);
            string dispositivo = Render + "{" + guid + "}";
            using (var d = Hklm.OpenSubKey(dispositivo))
                if (d == null) throw new Exception("Aparelho não encontrado no registro: " + guid);

            Servicos(false, log);
            try
            {
                // 1. arquivo do motor
                Directory.CreateDirectory(PastaPrograma);
                string dll = Path.Combine(PastaPrograma, "timbro_apo.dll");
                File.Copy(DllOrigem, dll, true);
                log.AppendLine("copiou " + dll);

                // 2. registro COM e do APO
                using (var k = Hklm.CreateSubKey(@"SOFTWARE\Classes\CLSID\" + Clsid))
                {
                    k.SetValue("", "Timbro");
                    using (var s = k.CreateSubKey("InprocServer32")) { s.SetValue("", dll); s.SetValue("ThreadingModel", "Both"); }
                }
                using (var k = Hklm.CreateSubKey(@"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\" + Clsid))
                {
                    k.SetValue("FriendlyName", "Timbro");
                    k.SetValue("Copyright", "Timbro");
                    k.SetValue("MajorVersion", 1, RegistryValueKind.DWord);
                    k.SetValue("MinorVersion", 0, RegistryValueKind.DWord);
                    k.SetValue("Flags", 0xF, RegistryValueKind.DWord);
                    k.SetValue("MinInputConnections", 1, RegistryValueKind.DWord);
                    k.SetValue("MaxInputConnections", 1, RegistryValueKind.DWord);
                    k.SetValue("MinOutputConnections", 1, RegistryValueKind.DWord);
                    k.SetValue("MaxOutputConnections", 1, RegistryValueKind.DWord);
                    k.SetValue("MaxInstances", unchecked((int)0xffffffff), RegistryValueKind.DWord);
                    k.SetValue("NumAPOInterfaces", 1, RegistryValueKind.DWord);
                    k.SetValue("APOInterface0", "{FD7F2B29-24D0-4b5c-B177-592C39F9CA10}");
                }
                log.AppendLine("registrou o APO");

                using (var copia = Hklm.CreateSubKey(Copia + "\\" + guid))
                using (var global = Hklm.CreateSubKey(Copia))
                {
                    // 3. o Windows só carrega APO sem assinatura com o audiodg sem proteção (o Equalizer APO faz o mesmo)
                    using (var a = Hklm.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio"))
                    {
                        GuardarValor(a, "DisableProtectedAudioDG", global, "audio");
                        a.SetValue("DisableProtectedAudioDG", 1, RegistryValueKind.DWord);
                    }

                    // 4. chave de ajustes: o Timbro (usuário) grava, o motor (serviço local) lê e anota o estado
                    using (var k = Hklm.CreateSubKey(Chave))
                    {
                        var s = k.GetAccessControl(System.Security.AccessControl.AccessControlSections.Access);
                        foreach (var sid in new[] { System.Security.Principal.WellKnownSidType.BuiltinUsersSid, System.Security.Principal.WellKnownSidType.LocalServiceSid })
                            s.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(new System.Security.Principal.SecurityIdentifier(sid, null),
                                System.Security.AccessControl.RegistryRights.FullControl, System.Security.AccessControl.InheritanceFlags.ContainerInherit,
                                System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
                        k.SetAccessControl(s);
                        k.CreateSubKey("Perfis").Close();
                        k.CreateSubKey("Estado").Close();
                    }

                    // 5. encaixa o motor no aparelho, guardando o que havia
                    Liberar(dispositivo, copia, "dispositivo");
                    bool tinhaFx;
                    using (var t = Hklm.OpenSubKey(dispositivo + "\\FxProperties")) tinhaFx = t != null;
                    if (!tinhaFx && copia.GetValue("fx|semchave") == null) copia.SetValue("fx|semchave", 1, RegistryValueKind.DWord);
                    if (tinhaFx) Liberar(dispositivo + "\\FxProperties", copia, "fx");
                    using (var fx = Hklm.CreateSubKey(dispositivo + "\\FxProperties"))
                    {
                        GuardarValor(fx, FxEfx, copia, "fx");
                        GuardarValor(fx, ModosEfx, copia, "fx");
                        fx.SetValue(FxEfx, Clsid, RegistryValueKind.String);
                        fx.SetValue(ModosEfx, new[] { ModoPadrao }, RegistryValueKind.MultiString);
                    }
                    log.AppendLine("encaixou no aparelho " + guid);

                    // 6. "Melhorias de áudio" precisam estar ligadas no aparelho
                    using (var p = Hklm.OpenSubKey(dispositivo + "\\Properties"))
                    {
                        object v = p == null ? null : p.GetValue(SysFx);
                        if (v is int && (int)v != 0)
                        {
                            Liberar(dispositivo + "\\Properties", copia, "props");
                            using (var pw = Hklm.OpenSubKey(dispositivo + "\\Properties", true))
                            {
                                GuardarValor(pw, SysFx, copia, "props");
                                pw.SetValue(SysFx, 0, RegistryValueKind.DWord);
                            }
                            log.AppendLine("ligou as melhorias de áudio do aparelho");
                        }
                    }
                }
            }
            finally { Servicos(true, log); }
        }

        static void RemoverDoAparelho(string guid, StringBuilder log)
        {
            string dispositivo = Render + "{" + guid + "}";
            using (var copia = Hklm.OpenSubKey(Copia + "\\" + guid, true))
            {
                using (var fx = Hklm.OpenSubKey(dispositivo + "\\FxProperties", true))
                {
                    if (fx != null)
                    {
                        if (copia != null) { DevolverValor(fx, FxEfx, copia, "fx"); DevolverValor(fx, ModosEfx, copia, "fx"); }
                        else if (Clsid.Equals(fx.GetValue(FxEfx) as string, StringComparison.OrdinalIgnoreCase)) { fx.DeleteValue(FxEfx, false); fx.DeleteValue(ModosEfx, false); }
                    }
                }
                if (copia == null) { log.AppendLine("sem cópia de " + guid + "; tirei só o motor"); return; }
                if (copia.GetValue("fx|semchave") != null)
                    using (var d = Hklm.OpenSubKey(dispositivo, true)) if (d != null) d.DeleteSubKeyTree("FxProperties", false);
                using (var p = Hklm.OpenSubKey(dispositivo + "\\Properties", true))
                    if (p != null && copia.GetValueNames().Any(n => n.StartsWith("props|"))) DevolverValor(p, SysFx, copia, "props");
                // De cima para baixo: as chaves de dentro herdam da de cima.
                DevolverPermissao(dispositivo, copia, "dispositivo", log);
                DevolverPermissao(dispositivo + "\\FxProperties", copia, "fx", log);
                DevolverPermissao(dispositivo + "\\Properties", copia, "props", log);
            }
            using (var c = Hklm.OpenSubKey(Copia, true)) if (c != null) c.DeleteSubKeyTree(guid, false);
            log.AppendLine("devolveu o aparelho " + guid + " como era");
        }

        public static void Remover(string guid, StringBuilder log)
        {
            Privilegio("SeTakeOwnershipPrivilege");
            Privilegio("SeRestorePrivilege");
            Servicos(false, log);
            try { RemoverDoAparelho(guid, log); }
            finally { Servicos(true, log); }
        }

        public static void RemoverTudo(StringBuilder log)
        {
            Privilegio("SeTakeOwnershipPrivilege");
            Privilegio("SeRestorePrivilege");
            Servicos(false, log);
            try
            {
                var guids = new List<string>();
                using (var c = Hklm.OpenSubKey(Copia)) if (c != null) guids.AddRange(c.GetSubKeyNames());
                foreach (var d in Audio.Saidas()) if (InstaladoNo(d.Guid) && !guids.Contains(d.Guid)) guids.Add(d.Guid);
                foreach (var g in guids) { try { RemoverDoAparelho(g, log); } catch (Exception e) { log.AppendLine(g + ": " + e.Message); } }

                using (var c = Hklm.OpenSubKey(Copia))
                using (var a = Hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Audio", true))
                    if (c != null && a != null) DevolverValor(a, "DisableProtectedAudioDG", c, "audio");
                Hklm.DeleteSubKeyTree(@"SOFTWARE\Classes\CLSID\" + Clsid, false);
                Hklm.DeleteSubKeyTree(@"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\" + Clsid, false);
                Hklm.DeleteSubKeyTree(Chave, false);
                Hklm.DeleteSubKeyTree(Copia, false);
                log.AppendLine("tirou o registro do motor");
            }
            finally { Servicos(true, log); }
            string dll = Path.Combine(PastaPrograma, "timbro_apo.dll");
            try { if (File.Exists(dll)) File.Delete(dll); Directory.Delete(PastaPrograma); }
            catch { MoveFileEx(dll, null, 4); log.AppendLine("o arquivo do motor sai no próximo reinício"); }
        }
    }

    // ---------------------------------------------------------------- visual
    static class Tema
    {
        public static readonly Color Fundo = Color.FromArgb(18, 19, 22);
        public static readonly Color Painel = Color.FromArgb(27, 29, 33);
        public static readonly Color Borda = Color.FromArgb(48, 51, 57);
        public static readonly Color Texto = Color.FromArgb(232, 234, 237);
        public static readonly Color Texto2 = Color.FromArgb(140, 146, 156);
        public static readonly Color Destaque = Color.FromArgb(46, 196, 182);
        public static readonly Color Apagado = Color.FromArgb(90, 95, 104);
        public static readonly Color Aviso = Color.FromArgb(245, 165, 36);

        public static Icon Icone(Color cor)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            using (var b = new SolidBrush(cor))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                int[] alt = { 12, 22, 16, 26, 10 };
                for (int i = 0; i < alt.Length; i++)
                {
                    using (var path = Arredondado(new RectangleF(2 + i * 6, 29 - alt[i], 4.5f, alt[i]), 2))
                        g.FillPath(b, path);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        public static GraphicsPath Arredondado(RectangleF r, float raio)
        {
            var p = new GraphicsPath();
            float d = raio * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    class EqPanel : Control
    {
        public static readonly int[] Freq = { 62, 116, 214, 397, 735, 1360, 2520, 4670, 8640, 16000 };
        public double[] Valores = new double[10];
        public bool Ativo = true;
        public event EventHandler Mudou;
        const double Max = 12;
        int arrastando = -1, sobre = -1;
        readonly Font fPeq = new Font("Segoe UI", 8f);
        readonly Font fVal = new Font("Segoe UI Semibold", 8.5f);

        public EqPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
        }

        RectangleF Area { get { return new RectangleF(46, 36, Width - 46 - 22, Height - 36 - 42); } }
        float X(int i) { var a = Area; return a.Left + a.Width * (i + 0.5f) / 10f; }
        float Y(double g) { var a = Area; return a.Top + (float)((Max - g) / (2 * Max)) * a.Height; }
        double G(float y)
        {
            var a = Area;
            double g = Max - (y - a.Top) / a.Height * 2 * Max;
            g = Math.Round(g * 2) / 2;
            return Math.Max(-Max, Math.Min(Max, g));
        }
        int Banda(float x)
        {
            var a = Area;
            int i = (int)Math.Floor((x - a.Left) / (a.Width / 10f));
            return Math.Max(0, Math.Min(9, i));
        }

        public static string TextoFreq(int f) { return f < 1000 ? f + " Hz" : (f / 1000.0).ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) + " kHz"; }
        static string TextoGanho(double g) { return g == 0 ? "0" : (g > 0 ? "+" : "") + g.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")); }

        void Definir(int i, double v)
        {
            if (Valores[i] == v) return;
            Valores[i] = v;
            Invalidate();
            if (Mudou != null) Mudou(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left) return;
            arrastando = Banda(e.X);
            Definir(arrastando, G(e.Y));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (arrastando >= 0) { Definir(arrastando, G(e.Y)); return; }
            int b = Banda(e.X);
            if (b != sobre) { sobre = b; Invalidate(); }
        }
        protected override void OnMouseUp(MouseEventArgs e) { arrastando = -1; }
        protected override void OnMouseLeave(EventArgs e) { sobre = -1; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { Focus(); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { Definir(Banda(e.X), 0); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int i = Banda(e.X);
            Definir(i, Math.Max(-Max, Math.Min(Max, Valores[i] + Math.Sign(e.Delta) * 0.5)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Tema.Fundo);
            using (var fundo = Tema.Arredondado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 14))
            using (var bf = new SolidBrush(Tema.Painel)) g.FillPath(bf, fundo);
            var a = Area;
            Color cor = Ativo ? Tema.Destaque : Tema.Apagado;

            var direita = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            var centro = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var grade = new Pen(Color.FromArgb(40, 43, 49)))
            using (var zero = new Pen(Color.FromArgb(70, 74, 82)))
            using (var b2 = new SolidBrush(Tema.Texto2))
            {
                foreach (int db in new[] { 12, 6, 0, -6, -12 })
                {
                    float y = Y(db);
                    g.DrawLine(db == 0 ? zero : grade, a.Left, y, a.Right, y);
                    g.DrawString((db > 0 ? "+" : "") + db + " dB", fPeq, b2, a.Left - 6, y, direita);
                }
                grade.DashStyle = DashStyle.Dot;
                for (int i = 0; i < 10; i++)
                {
                    float x = X(i);
                    if (i == sobre || i == arrastando)
                        using (var fundo = new SolidBrush(Color.FromArgb(14, 255, 255, 255)))
                            g.FillRectangle(fundo, x - a.Width / 20f, a.Top, a.Width / 10f, a.Height);
                    g.DrawLine(grade, x, a.Top, x, a.Bottom);
                    g.DrawString(TextoFreq(Freq[i]), fPeq, b2, x, a.Bottom + 20, centro);
                }
            }

            var pts = new List<PointF>();
            pts.Add(new PointF(a.Left, Y(Valores[0])));
            for (int i = 0; i < 10; i++) pts.Add(new PointF(X(i), Y(Valores[i])));
            pts.Add(new PointF(a.Right, Y(Valores[9])));

            using (var preench = new GraphicsPath())
            {
                preench.AddCurve(pts.ToArray(), 0.4f);
                preench.AddLine(a.Right, Y(Valores[9]), a.Right, Y(0));
                preench.AddLine(a.Right, Y(0), a.Left, Y(0));
                preench.CloseFigure();
                using (var b = new SolidBrush(Color.FromArgb(38, cor))) g.FillPath(b, preench);
            }
            using (var p = new Pen(cor, 2.5f)) g.DrawCurve(p, pts.ToArray(), 0.4f);

            using (var b = new SolidBrush(cor))
            using (var meio = new SolidBrush(Tema.Painel))
            using (var bt = new SolidBrush(Tema.Texto))
            {
                for (int i = 0; i < 10; i++)
                {
                    float x = X(i), y = Y(Valores[i]);
                    float r = (i == sobre || i == arrastando) ? 10 : 8;
                    g.FillEllipse(b, x - r, y - r, 2 * r, 2 * r);
                    g.FillEllipse(meio, x - 3, y - 3, 6, 6);
                    g.DrawString(TextoGanho(Valores[i]), fVal, bt, x, a.Top - 18, centro);
                }
            }
        }
    }

    // Base dos controles arredondados: desenho próprio, fundo do pai nos cantos, estado de mouse.
    abstract class Suave : Control
    {
        protected bool sobre, apertado;
        protected static readonly Color CorSobre = Color.FromArgb(38, 41, 47);
        protected static readonly Color CorApertado = Color.FromArgb(48, 52, 59);

        protected Suave()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            TabStop = false;
            ForeColor = Tema.Texto;
        }

        protected override void OnMouseEnter(EventArgs e) { sobre = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { sobre = false; apertado = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { apertado = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { apertado = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected Graphics Preparar(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Tema.Fundo);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            return g;
        }

        protected RectangleF Caixa { get { return new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f); } }
    }

    class Botao : Suave
    {
        bool destaque;
        public bool Destaque { get { return destaque; } set { destaque = value; Invalidate(); } }

        public Botao(string texto, int x, int y, int w, int h)
        {
            Text = texto;
            SetBounds(x, y, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Preparar(e);
            Color fundo = destaque ? (apertado ? Color.FromArgb(36, 170, 158) : sobre ? Color.FromArgb(70, 214, 200) : Tema.Destaque)
                                   : (apertado ? CorApertado : sobre ? CorSobre : Tema.Painel);
            using (var p = Tema.Arredondado(Caixa, Math.Min(10, Height / 2f)))
            {
                using (var b = new SolidBrush(fundo)) g.FillPath(b, p);
                if (!destaque) using (var pen = new Pen(sobre ? Color.FromArgb(70, 74, 82) : Tema.Borda)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, destaque ? Tema.Fundo : ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Caixa de escolha arredondada, com a lista aberta num menu escuro.
    class Seletor : Suave
    {
        public List<Dispositivo> Items = new List<Dispositivo>();
        Dispositivo sel;
        public event EventHandler SelectedIndexChanged;
        readonly ContextMenuStrip menu = new ContextMenuStrip { Renderer = new MenuEscuro(), ShowImageMargin = false, ShowCheckMargin = true };

        public Dispositivo SelectedItem
        {
            get { return sel; }
            set
            {
                if (sel == value) return;
                sel = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public Seletor() { menu.Closed += delegate { Invalidate(); }; }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            menu.Items.Clear();
            foreach (var d in Items)
            {
                var dd = d;
                var item = new ToolStripMenuItem(d.Nome, null, delegate { SelectedItem = dd; }) { Checked = d == sel, Padding = new Padding(0, 4, 0, 4) };
                menu.Items.Add(item);
            }
            menu.MinimumSize = new Size(Width, 0);
            menu.Show(this, new Point(0, Height + 4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Preparar(e);
            bool aberto = menu.Visible;
            using (var p = Tema.Arredondado(Caixa, 10))
            {
                using (var b = new SolidBrush(sobre || aberto ? CorSobre : Tema.Painel)) g.FillPath(b, p);
                using (var pen = new Pen(aberto ? Tema.Destaque : sobre ? Color.FromArgb(70, 74, 82) : Tema.Borda)) g.DrawPath(pen, p);
            }
            var r = new Rectangle(14, 0, Width - 48, Height);
            TextRenderer.DrawText(g, sel == null ? "Nenhuma saída" : sel.Nome, Font, r, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            float cx = Width - 22, cy = Height / 2f;
            using (var pen = new Pen(Tema.Texto2, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (aberto) g.DrawLines(pen, new[] { new PointF(cx - 5, cy + 2.5f), new PointF(cx, cy - 2.5f), new PointF(cx + 5, cy + 2.5f) });
                else g.DrawLines(pen, new[] { new PointF(cx - 5, cy - 2.5f), new PointF(cx, cy + 2.5f), new PointF(cx + 5, cy - 2.5f) });
            }
        }
    }

    // Chave deslizante no lugar da caixinha de marcar.
    class Interruptor : Suave
    {
        bool marcado;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return marcado; }
            set
            {
                if (marcado == value) return;
                marcado = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Preparar(e);
            var pista = new RectangleF(1, (Height - 20) / 2f, 36, 20);
            using (var p = Tema.Arredondado(pista, 10))
            using (var b = new SolidBrush(marcado ? Tema.Destaque : (sobre ? Color.FromArgb(72, 77, 86) : Color.FromArgb(58, 62, 70))))
                g.FillPath(b, p);
            float kx = marcado ? pista.Right - 18 : pista.Left + 2;
            using (var b = new SolidBrush(marcado ? Color.White : Color.FromArgb(200, 204, 210)))
                g.FillEllipse(b, kx, pista.Top + 2, 16, 16);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(46, 0, Width - 46, Height), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    // Menus escuros (lista de saídas e ícone da bandeja).
    class CoresMenu : ProfessionalColorTable
    {
        static readonly Color Sel = Color.FromArgb(40, 43, 49);
        public override Color MenuItemSelected { get { return Sel; } }
        public override Color MenuItemSelectedGradientBegin { get { return Sel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Sel; } }
        public override Color MenuItemBorder { get { return Sel; } }
        public override Color MenuBorder { get { return Tema.Borda; } }
        public override Color ToolStripDropDownBackground { get { return Tema.Painel; } }
        public override Color ImageMarginGradientBegin { get { return Tema.Painel; } }
        public override Color ImageMarginGradientMiddle { get { return Tema.Painel; } }
        public override Color ImageMarginGradientEnd { get { return Tema.Painel; } }
        public override Color SeparatorDark { get { return Tema.Borda; } }
        public override Color SeparatorLight { get { return Tema.Painel; } }
        public override Color CheckBackground { get { return Tema.Painel; } }
        public override Color CheckSelectedBackground { get { return Sel; } }
        public override Color CheckPressedBackground { get { return Sel; } }
        public override Color ButtonSelectedBorder { get { return Sel; } }
    }

    class MenuEscuro : ToolStripProfessionalRenderer
    {
        public MenuEscuro() : base(new CoresMenu()) { RoundedEdges = true; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Tema.Texto : Tema.Apagado;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Tema.Texto2;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            using (var b = new SolidBrush(Tema.Destaque)) g.FillEllipse(b, cx - 4, cy - 4, 8, 8);
        }

        protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Tema.Arredondado(new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2), 6))
            using (var b = new SolidBrush(Color.FromArgb(40, 43, 49)))
                g.FillPath(b, p);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { OnRenderItemBackground(e); }
    }

    // ---------------------------------------------------------------- janela
    class Janela : Form
    {
        static readonly string[] NomesPredef = { "Plano", "Mais grave", "Voz clara", "Música", "Filme e jogo" };
        static readonly double[][] Predef =
        {
            new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            new double[] { 6, 5, 3, 1, 0, 0, 0, 0, 0, 0 },
            new double[] { -3, -2, -1, 0, 1, 2.5, 3, 2, 1, 0 },
            new double[] { 4, 3, 1, 0, -1, 0, 1, 2, 3, 3 },
            new double[] { 5, 4, 2, 0, 0, 1, 2, 2.5, 2, 1 },
        };

        readonly Config cfg;
        readonly EqPanel eq;
        readonly Seletor cbSaida;
        readonly Interruptor ckFixa, ckEstereo;
        readonly Botao btLigado;
        readonly Label lbStatus, lbRodape;
        readonly LinkLabel lkAcao;
        readonly NotifyIcon tray;
        readonly System.Windows.Forms.Timer tSalvar, tVerificar;
        readonly Icon icoLigado, icoDesligado;
        readonly List<DateTime> forcadas = new List<DateTime>();
        Notificador notif;
        Perfil perfil;
        Dispositivo atual;
        bool permitirMostrar, saindo, preenchendo;
        Action acaoDoLink;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, int mods, int vk);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int sim = 1;
            DwmSetWindowAttribute(Handle, 20, ref sim, 4); // barra de título escura
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); ActiveControl = eq; }

        public Janela(Config c, bool oculto)
        {
            cfg = c;
            permitirMostrar = !oculto;
            icoLigado = Tema.Icone(Tema.Destaque);
            icoDesligado = Tema.Icone(Tema.Apagado);

            Text = "Timbro";
            Icon = icoLigado;
            BackColor = Tema.Fundo;
            ForeColor = Tema.Texto;
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(780, 524);

            var titulo = new Label { Text = "Timbro", Font = new Font("Segoe UI Semibold", 15f), AutoSize = true, Location = new Point(20, 14), ForeColor = Tema.Texto };
            Controls.Add(titulo);
            btLigado = new Botao("Ligado", 650, 16, 110, 34);
            btLigado.Font = new Font("Segoe UI Semibold", 9.5f);
            btLigado.Click += delegate { AlternarLigado(); };
            Controls.Add(btLigado);

            Controls.Add(new Label { Text = "Saída de áudio", AutoSize = true, Location = new Point(20, 66), ForeColor = Tema.Texto2 });
            cbSaida = new Seletor { Location = new Point(20, 88), Size = new Size(470, 36) };
            cbSaida.SelectedIndexChanged += delegate { SaidaEscolhida(); };
            
            Controls.Add(cbSaida);
            ckFixa = new Interruptor { Text = "Manter sempre nesta saída", Location = new Point(508, 88), Size = new Size(252, 36) };
            ckFixa.CheckedChanged += delegate { FixaMudou(); };
            Controls.Add(ckFixa);
            ckEstereo = new Interruptor { Text = "Manter o fone em estéreo (o microfone dele fica desligado)", Location = new Point(20, 130), Size = new Size(470, 28), Visible = false };
            ckEstereo.CheckedChanged += delegate { EstereoMudou(); };
            Controls.Add(ckEstereo);

            eq = new EqPanel { Location = new Point(20, 166), Size = new Size(740, 250) };
            eq.Mudou += delegate { AoMudarEq(); };
            Controls.Add(eq);

            int x = 20;
            for (int i = 0; i < NomesPredef.Length; i++)
            {
                int k = i;
                var b = new Botao(NomesPredef[i], x, 430, 106, 36);
                b.Click += delegate { AplicarPredef(k); };
                Controls.Add(b);
                x += 114;
            }

            lbRodape = new Label { AutoSize = false, Location = new Point(600, 430), Size = new Size(160, 36), ForeColor = Tema.Texto2, TextAlign = ContentAlignment.MiddleRight, Font = new Font("Segoe UI", 8.5f) };
            Controls.Add(lbRodape);

            lbStatus = new Label { AutoSize = false, Location = new Point(20, 480), Size = new Size(560, 34), ForeColor = Tema.Texto2, Font = new Font("Segoe UI", 8.5f) };
            Controls.Add(lbStatus);
            lkAcao = new LinkLabel { AutoSize = false, Location = new Point(580, 480), Size = new Size(180, 34), TextAlign = ContentAlignment.TopRight, LinkColor = Tema.Destaque, ActiveLinkColor = Tema.Texto, Font = new Font("Segoe UI", 8.5f) };
            lkAcao.LinkClicked += delegate { if (acaoDoLink != null) acaoDoLink(); };
            Controls.Add(lkAcao);

            tSalvar = new System.Windows.Forms.Timer { Interval = 180 };
            tSalvar.Tick += delegate { tSalvar.Stop(); Aplicar(); };
            tVerificar = new System.Windows.Forms.Timer { Interval = 500 };
            tVerificar.Tick += delegate { tVerificar.Stop(); Verificar(); };
            var tStatus = new System.Windows.Forms.Timer { Interval = 2000 };
            tStatus.Tick += delegate { if (Visible) AtualizarStatus(null); };
            tStatus.Start();

            tray = new NotifyIcon { Icon = icoLigado, Text = "Timbro", Visible = true, ContextMenuStrip = new ContextMenuStrip { Renderer = new MenuEscuro() } };
            tray.ContextMenuStrip.Opening += delegate { MontarMenu(); };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Mostrar(); };

            if (!IsHandleCreated) CreateHandle();
            RegisterHotKey(Handle, 1, 0x1 | 0x2 | 0x4000, (int)Keys.E); // Ctrl+Alt+E

            notif = new Notificador(() => { try { BeginInvoke((MethodInvoker)delegate { tVerificar.Stop(); tVerificar.Start(); }); } catch { } });
            Audio.Avisar(notif);

            AtualizarLigado();
            Verificar();
            Aplicar();
        }

        // ------------------------------------------------ mostrar e esconder
        protected override void SetVisibleCore(bool value)
        {
            if (!permitirMostrar) { value = false; if (!IsHandleCreated) CreateHandle(); }
            base.SetVisibleCore(value);
        }

        public void Mostrar()
        {
            permitirMostrar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!saindo && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) { AlternarLigado(); tray.ShowBalloonTip(1500, "Timbro", cfg.Ligado ? "Equalizador ligado" : "Equalizador desligado", ToolTipIcon.None); }
            base.WndProc(ref m);
        }

        void Sair() { saindo = true; tray.Visible = false; Application.Exit(); }

        // ------------------------------------------------ saídas
        void Verificar()
        {
            if (cfg.SaidaFixa != "" && Audio.Ativo(cfg.SaidaFixa) && Audio.Padrao() != cfg.SaidaFixa)
            {
                forcadas.RemoveAll(t => (DateTime.Now - t).TotalSeconds > 20);
                if (forcadas.Count < 4) { forcadas.Add(DateTime.Now); Audio.TornarPadrao(cfg.SaidaFixa); }
            }
            RecarregarSaidas();
        }

        void RecarregarSaidas()
        {
            preenchendo = true;
            var lista = Audio.Saidas();
            string padrao = Audio.Padrao();
            cbSaida.Items.Clear();
            foreach (var d in lista) cbSaida.Items.Add(d);
            var sel = lista.FirstOrDefault(d => d.Id == padrao) ?? lista.FirstOrDefault();
            if (sel != null) cbSaida.SelectedItem = sel;
            preenchendo = false;
            CarregarPerfil(sel);
        }

        void CarregarPerfil(Dispositivo d)
        {
            atual = d;
            preenchendo = true;
            ckFixa.Checked = d != null && d.Id == cfg.SaidaFixa;
            preenchendo = false;
            if (d == null) { perfil = null; eq.Enabled = false; AtualizarStatus(null); return; }
            eq.Enabled = true;
            AtualizarEstereo();
            perfil = cfg.PerfilDe(d);
            eq.Valores = perfil.Ganho;
            eq.Invalidate();
            AtualizarRodape();
            AtualizarStatus(null);
        }

        void SaidaEscolhida()
        {
            if (preenchendo) return;
            var d = cbSaida.SelectedItem as Dispositivo;
            if (d == null) return;
            if (d.Id != Audio.Padrao()) Audio.TornarPadrao(d.Id);
            if (cfg.SaidaFixa != "" && cfg.SaidaFixa != d.Id) cfg.SaidaFixa = d.Id; // trocar à mão muda a saída fixa junto
            CarregarPerfil(d);
            cfg.Salvar();
        }

        void AtualizarEstereo()
        {
            var vv = Audio.VivaVoz(atual);
            preenchendo = true;
            ckEstereo.Visible = vv.Count > 0;
            ckEstereo.Checked = vv.Count > 0 && vv.All(p => p.Value != 1);
            preenchendo = false;
        }

        // Desliga (ou religa) o "mãos-livres" do fone: sem ele o Windows não troca o fone para o modo viva-voz.
        void EstereoMudou()
        {
            if (preenchendo || atual == null) return;
            var inst = Audio.VivaVoz(atual).Select(p => Audio.Instancia(p.Key)).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            if (inst.Count == 0) return;
            bool travar = ckEstereo.Checked;
            if (ComoAdministrador("--viva-voz " + (travar ? "desligar" : "ligar") + " " + atual.Guid))
            {
                if (travar) cfg.Estereo.Add(atual.Guid); else cfg.Estereo.Remove(atual.Guid);
                cfg.Salvar();
            }
            AtualizarEstereo();
        }
        void FixaMudou()
        {
            if (preenchendo || atual == null) return;
            cfg.SaidaFixa = ckFixa.Checked ? atual.Id : "";
            cfg.Salvar();
            if (ckFixa.Checked && Audio.Padrao() != atual.Id) Audio.TornarPadrao(atual.Id);
        }

        // ------------------------------------------------ equalizador
        void AoMudarEq()
        {
            AtualizarRodape();
            tSalvar.Stop();
            tSalvar.Start();
        }

        void AplicarPredef(int k)
        {
            if (perfil == null) return;
            Array.Copy(Predef[k], perfil.Ganho, 10);
            eq.Invalidate();
            AoMudarEq();
        }

        void AlternarLigado()
        {
            cfg.Ligado = !cfg.Ligado;
            AtualizarLigado();
            Aplicar();
        }

        void AtualizarLigado()
        {
            btLigado.Text = cfg.Ligado ? "Ligado" : "Desligado";
            btLigado.Destaque = cfg.Ligado;
            btLigado.ForeColor = cfg.Ligado ? Tema.Fundo : Tema.Texto2;
            eq.Ativo = cfg.Ligado;
            eq.Invalidate();
            tray.Icon = cfg.Ligado ? icoLigado : icoDesligado;
            tray.Text = cfg.Ligado ? "Timbro: ligado" : "Timbro: desligado";
        }

        void AtualizarRodape()
        {
            if (perfil == null) { lbRodape.Text = ""; return; }
            double max = perfil.Ganho.Max();
            lbRodape.Text = max > 0 ? "Volume -" + max.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR")) + " dB\npara não distorcer" : "";
        }

        void Aplicar()
        {
            cfg.Salvar();
            Motor.EscreverParametros(cfg);
            AtualizarStatus(null);
        }

        // Roda timbro.exe como administrador para mexer no motor. Devolve false se cancelou ou falhou.
        bool ComoAdministrador(string argumentos)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, argumentos) { Verb = "runas", UseShellExecute = true });
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch { return false; }
            finally { Cursor = Cursors.Default; Aplicar(); }
        }

        void AtualizarStatus(string erro)
        {
            Action acao = null;
            string link = "", texto;
            Color cor = Tema.Texto2;
            if (atual == null)
            {
                texto = "Nenhuma saída de áudio ativa.";
            }
            else if (!Motor.InstaladoNo(atual.Guid))
            {
                texto = "O motor do Timbro ainda não está neste aparelho. Sem ele o som não muda.";
                link = "Instalar neste aparelho";
                var guid = atual.Guid;
                acao = () => ComoAdministrador("--instalar-motor " + guid);
                cor = Tema.Aviso;
            }
            else
            {
                string est = Motor.Estado(atual.Guid);
                if (est != null && est.StartsWith("rodando"))
                {
                    var partes = est.Split(' ');
                    texto = "Motor rodando (" + partes[1] + " Hz, " + partes[3] + " canais). Duplo clique zera a faixa, roda do mouse ajusta fino, Ctrl+Alt+E liga e desliga.";
                }
                else texto = "Motor instalado neste aparelho. Toque algum som para ele começar.";
                link = "Remover deste aparelho";
                var guid = atual.Guid;
                acao = () => ComoAdministrador("--remover-motor " + guid);
            }
            if (lbStatus.Text != texto) lbStatus.Text = texto;
            lbStatus.ForeColor = cor;
            if (lkAcao.Text != link) lkAcao.Text = link;
            acaoDoLink = acao;
        }

        // ------------------------------------------------ bandeja
        static string ChaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool IniciaComWindows()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(ChaveRun)) return k != null && k.GetValue("Timbro") != null;
        }

        static void IniciarComWindows(bool sim)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(ChaveRun, true))
            {
                if (sim) k.SetValue("Timbro", "\"" + Application.ExecutablePath + "\" --oculto");
                else k.DeleteValue("Timbro", false);
            }
        }

        void MontarMenu()
        {
            var m = tray.ContextMenuStrip;
            m.Items.Clear();
            var abrir = new ToolStripMenuItem("Abrir o Timbro", null, delegate { Mostrar(); });
            abrir.Font = new Font(abrir.Font, FontStyle.Bold);
            m.Items.Add(abrir);
            m.Items.Add(new ToolStripMenuItem("Equalizador ligado", null, delegate { AlternarLigado(); }) { Checked = cfg.Ligado });

            var pre = new ToolStripMenuItem("Predefinição");
            for (int i = 0; i < NomesPredef.Length; i++) { int k = i; pre.DropDownItems.Add(NomesPredef[i], null, delegate { AplicarPredef(k); }); }
            pre.Enabled = perfil != null;
            m.Items.Add(pre);

            var saida = new ToolStripMenuItem("Saída de áudio");
            string padrao = Audio.Padrao();
            foreach (var d in Audio.Saidas())
            {
                var dd = d;
                saida.DropDownItems.Add(new ToolStripMenuItem(d.Nome + (d.Id == cfg.SaidaFixa ? "  (fixa)" : ""), null, delegate { Audio.TornarPadrao(dd.Id); if (cfg.SaidaFixa != "") { cfg.SaidaFixa = dd.Id; cfg.Salvar(); } }) { Checked = d.Id == padrao });
            }
            m.Items.Add(saida);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Iniciar com o Windows", null, delegate { IniciarComWindows(!IniciaComWindows()); }) { Checked = IniciaComWindows() });
            m.Items.Add(new ToolStripMenuItem("Remover o motor de todos os aparelhos", null, delegate
            {
                if (MessageBox.Show("Tirar o motor do Timbro de todos os aparelhos e devolver o som como era?", "Timbro", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    ComoAdministrador("--remover-tudo");
            }));
            m.Items.Add(new ToolStripMenuItem("Sair", null, delegate { Sair(); }));
        }
    }

    // ---------------------------------------------------------------- início
    static class Program
    {
        static string Arg(string[] a, string nome)
        {
            int i = Array.IndexOf(a, nome);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        [STAThread]
        static void Main(string[] args)
        {
            // Tarefas de administrador (o próprio Timbro se chama com "runas").
            string g;
            if ((g = Arg(args, "--instalar-motor")) != null || (g = Arg(args, "--remover-motor")) != null || args.Contains("--remover-tudo"))
            {
                var log = new StringBuilder();
                int codigo = 0;
                try
                {
                    if (args.Contains("--instalar-motor")) Motor.Instalar(g, log);
                    else if (args.Contains("--remover-motor")) Motor.Remover(g, log);
                    else Motor.RemoverTudo(log);
                }
                catch (Exception e) { log.AppendLine("ERRO: " + e); codigo = 1; }
                try { Directory.CreateDirectory(Config.Pasta); File.AppendAllText(Path.Combine(Config.Pasta, "motor.log"), DateTime.Now + " " + string.Join(" ", args) + "\r\n" + log + "\r\n"); } catch { }
                if (codigo != 0) MessageBox.Show(log.ToString(), "Timbro: não deu certo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Environment.Exit(codigo);
            }

            int iv = Array.IndexOf(args, "--viva-voz");
            if (iv >= 0)
            {
                if (iv + 2 >= args.Length)
                {
                    try { File.AppendAllText(Path.Combine(Config.Pasta, "motor.log"), DateTime.Now + " --viva-voz com argumentos faltando: [" + string.Join("] [", args) + "]\r\n"); } catch { }
                    Environment.Exit(2);
                }
                bool desligar = args[iv + 1] == "desligar";
                var log = new StringBuilder();
                int codigo = 0;
                bool admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                log.AppendLine("administrador=" + admin + " usuario=" + Environment.UserName);
                try
                {
                    var fone = Audio.Saidas().FirstOrDefault(x => x.Guid.Equals(args[iv + 2], StringComparison.OrdinalIgnoreCase));
                    var lista = Audio.VivaVoz(fone).Select(x => Audio.Instancia(x.Key)).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
                    log.AppendLine("fone=" + (fone == null ? "(não achei)" : fone.Nome) + " viva-voz=" + string.Join(", ", lista));
                    if (lista.Count == 0) codigo = 1;
                    foreach (var inst in lista)
                    {
                        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), (desligar ? "/disable-device" : "/enable-device") + " \"" + inst + "\"")
                        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                        var p = Process.Start(psi);
                        log.AppendLine(p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd());
                        p.WaitForExit();
                        log.AppendLine("pnputil saiu com " + p.ExitCode);
                        if (p.ExitCode != 0 && p.ExitCode != 3010) codigo = 1;
                    }
                }
                catch (Exception e) { log.AppendLine("ERRO: " + e); codigo = 1; }
                try { Directory.CreateDirectory(Config.Pasta); File.AppendAllText(Path.Combine(Config.Pasta, "motor.log"), DateTime.Now + " " + string.Join(" ", args) + "\r\n" + log + "\r\n"); } catch { }
                if (codigo != 0) MessageBox.Show(log.ToString(), "Timbro: não deu certo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Environment.Exit(codigo);
            }
            // --listar <arquivo>: escreve as saídas e onde o motor está (diagnóstico) e sai.
            string listar = Arg(args, "--listar");
            if (listar != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine("padrao=" + Audio.Padrao());
                foreach (var d in Audio.Saidas()) sb.AppendLine(d.Id + " | " + d.Nome + " | " + d.Guid + " | motor=" + Motor.InstaladoNo(d.Guid) + " | estado=" + Motor.Estado(d.Guid));
                sb.AppendLine("estados=" + Motor.EstadosTodos());
                File.WriteAllText(listar, sb.ToString(), new UTF8Encoding(false));
                return;
            }

            // --predef "<nome>": aplica a predefinição na saída atual e sai.
            string predef = Arg(args, "--predef");
            if (predef != null)
            {
                var c = Config.Carregar();
                string padrao = Audio.Padrao();
                var d = Audio.Saidas().FirstOrDefault(x => x.Id == padrao);
                var campo = typeof(Janela).GetField("Predef", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var nomes = (string[])typeof(Janela).GetField("NomesPredef", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                int k = Array.FindIndex(nomes, n => n.Equals(predef, StringComparison.OrdinalIgnoreCase));
                if (d == null || k < 0) Environment.Exit(2);
                Array.Copy(((double[][])campo.GetValue(null))[k], c.PerfilDe(d).Ganho, 10);
                c.Salvar();
                Motor.EscreverParametros(c);
                return;
            }

            bool novo;
            var mutex = new Mutex(true, "Timbro-instancia-unica", out novo);
            var evento = new EventWaitHandle(false, EventResetMode.AutoReset, "Timbro-mostrar");
            if (!novo) { evento.Set(); return; }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var janela = new Janela(Config.Carregar(), args.Contains("--oculto"));
            ThreadPool.RegisterWaitForSingleObject(evento, (s, t) => { try { janela.BeginInvoke((MethodInvoker)janela.Mostrar); } catch { } }, null, -1, false);
            Application.Run(janela);
            GC.KeepAlive(mutex);
        }
    }
}
