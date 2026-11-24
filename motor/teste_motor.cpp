// Testa o motor fora do áudio do Windows: os filtros e o objeto COM carregado como o audiodg carrega (agregado).
#include <windows.h>
#include <initguid.h>
#include <mmdeviceapi.h>
#include <audioenginebaseapo.h>
#include <audiomediatype.h>
#include <ks.h>
#include <ksmedia.h>
#include <cstdio>
#include <cmath>
#include <vector>
#include "dsp.h"

DEFINE_GUID(CLSID_TimbroApo, 0x162bafbe, 0x06d6, 0x43a3, 0xbe, 0x19, 0x82, 0x72, 0xbd, 0x6b, 0xcf, 0x80);

static int falhas = 0;
#define CONFERE(cond, msg) do { if (cond) printf("  ok    %s\n", msg); else { printf("  FALHA %s\n", msg); falhas++; } } while (0)

// Mede o ganho (dB) de um seno passando pelo projeto.
static double Medir(const timbro::Projeto& pj, double fs, double f, double* picoSaida)
{
    const int N = (int)fs; // 1 s
    std::vector<float> buf(N * 2);
    for (int i = 0; i < N; i++) buf[i * 2] = buf[i * 2 + 1] = (float)(0.9 * std::sin(2 * timbro::PI * f * i / fs));
    timbro::Estado est[2]; memset(est, 0, sizeof(est));
    timbro::Processar(pj, est, 2, buf.data(), buf.data(), N);
    double eIn = 0, eOut = 0, pico = 0;
    for (int i = N / 2; i < N; i++)
    {
        double x = 0.9 * std::sin(2 * timbro::PI * f * i / fs);
        eIn += x * x; eOut += (double)buf[i * 2] * buf[i * 2];
        if (fabs(buf[i * 2]) > pico) pico = fabs(buf[i * 2]);
    }
    if (picoSaida) *picoSaida = pico;
    return 10 * log10(eOut / eIn);
}

static void TesteFiltros()
{
    printf("Filtros\n");
    const char* nomes[] = { "Mais grave", "Voz clara", "Musica", "Faixa unica +6 em 735 Hz" };
    double pre[][10] = { { 6, 5, 3, 1, 0, 0, 0, 0, 0, 0 }, { -3, -2, -1, 0, 1, 2.5, 3, 2, 1, 0 }, { 4, 3, 1, 0, -1, 0, 1, 2, 3, 3 }, { 0, 0, 0, 0, 6, 0, 0, 0, 0, 0 } };
    for (int fsI = 0; fsI < 2; fsI++)
    {
        double fs = fsI ? 44100 : 48000;
        for (int k = 0; k < 4; k++)
        {
            timbro::Parametros p; p.ligado = 1;
            for (int i = 0; i < 10; i++) p.ganho[i] = pre[k][i];
            timbro::Projeto pj; pj.Montar(p, fs);
            printf(" %s @ %.0f Hz  (pico da curva %.2f dB, volume x%.3f)\n  ", nomes[k], fs, pj.picoDb, pj.preamp);
            double maiorPico = 0;
            for (int i = 0; i < 10; i++)
            {
                double pk; double g = Medir(pj, fs, timbro::FREQ[i] < fs / 2 ? timbro::FREQ[i] : 1000, &pk);
                printf("%6.1f", g + pj.picoDb);
                if (pk > maiorPico) maiorPico = pk;
            }
            printf("   <- ganho medido + compensacao, por faixa\n");
            CONFERE(maiorPico <= 0.9 * 1.02, "  saida nao passa do volume de entrada (nao distorce)");
        }
    }
    timbro::Parametros zero; memset(&zero, 0, sizeof(zero)); zero.ligado = 1;
    timbro::Projeto pj; pj.Montar(zero, 48000);
    CONFERE(!pj.ativo, "tudo em zero passa direto");
    zero.ganho[3] = 5; zero.ligado = 0; pj.Montar(zero, 48000);
    CONFERE(!pj.ativo, "desligado passa direto");
}

// Objeto "de fora", como o motor de áudio faz ao agregar o APO.
struct Fora : public IUnknown
{
    long refs = 1;
    IUnknown* dentro = nullptr;
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (riid == IID_IUnknown) { *ppv = this; AddRef(); return S_OK; }
        return dentro->QueryInterface(riid, ppv);
    }
    STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&refs); }
    STDMETHODIMP_(ULONG) Release() { return InterlockedDecrement(&refs); }
};

static void GravarPerfil(const wchar_t* valor, DWORD ligado)
{
    HKEY k;
    RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\TimbroTeste\\Perfis", 0, NULL, 0, KEY_ALL_ACCESS, NULL, &k, NULL);
    RegSetValueExW(k, L"guid-de-teste", 0, REG_SZ, (const BYTE*)valor, (DWORD)((wcslen(valor) + 1) * 2));
    RegCloseKey(k);
    RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\TimbroTeste", 0, NULL, 0, KEY_ALL_ACCESS, NULL, &k, NULL);
    RegSetValueExW(k, L"Ligado", 0, REG_DWORD, (const BYTE*)&ligado, 4);
    RegCloseKey(k);
}

static double Rodar(IAudioProcessingObjectRT* rt, double f, int quadros)
{
    std::vector<float> buf(quadros * 2);
    for (int i = 0; i < quadros; i++) buf[i * 2] = buf[i * 2 + 1] = (float)(0.5 * std::sin(2 * timbro::PI * f * i / 48000));
    double eIn = 0, eOut = 0;
    for (int i = quadros / 2; i < quadros; i++) eIn += (double)buf[i * 2] * buf[i * 2];
    // em blocos de 480 quadros (10 ms), como o Windows
    for (int ini = 0; ini < quadros; ini += 480)
    {
        APO_CONNECTION_PROPERTY c = {};
        c.pBuffer = (UINT_PTR)(buf.data() + ini * 2);
        c.u32ValidFrameCount = 480;
        c.u32BufferFlags = BUFFER_VALID;
        c.u32Signature = APO_CONNECTION_PROPERTY_SIGNATURE;
        APO_CONNECTION_PROPERTY* e = &c; APO_CONNECTION_PROPERTY* s = &c;
        rt->APOProcess(1, &e, 1, &s);
    }
    for (int i = quadros / 2; i < quadros; i++) eOut += (double)buf[i * 2] * buf[i * 2];
    return 10 * log10(eOut / eIn);
}

static void TesteCom(const wchar_t* dll)
{
    printf("Objeto COM (%ls)\n", dll);
    SetEnvironmentVariableW(L"TIMBRO_TESTE_GUID", L"{GUID-DE-TESTE}");
    GravarPerfil(L"6.0,5.0,3.0,1.0,0.0,0.0,0.0,0.0,0.0,0.0", 1);

    HMODULE m = LoadLibraryW(dll);
    CONFERE(m != NULL, "DLL carrega");
    if (!m) return;
    typedef HRESULT(WINAPI * GetCO)(REFCLSID, REFIID, void**);
    GetCO get = (GetCO)GetProcAddress(m, "DllGetClassObject");
    IClassFactory* fab = nullptr;
    CONFERE(get && SUCCEEDED(get(CLSID_TimbroApo, IID_IClassFactory, (void**)&fab)), "fabrica");
    if (!fab) return;

    Fora fora;
    HRESULT hr = fab->CreateInstance(&fora, IID_IUnknown, (void**)&fora.dentro);
    CONFERE(SUCCEEDED(hr), "cria agregado");
    IAudioProcessingObject* apo = nullptr;
    IAudioProcessingObjectRT* rt = nullptr;
    IAudioProcessingObjectConfiguration* cfg = nullptr;
    IAudioSystemEffects* fx = nullptr;
    fora.QueryInterface(__uuidof(IAudioProcessingObject), (void**)&apo);
    fora.QueryInterface(__uuidof(IAudioProcessingObjectRT), (void**)&rt);
    fora.QueryInterface(__uuidof(IAudioProcessingObjectConfiguration), (void**)&cfg);
    fora.QueryInterface(__uuidof(IAudioSystemEffects), (void**)&fx);
    CONFERE(apo && rt && cfg && fx, "as 4 interfaces respondem");
    if (!apo || !rt || !cfg) return;

    APO_REG_PROPERTIES* reg = nullptr;
    CONFERE(SUCCEEDED(apo->GetRegistrationProperties(&reg)) && reg->clsid == CLSID_TimbroApo, "registro do APO");
    CoTaskMemFree(reg);

    APOInitSystemEffects2 ini = {};
    ini.APOInit.cbSize = sizeof(ini);
    ini.APOInit.clsid = CLSID_TimbroApo;
    CONFERE(SUCCEEDED(apo->Initialize(sizeof(ini), (BYTE*)&ini)), "Initialize");
    CONFERE(apo->Initialize(sizeof(ini), (BYTE*)&ini) == APOERR_ALREADY_INITIALIZED, "segundo Initialize recusado");

    UNCOMPRESSEDAUDIOFORMAT ff = { KSDATAFORMAT_SUBTYPE_IEEE_FLOAT, 2, 4, 32, 48000.0f, 3 };
    IAudioMediaType* tipo = nullptr;
    CriarTipoDeAudio(ff, &tipo);
    IAudioMediaType* sup = nullptr;
    CONFERE(apo->IsInputFormatSupported(tipo, tipo, &sup) == S_OK && sup == tipo, "aceita float 32");
    if (sup) sup->Release();
    UNCOMPRESSEDAUDIOFORMAT f16 = { KSDATAFORMAT_SUBTYPE_PCM, 2, 2, 16, 48000.0f, 3 };
    IAudioMediaType* t16 = nullptr; CriarTipoDeAudio(f16, &t16);
    sup = nullptr;
    hr = apo->IsInputFormatSupported(nullptr, t16, &sup);
    UNCOMPRESSEDAUDIOFORMAT s; bool sugereFloat = sup && SUCCEEDED(sup->GetUncompressedAudioFormat(&s)) && s.guidFormatType == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
    CONFERE(hr == S_FALSE && sugereFloat, "PCM 16 recusado com sugestao de float");
    if (sup) sup->Release();

    APO_CONNECTION_DESCRIPTOR d = { APO_CONNECTION_BUFFER_TYPE_EXTERNAL, 0, 480, tipo, APO_CONNECTION_DESCRIPTOR_SIGNATURE };
    APO_CONNECTION_DESCRIPTOR* pd = &d;
    CONFERE(SUCCEEDED(cfg->LockForProcess(1, &pd, 1, &pd)), "LockForProcess");
    UINT32 nc = 0; apo->GetInputChannelCount(&nc);
    CONFERE(nc == 2, "2 canais");

    double g62 = Rodar(rt, 62, 48000), g2k = Rodar(rt, 2000, 48000);
    printf("  perfil Mais grave: 62 Hz %.1f dB, 2 kHz %.1f dB\n", g62, g2k);
    CONFERE(g62 > g2k + 5, "grave acima do medio");
    CONFERE(g62 <= 0.2, "grave nao passa do volume original");

    GravarPerfil(L"0,0,0,0,0,0,0,0,0,0", 1);
    Sleep(300);
    double plano = Rodar(rt, 62, 9600);
    printf("  depois de zerar pelo registro: 62 Hz %.2f dB\n", plano);
    CONFERE(fabs(plano) < 0.05, "mudanca no registro chega ao motor sem reiniciar");

    GravarPerfil(L"6,5,3,1,0,0,0,0,0,0", 0);
    Sleep(300);
    double desl = Rodar(rt, 62, 9600);
    CONFERE(fabs(desl) < 0.05, "Ligado=0 passa direto");

    // buffer silencioso
    std::vector<float> z(960, 1.0f);
    APO_CONNECTION_PROPERTY c = { (UINT_PTR)z.data(), 480, BUFFER_SILENT, APO_CONNECTION_PROPERTY_SIGNATURE };
    APO_CONNECTION_PROPERTY* pc = &c;
    rt->APOProcess(1, &pc, 1, &pc);
    CONFERE(c.u32BufferFlags == BUFFER_SILENT, "silencio continua silencio");

    CONFERE(SUCCEEDED(cfg->UnlockForProcess()), "UnlockForProcess");

    wchar_t est[200]; DWORD tam = sizeof(est);
    bool anotou = RegGetValueW(HKEY_CURRENT_USER, L"Software\\TimbroTeste\\Estado", L"guid-de-teste", RRF_RT_REG_SZ, NULL, est, &tam) == ERROR_SUCCESS;
    printf("  estado anotado: %ls\n", anotou ? est : L"(nada)");
    CONFERE(anotou, "motor anota que rodou");

    tipo->Release(); t16->Release();
    apo->Release(); rt->Release(); cfg->Release(); fx->Release();
    long antes = fora.refs;
    fora.dentro->Release();
    CONFERE(antes == 1, "contagem de referencias volta ao inicio");
    typedef HRESULT(WINAPI * Pode)();
    Pode pode = (Pode)GetProcAddress(m, "DllCanUnloadNow");
    CONFERE(pode && pode() == S_OK, "objeto destruido (DllCanUnloadNow)");
    RegDeleteTreeW(HKEY_CURRENT_USER, L"Software\\TimbroTeste");
}

int wmain(int argc, wchar_t** argv)
{
    CoInitializeEx(NULL, COINIT_MULTITHREADED);
    TesteFiltros();
    if (argc > 1) TesteCom(argv[1]);
    printf(falhas ? "\n%d FALHA(S)\n" : "\nTUDO OK\n", falhas);
    return falhas ? 1 : 0;
}
