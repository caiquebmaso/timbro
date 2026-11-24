// Timbro: motor de som (APO de efeito de ponta, EFX) carregado pelo audiodg.exe.
// Lê os ajustes de HKLM\SOFTWARE\Timbro, que o Timbro grava, e equaliza o aparelho onde foi instalado.
#include <windows.h>
#include <initguid.h>
#include <mmdeviceapi.h>
#include <audioenginebaseapo.h>
#include <audiomediatype.h>
#include <ks.h>
#include <ksmedia.h>
#include <xmmintrin.h>
#include <atomic>
#include <new>
#include <cwchar>
#include <cstdio>
#include <locale.h>
#include <stdlib.h>
#include "dsp.h"

// {162BAFBE-06D6-43A3-BE19-8272BD6BCF80}
DEFINE_GUID(CLSID_TimbroApo, 0x162bafbe, 0x06d6, 0x43a3, 0xbe, 0x19, 0x82, 0x72, 0xbd, 0x6b, 0xcf, 0x80);

static HMODULE g_modulo = NULL;
static std::atomic<long> g_objetos(0), g_travas(0);

static const wchar_t* CHAVE = L"SOFTWARE\\Timbro";

// Troca de parâmetros sem trava entre a thread que lê o registro e a thread de áudio (buffer triplo).
class Caixa
{
    timbro::Parametros slots[3];
    std::atomic<int> meio;   // índice do slot do meio; bit 4 = novo
    int escrita, leitura;
public:
    Caixa() : meio(1), escrita(0), leitura(2) { std::memset(slots, 0, sizeof(slots)); }
    void Publicar(const timbro::Parametros& p)
    {
        slots[escrita] = p;
        escrita = meio.exchange(escrita | 4) & 3;
    }
    // Só a thread de áudio chama. Devolve true se chegou parâmetro novo.
    bool Pegar(timbro::Parametros& p)
    {
        if (!(meio.load(std::memory_order_acquire) & 4)) return false;
        leitura = meio.exchange(leitura) & 3;
        p = slots[leitura];
        return true;
    }
};

class TimbroApo : public IAudioProcessingObject, public IAudioProcessingObjectRT,
                 public IAudioProcessingObjectConfiguration, public IAudioSystemEffects
{
    // IUnknown "de dentro", para o motor de áudio poder agregar o objeto.
    struct Interno : public IUnknown
    {
        TimbroApo* dono;
        STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
        {
            if (!ppv) return E_POINTER;
            *ppv = NULL;
            if (riid == IID_IUnknown) *ppv = static_cast<IUnknown*>(this);
            else if (riid == __uuidof(IAudioProcessingObject)) *ppv = static_cast<IAudioProcessingObject*>(dono);
            else if (riid == __uuidof(IAudioProcessingObjectRT)) *ppv = static_cast<IAudioProcessingObjectRT*>(dono);
            else if (riid == __uuidof(IAudioProcessingObjectConfiguration)) *ppv = static_cast<IAudioProcessingObjectConfiguration*>(dono);
            else if (riid == __uuidof(IAudioSystemEffects)) *ppv = static_cast<IAudioSystemEffects*>(dono);
            else return E_NOINTERFACE;
            reinterpret_cast<IUnknown*>(*ppv)->AddRef();
            return S_OK;
        }
        STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&dono->refs); }
        STDMETHODIMP_(ULONG) Release()
        {
            long r = InterlockedDecrement(&dono->refs);
            if (r == 0) delete dono;
            return r;
        }
    } interno;

    long refs;
    IUnknown* externo;

    bool iniciado, travado;
    wchar_t guid[64];
    bool teste;
    UINT32 canais;
    double taxa;
    timbro::Estado* estados;
    timbro::Projeto projeto;
    timbro::Parametros atuais;
    Caixa caixa;
    HANDLE parar, vigia;

public:
    explicit TimbroApo(IUnknown* fora)
        : refs(1), iniciado(false), travado(false), teste(false), canais(0), taxa(0), estados(NULL), parar(NULL), vigia(NULL)
    {
        interno.dono = this;
        externo = fora ? fora : static_cast<IUnknown*>(&interno);
        guid[0] = 0;
        std::memset(&atuais, 0, sizeof(atuais));
        std::memset(&projeto, 0, sizeof(projeto));
        g_objetos++;
    }

    ~TimbroApo()
    {
        if (vigia)
        {
            SetEvent(parar);
            WaitForSingleObject(vigia, 5000);
            CloseHandle(vigia);
        }
        if (parar) CloseHandle(parar);
        delete[] estados;
        g_objetos--;
    }

    IUnknown* Interior() { return &interno; }

    // IUnknown (delegando)
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) { return externo->QueryInterface(riid, ppv); }
    STDMETHODIMP_(ULONG) AddRef() { return externo->AddRef(); }
    STDMETHODIMP_(ULONG) Release() { return externo->Release(); }

    // ------------------------------------------------ IAudioProcessingObject
    STDMETHODIMP Reset()
    {
        if (estados) std::memset(estados, 0, sizeof(timbro::Estado) * canais);
        return S_OK;
    }

    STDMETHODIMP GetLatency(HNSTIME* t) { if (!t) return E_POINTER; *t = 0; return S_OK; }

    STDMETHODIMP GetRegistrationProperties(APO_REG_PROPERTIES** pp)
    {
        if (!pp) return E_POINTER;
        APO_REG_PROPERTIES* r = (APO_REG_PROPERTIES*)CoTaskMemAlloc(sizeof(APO_REG_PROPERTIES));
        if (!r) return E_OUTOFMEMORY;
        std::memset(r, 0, sizeof(*r));
        r->clsid = CLSID_TimbroApo;
        r->Flags = (APO_FLAG)(APO_FLAG_INPLACE | APO_FLAG_DEFAULT);
        wcscpy_s(r->szFriendlyName, L"Timbro");
        wcscpy_s(r->szCopyrightInfo, L"Timbro");
        r->u32MajorVersion = 1;
        r->u32MinorVersion = 0;
        r->u32MinInputConnections = 1;
        r->u32MaxInputConnections = 1;
        r->u32MinOutputConnections = 1;
        r->u32MaxOutputConnections = 1;
        r->u32MaxInstances = 0xffffffff;
        r->u32NumAPOInterfaces = 1;
        r->iidAPOInterfaceList[0] = __uuidof(IAudioProcessingObject);
        *pp = r;
        return S_OK;
    }

    STDMETHODIMP Initialize(UINT32 cb, BYTE* dados)
    {
        if (iniciado) return APOERR_ALREADY_INITIALIZED;
        bool soDescoberta = false;
        if (dados && cb >= sizeof(APOInitSystemEffects))
        {
            APOInitSystemEffects* ini = (APOInitSystemEffects*)dados;
            if (ini->pAPOEndpointProperties)
            {
                PROPVARIANT v;
                PropVariantInit(&v);
                if (SUCCEEDED(ini->pAPOEndpointProperties->GetValue(PKEY_AudioEndpoint_GUID, &v)) && v.vt == VT_LPWSTR && v.pwszVal)
                    wcsncpy_s(guid, v.pwszVal, _TRUNCATE);
                PropVariantClear(&v);
            }
            if (cb >= sizeof(APOInitSystemEffects2))
                soDescoberta = ((APOInitSystemEffects2*)dados)->InitializeForDiscoveryOnly != FALSE;
        }
        // Só para o programa de teste: guid e chave de registro vindos do ambiente.
        wchar_t amb[64];
        if (GetEnvironmentVariableW(L"TIMBRO_TESTE_GUID", amb, 64) > 0) { wcsncpy_s(guid, amb, _TRUNCATE); teste = true; }
        // Normaliza: sem chaves, minúsculo.
        wchar_t limpo[64]; int k = 0;
        for (int i = 0; guid[i] && k < 63; i++) if (guid[i] != L'{' && guid[i] != L'}') limpo[k++] = towlower(guid[i]);
        limpo[k] = 0;
        wcscpy_s(guid, limpo);

        LerParametros();
        if (!soDescoberta)
        {
            parar = CreateEventW(NULL, TRUE, FALSE, NULL);
            vigia = CreateThread(NULL, 0, Vigiar, this, 0, NULL);
        }
        iniciado = true;
        return S_OK;
    }

    static bool EhFloat32(const UNCOMPRESSEDAUDIOFORMAT& f)
    {
        return f.guidFormatType == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT && f.dwBytesPerSampleContainer == 4 && f.dwValidBitsPerSample == 32;
    }

    HRESULT FormatoSuportado(IAudioMediaType* oposto, IAudioMediaType* pedido, IAudioMediaType** saida)
    {
        if (!pedido || !saida) return E_POINTER;
        *saida = NULL;
        UNCOMPRESSEDAUDIOFORMAT f;
        if (FAILED(pedido->GetUncompressedAudioFormat(&f))) return APOERR_FORMAT_NOT_SUPPORTED;
        UNCOMPRESSEDAUDIOFORMAT sug = f;
        bool ok = EhFloat32(f);
        if (oposto)
        {
            UNCOMPRESSEDAUDIOFORMAT o;
            if (SUCCEEDED(oposto->GetUncompressedAudioFormat(&o)))
            {
                if (o.dwSamplesPerFrame != f.dwSamplesPerFrame || o.fFramesPerSecond != f.fFramesPerSecond) ok = false;
                sug.dwSamplesPerFrame = o.dwSamplesPerFrame;
                sug.fFramesPerSecond = o.fFramesPerSecond;
                sug.dwChannelMask = o.dwChannelMask;
            }
        }
        if (ok) { pedido->AddRef(); *saida = pedido; return S_OK; }
        sug.guidFormatType = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
        sug.dwBytesPerSampleContainer = 4;
        sug.dwValidBitsPerSample = 32;
        if (FAILED(CriarTipoDeAudio(sug, saida))) return APOERR_FORMAT_NOT_SUPPORTED;
        return S_FALSE;
    }

    STDMETHODIMP IsInputFormatSupported(IAudioMediaType* o, IAudioMediaType* p, IAudioMediaType** s) { return FormatoSuportado(o, p, s); }
    STDMETHODIMP IsOutputFormatSupported(IAudioMediaType* o, IAudioMediaType* p, IAudioMediaType** s) { return FormatoSuportado(o, p, s); }

    STDMETHODIMP GetInputChannelCount(UINT32* n) { if (!n) return E_POINTER; *n = canais; return S_OK; }

    // ------------------------------------------------ IAudioProcessingObjectConfiguration
    STDMETHODIMP LockForProcess(UINT32 nEnt, APO_CONNECTION_DESCRIPTOR** ent, UINT32 nSai, APO_CONNECTION_DESCRIPTOR** sai)
    {
        if (!iniciado) return APOERR_NOT_INITIALIZED;
        if (travado) return APOERR_APO_LOCKED;
        if (nEnt != 1 || nSai != 1 || !ent || !sai || !ent[0] || !sai[0]) return APOERR_NUM_CONNECTIONS_INVALID;
        UNCOMPRESSEDAUDIOFORMAT f;
        if (!ent[0]->pFormat || FAILED(ent[0]->pFormat->GetUncompressedAudioFormat(&f)) || !EhFloat32(f))
            return APOERR_INVALID_CONNECTION_FORMAT;
        canais = f.dwSamplesPerFrame;
        taxa = f.fFramesPerSecond;
        delete[] estados;
        estados = new (std::nothrow) timbro::Estado[canais ? canais : 1];
        if (!estados) return E_OUTOFMEMORY;
        std::memset(estados, 0, sizeof(timbro::Estado) * (canais ? canais : 1));
        caixa.Pegar(atuais);
        projeto.Montar(atuais, taxa);
        travado = true;
        AnotarEstado(L"rodando");
        return S_OK;
    }

    STDMETHODIMP UnlockForProcess()
    {
        if (!travado) return APOERR_ALREADY_UNLOCKED;
        travado = false;
        AnotarEstado(L"parado");
        return S_OK;
    }

    // ------------------------------------------------ IAudioProcessingObjectRT (tempo real)
    STDMETHODIMP_(void) APOProcess(UINT32 nEnt, APO_CONNECTION_PROPERTY** ent, UINT32 nSai, APO_CONNECTION_PROPERTY** sai)
    {
        if (nEnt < 1 || nSai < 1) return;
        APO_CONNECTION_PROPERTY* e = ent[0];
        APO_CONNECTION_PROPERTY* s = sai[0];
        UINT32 n = e->u32ValidFrameCount;
        float* src = (float*)e->pBuffer;
        float* dst = (float*)s->pBuffer;

        if (caixa.Pegar(atuais)) projeto.Montar(atuais, taxa);

        if (e->u32BufferFlags == BUFFER_SILENT)
        {
            if (estados) std::memset(estados, 0, sizeof(timbro::Estado) * canais);
            if (src != dst) std::memset(dst, 0, sizeof(float) * canais * n);
            s->u32ValidFrameCount = n;
            s->u32BufferFlags = BUFFER_SILENT;
            return;
        }

        unsigned int csr = _mm_getcsr();
        _mm_setcsr(csr | 0x8040); // denormais viram zero (evita picos de CPU)
        timbro::Processar(projeto, estados, (int)canais, src, dst, n);
        _mm_setcsr(csr);

        s->u32ValidFrameCount = n;
        s->u32BufferFlags = BUFFER_VALID;
    }

    STDMETHODIMP_(UINT32) CalcInputFrames(UINT32 n) { return n; }
    STDMETHODIMP_(UINT32) CalcOutputFrames(UINT32 n) { return n; }

    // ------------------------------------------------ ajustes vindos do registro
    HKEY Raiz() { return teste ? HKEY_CURRENT_USER : HKEY_LOCAL_MACHINE; }
    const wchar_t* Chave() { return teste ? L"Software\\TimbroTeste" : CHAVE; }

    void LerParametros()
    {
        timbro::Parametros p;
        std::memset(&p, 0, sizeof(p));
        p.ligado = 1;
        HKEY k;
        if (RegOpenKeyExW(Raiz(), Chave(), 0, KEY_READ, &k) == ERROR_SUCCESS)
        {
            DWORD v = 1, tam = sizeof(v);
            if (RegQueryValueExW(k, L"Ligado", NULL, NULL, (BYTE*)&v, &tam) == ERROR_SUCCESS) p.ligado = v != 0;
            wchar_t txt[512];
            tam = sizeof(txt) - sizeof(wchar_t);
            LSTATUS r = RegGetValueW(k, L"Perfis", guid, RRF_RT_REG_SZ, NULL, txt, &tam);
            if (r != ERROR_SUCCESS) { tam = sizeof(txt) - sizeof(wchar_t); r = RegGetValueW(k, L"Perfis", L"*", RRF_RT_REG_SZ, NULL, txt, &tam); }
            if (r == ERROR_SUCCESS)
            {
                _locale_t c = _create_locale(LC_NUMERIC, "C");
                wchar_t* pos = txt;
                for (int i = 0; i < timbro::BANDAS && *pos; i++)
                {
                    wchar_t* fim;
                    p.ganho[i] = _wcstod_l(pos, &fim, c);
                    if (fim == pos) break;
                    pos = fim;
                    while (*pos == L',' || *pos == L';' || *pos == L' ') pos++;
                }
                _free_locale(c);
            }
            RegCloseKey(k);
        }
        caixa.Publicar(p);
    }

    void AnotarEstado(const wchar_t* situacao)
    {
        HKEY k;
        if (RegCreateKeyExW(Raiz(), teste ? L"Software\\TimbroTeste\\Estado" : L"SOFTWARE\\Timbro\\Estado", 0, NULL, 0, KEY_SET_VALUE, NULL, &k, NULL) != ERROR_SUCCESS) return;
        wchar_t txt[200];
        SYSTEMTIME t; GetLocalTime(&t);
        swprintf_s(txt, L"%s %u Hz %u canais %02d/%02d %02d:%02d:%02d", situacao, (unsigned)taxa, canais, t.wDay, t.wMonth, t.wHour, t.wMinute, t.wSecond);
        RegSetValueExW(k, guid[0] ? guid : L"sem-guid", 0, REG_SZ, (const BYTE*)txt, (DWORD)((wcslen(txt) + 1) * sizeof(wchar_t)));
        RegCloseKey(k);
    }

    static DWORD WINAPI Vigiar(void* arg)
    {
        TimbroApo* a = (TimbroApo*)arg;
        HANDLE mudou = CreateEventW(NULL, FALSE, FALSE, NULL);
        HKEY k = NULL;
        for (;;)
        {
            if (!k && RegOpenKeyExW(a->Raiz(), a->Chave(), 0, KEY_READ | KEY_NOTIFY, &k) != ERROR_SUCCESS) k = NULL;
            if (k) RegNotifyChangeKeyValue(k, TRUE, REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET, mudou, TRUE);
            a->LerParametros();
            HANDLE h[2] = { a->parar, mudou };
            DWORD r = WaitForMultipleObjects(2, h, FALSE, k ? INFINITE : 3000);
            if (r == WAIT_OBJECT_0) break;
        }
        if (k) RegCloseKey(k);
        CloseHandle(mudou);
        return 0;
    }
};

// ---------------------------------------------------------------- fábrica e exportações
class Fabrica : public IClassFactory
{
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory) { *ppv = static_cast<IClassFactory*>(this); return S_OK; }
        *ppv = NULL;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() { return 2; }
    STDMETHODIMP_(ULONG) Release() { return 1; }

    STDMETHODIMP CreateInstance(IUnknown* fora, REFIID riid, void** ppv)
    {
        if (!ppv) return E_POINTER;
        *ppv = NULL;
        if (fora && riid != IID_IUnknown) return CLASS_E_NOAGGREGATION;
        TimbroApo* o = new (std::nothrow) TimbroApo(fora);
        if (!o) return E_OUTOFMEMORY;
        HRESULT hr = o->Interior()->QueryInterface(riid, ppv);
        o->Interior()->Release();
        return hr;
    }
    STDMETHODIMP LockServer(BOOL t) { if (t) g_travas++; else g_travas--; return S_OK; }
};

static Fabrica g_fabrica;

extern "C" BOOL WINAPI DllMain(HINSTANCE h, DWORD motivo, LPVOID)
{
    if (motivo == DLL_PROCESS_ATTACH) { g_modulo = h; DisableThreadLibraryCalls(h); }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, void** ppv)
{
    if (clsid != CLSID_TimbroApo) return CLASS_E_CLASSNOTAVAILABLE;
    return g_fabrica.QueryInterface(riid, ppv);
}

STDAPI DllCanUnloadNow() { return (g_objetos == 0 && g_travas == 0) ? S_OK : S_FALSE; }
