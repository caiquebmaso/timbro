// Timbro: filtros do equalizador de 10 faixas (receitas RBJ do "Audio EQ Cookbook").
// Sem alocação nem trava: pode rodar na thread de tempo real do áudio.
#pragma once
#include <cmath>
#include <cstring>
#include <new>

namespace timbro {

const int BANDAS = 10;
const double FREQ[BANDAS] = { 62, 116, 214, 397, 735, 1360, 2520, 4670, 8640, 16000 };
const double Q_PICO = 1.4;          // largura de cada faixa do meio (~0,9 oitava)
const double CANTO_GRAVE = 85;      // prateleira de grave: meio caminho entre 62 e 116 Hz
const double CANTO_AGUDO = 11750;   // prateleira de agudo: meio caminho entre 8,64 e 16 kHz
const double PI = 3.14159265358979323846;

struct Coef { double b0, b1, b2, a1, a2; };

inline Coef Identidade() { Coef c = { 1, 0, 0, 0, 0 }; return c; }

inline Coef Pico(double fs, double f, double q, double db)
{
    double A = std::pow(10.0, db / 40.0), w = 2 * PI * f / fs, cw = std::cos(w), al = std::sin(w) / (2 * q);
    double a0 = 1 + al / A;
    Coef c = { (1 + al * A) / a0, (-2 * cw) / a0, (1 - al * A) / a0, (-2 * cw) / a0, (1 - al / A) / a0 };
    return c;
}

inline Coef PrateleiraGrave(double fs, double f, double db)
{
    double A = std::pow(10.0, db / 40.0), w = 2 * PI * f / fs, cw = std::cos(w), sA = std::sqrt(A);
    double al = std::sin(w) / 2 * std::sqrt(2.0); // inclinação S = 1
    double a0 = (A + 1) + (A - 1) * cw + 2 * sA * al;
    Coef c = { A * ((A + 1) - (A - 1) * cw + 2 * sA * al) / a0, 2 * A * ((A - 1) - (A + 1) * cw) / a0,
               A * ((A + 1) - (A - 1) * cw - 2 * sA * al) / a0, -2 * ((A - 1) + (A + 1) * cw) / a0,
               ((A + 1) + (A - 1) * cw - 2 * sA * al) / a0 };
    return c;
}

inline Coef PrateleiraAguda(double fs, double f, double db)
{
    double A = std::pow(10.0, db / 40.0), w = 2 * PI * f / fs, cw = std::cos(w), sA = std::sqrt(A);
    double al = std::sin(w) / 2 * std::sqrt(2.0);
    double a0 = (A + 1) - (A - 1) * cw + 2 * sA * al;
    Coef c = { A * ((A + 1) + (A - 1) * cw + 2 * sA * al) / a0, -2 * A * ((A - 1) + (A + 1) * cw) / a0,
               A * ((A + 1) + (A - 1) * cw - 2 * sA * al) / a0, 2 * ((A - 1) - (A + 1) * cw) / a0,
               ((A + 1) - (A - 1) * cw - 2 * sA * al) / a0 };
    return c;
}

// Ganho (dB) de um filtro na frequência f.
inline double RespostaDb(const Coef& c, double fs, double f)
{
    double w = 2 * PI * f / fs;
    double cr1 = std::cos(w), ci1 = -std::sin(w), cr2 = std::cos(2 * w), ci2 = -std::sin(2 * w);
    double nr = c.b0 + c.b1 * cr1 + c.b2 * cr2, ni = c.b1 * ci1 + c.b2 * ci2;
    double dr = 1 + c.a1 * cr1 + c.a2 * cr2, di = c.a1 * ci1 + c.a2 * ci2;
    return 10 * std::log10((nr * nr + ni * ni) / (dr * dr + di * di));
}

struct Parametros
{
    int ligado;
    double ganho[BANDAS];
};

// Equalizador pronto para uma taxa de amostragem: filtros + pré-amplificação que evita distorção.
struct Projeto
{
    int ativo;          // 0 = passa direto
    int usados;         // filtros com ganho diferente de zero
    Coef c[BANDAS];
    float preamp;       // multiplicador linear (<= 1)
    double picoDb;      // maior ganho da curva somada

    void Montar(const Parametros& p, double fs)
    {
        usados = 0;
        ativo = 0;
        preamp = 1;
        picoDb = 0;
        if (!p.ligado || fs < 8000) return;
        double alvo[BANDAS], g[BANDAS];
        bool algum = false;
        for (int i = 0; i < BANDAS; i++)
        {
            alvo[i] = p.ganho[i] > 24 ? 24 : (p.ganho[i] < -24 ? -24 : p.ganho[i]);
            if (FREQ[i] >= fs / 2) alvo[i] = 0;
            if (alvo[i] != 0) algum = true;
            g[i] = alvo[i];
        }
        if (!algum) return;
        // Faixas vizinhas se somam. Corrige o ganho de cada filtro para a curva
        // medida no centro de cada faixa bater com a desenhada na tela.
        usados = BANDAS;
        for (int it = 0; ; it++)
        {
            for (int i = 0; i < BANDAS; i++)
            {
                if (FREQ[i] >= fs / 2 || g[i] == 0) c[i] = Identidade();
                else if (i == 0) c[i] = PrateleiraGrave(fs, CANTO_GRAVE, g[i]);
                else if (i == BANDAS - 1) c[i] = PrateleiraAguda(fs, CANTO_AGUDO < fs * 0.45 ? CANTO_AGUDO : fs * 0.45, g[i]);
                else c[i] = Pico(fs, FREQ[i], Q_PICO, g[i]);
            }
            if (it == 8) break;
            for (int i = 0; i < BANDAS; i++)
            {
                if (FREQ[i] >= fs / 2) continue;
                double soma = 0;
                for (int j = 0; j < BANDAS; j++) soma += RespostaDb(c[j], fs, FREQ[i]);
                g[i] += 0.8 * (alvo[i] - soma);
                if (g[i] > 30) g[i] = 30;
                if (g[i] < -30) g[i] = -30;
            }
        }
        ativo = 1;
        // Maior ganho da curva somada, varrendo 20 Hz a 20 kHz (ou Nyquist).
        double fim = fs / 2 * 0.98 < 20000 ? fs / 2 * 0.98 : 20000;
        for (int k = 0; k <= 240; k++)
        {
            double f = 20 * std::pow(fim / 20, k / 240.0), soma = 0;
            for (int i = 0; i < usados; i++) soma += RespostaDb(c[i], fs, f);
            if (soma > picoDb) picoDb = soma;
        }
        preamp = (float)std::pow(10.0, -picoDb / 20.0);
    }
};

// Memória dos filtros por canal.
struct Estado { double z1[BANDAS], z2[BANDAS]; };

// Processa quadros intercalados (float32). src e dst podem ser o mesmo buffer.
inline void Processar(const Projeto& pj, Estado* est, int canais, const float* src, float* dst, unsigned quadros)
{
    if (!pj.ativo)
    {
        if (src != dst) std::memcpy(dst, src, sizeof(float) * canais * quadros);
        return;
    }
    for (int ch = 0; ch < canais; ch++)
    {
        Estado& e = est[ch];
        for (unsigned n = 0; n < quadros; n++)
        {
            double x = src[n * canais + ch] * pj.preamp;
            for (int i = 0; i < pj.usados; i++)
            {
                const Coef& c = pj.c[i];
                double y = c.b0 * x + e.z1[i];
                e.z1[i] = c.b1 * x - c.a1 * y + e.z2[i];
                e.z2[i] = c.b2 * x - c.a2 * y;
                x = y;
            }
            dst[n * canais + ch] = (float)x;
        }
    }
}

} // namespace timbro

#ifdef _UNCOMPRESSEDAUDIOFORMAT_
// IAudioMediaType próprio (o do SDK exige ATL).
class TipoDeAudio : public IAudioMediaType
{
    long refs;
    WAVEFORMATEXTENSIBLE w;
    UNCOMPRESSEDAUDIOFORMAT u;
public:
    TipoDeAudio(const WAVEFORMATEXTENSIBLE& f, const UNCOMPRESSEDAUDIOFORMAT& uf) : refs(1), w(f), u(uf) {}
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv)
    {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == __uuidof(IAudioMediaType)) { *ppv = static_cast<IAudioMediaType*>(this); AddRef(); return S_OK; }
        *ppv = NULL;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() { return InterlockedIncrement(&refs); }
    STDMETHODIMP_(ULONG) Release() { long r = InterlockedDecrement(&refs); if (!r) delete this; return r; }
    STDMETHODIMP IsCompressedFormat(BOOL* p) { if (!p) return E_POINTER; *p = FALSE; return S_OK; }
    STDMETHODIMP IsEqual(IAudioMediaType* o, DWORD* flags)
    {
        if (!o || !flags) return E_POINTER;
        UNCOMPRESSEDAUDIOFORMAT x;
        *flags = 0;
        if (SUCCEEDED(o->GetUncompressedAudioFormat(&x)) && x.guidFormatType == u.guidFormatType && x.dwSamplesPerFrame == u.dwSamplesPerFrame &&
            x.dwBytesPerSampleContainer == u.dwBytesPerSampleContainer && x.dwValidBitsPerSample == u.dwValidBitsPerSample &&
            x.fFramesPerSecond == u.fFramesPerSecond && x.dwChannelMask == u.dwChannelMask)
            *flags = 0x2 | 0x4 | 0x8; // AUDIOMEDIATYPE_EQUAL_FORMAT_TYPES | _DATA | _USER_DATA
        return *flags ? S_OK : S_FALSE;
    }
    STDMETHODIMP_(const WAVEFORMATEX*) GetAudioFormat() { return &w.Format; }
    STDMETHODIMP GetUncompressedAudioFormat(UNCOMPRESSEDAUDIOFORMAT* p) { if (!p) return E_POINTER; *p = u; return S_OK; }
};

inline HRESULT CriarTipoDeAudio(const UNCOMPRESSEDAUDIOFORMAT& u, IAudioMediaType** pp)
{
    if (!pp) return E_POINTER;
    WAVEFORMATEXTENSIBLE w;
    std::memset(&w, 0, sizeof(w));
    w.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
    w.Format.nChannels = (WORD)u.dwSamplesPerFrame;
    w.Format.nSamplesPerSec = (DWORD)u.fFramesPerSecond;
    w.Format.wBitsPerSample = (WORD)(u.dwBytesPerSampleContainer * 8);
    w.Format.nBlockAlign = (WORD)(u.dwSamplesPerFrame * u.dwBytesPerSampleContainer);
    w.Format.nAvgBytesPerSec = w.Format.nSamplesPerSec * w.Format.nBlockAlign;
    w.Format.cbSize = sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX);
    w.Samples.wValidBitsPerSample = (WORD)u.dwValidBitsPerSample;
    w.dwChannelMask = u.dwChannelMask;
    w.SubFormat = u.guidFormatType;
    *pp = new (std::nothrow) TipoDeAudio(w, u);
    return *pp ? S_OK : E_OUTOFMEMORY;
}
#endif
