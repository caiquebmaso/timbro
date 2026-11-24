# Timbro

![Timbro](docs/timbro.png)

Equalizador simples para o Windows, com motor de som próprio. Fica no ícone perto do relógio, guarda um ajuste para cada fone ou caixa, pode prender o som numa saída e impede que o fone Bluetooth caia no modo viva-voz.

Precisa de Windows 10 ou 11 de 64 bits. O .NET que ele usa já vem no Windows.

## Instalar

1. Desinstale o FxSound e o Equalizer APO, se estiverem no PC. Eles disputam o mesmo encaixe no som.
2. Baixe o `Timbro-<versão>.zip` em [Releases](https://github.com/caiquebmaso/timbro/releases/latest) e extraia numa pasta fixa (ex.: `C:\Programas\Timbro`). O `timbro.exe` e o `timbro_apo.dll` precisam ficar juntos.
3. Abra o `timbro.exe`. O Windows pode mostrar "O Windows protegeu o computador", porque o programa não tem assinatura digital paga: clique em "Mais informações" e "Executar assim mesmo".
4. Escolha o aparelho na lista e clique em "Instalar neste aparelho", embaixo à direita. O Windows pede administrador; o som para por uns segundos e volta.
5. Toque uma música. A linha de baixo passa a mostrar "Motor rodando".
6. Clique com o botão direito no ícone perto do relógio e marque "Iniciar com o Windows".

Quer compilar em vez de baixar? Veja [Compilar](#compilar).

## Usar

- Saída de áudio: escolher na lista já troca o som para esse aparelho e carrega o ajuste dele.
- Manter sempre nesta saída: se o Windows trocar sozinho (o fone reconectou, outro aparelho entrou), o Timbro volta para ela.
- Bolinhas: arrastar muda a faixa; duplo clique zera; a roda do mouse ajusta de 0,5 em 0,5 dB.
- Predefinições: Plano, Mais grave, Voz clara, Música, Filme e jogo.
- Ctrl+Alt+E liga e desliga o equalizador de qualquer lugar.
- O volume baixa sozinho na medida em que as faixas sobem, para o som não distorcer.
- Fechar a janela só esconde. Sair é pelo menu do ícone. O equalizador continua valendo com o Timbro fechado.

### Manter o fone em estéreo

O fone Bluetooth tem dois modos: estéreo, com som bom, e viva-voz, que liga o microfone do fone mas deixa o som mono e abafado, com qualidade de ligação. Quando algum programa abre o microfone (Discord, Teams, WhatsApp, às vezes o próprio Windows), o fone vai sozinho para o viva-voz, e a música fica ruim. Nesse modo o equalizador também não age.

A chave "Manter o fone em estéreo" aparece para fones que têm viva-voz. Ligada, ela desativa o viva-voz do fone e ele fica sempre em estéreo. O custo é que o microfone do fone não funciona no PC; chamadas usam outro microfone. Pede administrador para ligar e para desligar.

## Se o som sumir

Rode o `emergencia.bat` que vem junto (ou, no menu do ícone, "Remover o motor de todos os aparelhos"). Ele devolve cada aparelho como era e tira o motor. Se o som não voltar, reinicie o PC.

## Desinstalar

1. No menu do ícone, "Remover o motor de todos os aparelhos" (pede administrador).
2. Se ligou a trava de estéreo, desligue a chave antes, para o microfone do fone voltar.
3. Desmarque "Iniciar com o Windows" e clique em "Sair".
4. Apague a pasta do Timbro e `%LOCALAPPDATA%\Timbro`.

## O que muda no Windows

Ao instalar o motor num aparelho:

- Copia o motor para `C:\Program Files\Timbro\timbro_apo.dll` e registra ele.
- Liga `DisableProtectedAudioDG` em `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Audio`. O Windows só carrega efeito de som sem assinatura da Microsoft assim (o Equalizer APO faz o mesmo).
- Põe o motor como efeito de ponta (EFX) em `MMDevices\Audio\Render\{aparelho}\FxProperties`. Essas chaves são do sistema: o Timbro assume a posse delas para gravar.
- Guarda o que havia antes (valores e permissões) em `HKLM\SOFTWARE\TimbroBackup`. Remover devolve daí.
- Os ajustes ficam em `HKLM\SOFTWARE\Timbro` (Ligado e Perfis); o motor lê na hora, sem reiniciar o som, e anota em `Timbro\Estado` quando está rodando.

Ao ligar a trava de estéreo:

- Desativa o dispositivo de viva-voz do fone (`BTHHFENUM\...`, "Hands-Free AG Audio") no Gerenciador de Dispositivos, com o `pnputil` do Windows. Desligar a chave reativa.

Tudo o que é feito como administrador fica registrado em `%LOCALAPPDATA%\Timbro\motor.log`.

## Compilar

Precisa das Ferramentas de Build do Visual Studio com C++ (para o motor).

1. Rode `timbro.bat`. Na primeira vez ele compila o motor (`motor\compilar.bat`) e o Timbro em `%LOCALAPPDATA%\Timbro` e abre o programa.
2. Siga do passo 4 de [Instalar](#instalar).

### Peças

- `timbro.cs`: a janela e o ícone (C#, compila com o `csc` do .NET que vem no Windows).
- `motor\timbro_apo.cpp` e `motor\dsp.h`: o motor de som, um APO (o tipo de plugin que o próprio Windows carrega no caminho do som de cada aparelho). Faz 10 faixas de equalização, corrigidas para o som seguir a curva desenhada, e a compensação de volume.
- `motor\teste_motor.cpp`: testa os filtros e o motor fora do som do Windows.

### Diagnóstico

- `timbro.exe --listar saida.txt`: saídas, onde o motor está instalado e o estado dele.
- `timbro.exe --predef "Mais grave"`: aplica uma predefinição na saída atual e sai.
- `motor\compilar.bat` e depois `motor\bin\teste_motor.exe motor\bin\timbro_apo.dll`: testes do motor.

## Licença

MIT. Veja [LICENSE](LICENSE).
