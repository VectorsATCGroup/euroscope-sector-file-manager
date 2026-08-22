<div align="center">

<img src="assets/vectors-logo-dark.png" alt="Vectors ATC Group" width="360" />

# EuroScope Sector File Manager

**A free, community desktop tool that installs and updates EuroScope Sector Files from AeroNav, safely and in one click.**

[![CI](https://github.com/VectorsATCGroup/euroscope-sector-file-manager/actions/workflows/ci.yml/badge.svg)](https://github.com/VectorsATCGroup/euroscope-sector-file-manager/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/VectorsATCGroup/euroscope-sector-file-manager?display_name=tag&sort=semver)](https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/latest)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-0078D6)](#)

[Português](#português) · [Download](#download) · [How it works](#how-it-works) · [Build from source](#build-from-source) · [Contributing](CONTRIBUTING.md)

</div>

---

> **Independent community project.** Developed and offered by [Vectors ATC Group](https://vectorsatcgroup.com/), an independent group of VATSIM virtual controllers. It is not affiliated with VATSIM, AeroNav, Navigraph, or EuroScope. Free, non-commercial, and open to the community.

## What it does

Keeping EuroScope Sector Files current normally means visiting AeroNav, downloading the right package for each FIR, unpacking it, and carefully copying files without losing your personal settings. This app does all of that for you, transactionally and safely.

- **One click install and update** for every VATSIM Brasil FIR (SBAO, SBAZ, SBBS, SBCW, SBRE).
- **Downloads only from the official AeroNav source.** No files are redistributed by this project.
- **Preserves your personalization.** Your `Settings` folder and custom files are kept across updates.
- **Transactional and reversible.** Every change is staged, an automatic backup is taken, and the operation is committed only if it fully succeeds. If anything fails, it rolls back.
- **Shows what you have.** The dashboard always displays the installed AIRAC per FIR and what is available.
- **Light and dark themes**, Portuguese and English.
- **Tells you when a new version is out.** At startup the app checks the project's GitHub Releases and offers a one-click update (download, verify, silent install, restart). It can be turned off in Settings.

## Privacy first, by design

This tool is built so that it **cannot** collect your credentials or personal data.

- **No telemetry, no analytics, no advertising, no backend.** Nothing is sent to any Vectors ATC Group server.
- **Your password is never seen by the app.** Authentication happens exclusively on the official AeroNav, VATSIM, and Navigraph pages, inside an **isolated** browser profile separate from your own browser. The app never types, reads, intercepts, or stores your password.
- **Only technical settings are stored locally** (install paths, installed versions, theme, language). The AeroNav session cookies live in the isolated profile only, to avoid signing in on every launch.
- **The update check is anonymous and optional.** The app reads the public GitHub Releases metadata of this repository (`api.github.com`) to know whether a newer version exists; the request carries only the app name and version. Installers are downloaded from this repository only and verified (SHA-256) before they run. You can disable the check in Settings.

The full usage and privacy terms are in [TERMS.txt](TERMS.txt).

## Download

Grab the latest signed installer from the [**Releases**](https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/latest) page: `VectorsEuroScopeSectorFileManager-Setup.exe`.

The installer is **per-user** (no administrator rights needed) and installs into your local app data. It also installs the Microsoft WebView2 runtime if your machine does not already have it.

**Requirements:** Windows 10 or later (x64), and EuroScope. An active AeroNav account is required to download packages, exactly as when downloading them manually from the AeroNav website.

## How it works

1. On first run, the app detects your EuroScope installation and Sector Files folder.
2. You authenticate once on the official AeroNav page (in the isolated browser window). The session is remembered so you do not log in every time.
3. The dashboard lists each FIR with its installed AIRAC and the available package.
4. Clicking **Install** or **Update** downloads the official package, takes a backup, applies the change in a temporary work area, and commits it atomically. Your personalization is preserved.

## Build from source

> The desktop app targets `net8.0-windows` (WPF) and therefore builds and runs on **Windows** only. The Core and Infrastructure libraries and the test suite are plain `net8.0`.

**Prerequisites:** the [.NET SDK](https://dotnet.microsoft.com/download) (8.0 or newer) on Windows.

```powershell
# Restore, build, and test
dotnet build Vectors.EuroScopeUpdater.sln -c Release
dotnet test  Vectors.EuroScopeUpdater.sln -c Release

# Publish a self-contained app (x64)
dotnet publish src/Vectors.EuroScopeUpdater.App/Vectors.EuroScopeUpdater.App.csproj `
  -c Release -r win-x64 --self-contained -o build/app
```

To build the installer locally you also need [Inno Setup 6](https://jrsoftware.org/isdl.php):

```powershell
iscc installer/setup.iss   # output in installer/Output/
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for the project layout, coding conventions, and the rules contributors must follow (in particular, never commit AeroNav files).

## Project structure

| Path | What it is |
|------|-----------|
| `src/Vectors.EuroScopeUpdater.Core` | Domain model, install engine, archive safety, scanning (`net8.0`). |
| `src/Vectors.EuroScopeUpdater.Infrastructure` | Package sources and parsers (`net8.0`). |
| `src/Vectors.EuroScopeUpdater.App` | WPF desktop app, MVVM, WebView2 (`net8.0-windows`). |
| `tests/Vectors.EuroScopeUpdater.Tests` | xUnit test suite (`net8.0`). |
| `installer/` | Inno Setup script for the per-user installer. |
| `docs/` | Package format and AeroNav integration notes. |
| `fixtures/` | Synthetic test fixtures only. No AeroNav content. |

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and our [Code of Conduct](CODE_OF_CONDUCT.md). Good first steps are opening an issue to discuss a change, or picking up an issue labeled `good first issue`.

## Security

Found a vulnerability or a privacy concern? Please follow the process in [SECURITY.md](SECURITY.md). Do not open a public issue for security reports.

## License

Source code is licensed under the [Apache License 2.0](LICENSE). The usage and privacy terms shown in the installer are in [TERMS.txt](TERMS.txt).

VATSIM, AeroNav, Navigraph, and EuroScope are trademarks of their respective owners. This project is not affiliated with them and only interacts with their official services.

---

<a id="português"></a>

## Português

**Ferramenta gratuita e comunitária que instala e atualiza os Sector Files do EuroScope a partir do AeroNav, com segurança e em um clique.**

> **Projeto comunitário independente.** Desenvolvido e oferecido pelo [Vectors ATC Group](https://vectorsatcgroup.com/), um grupo independente de controladores virtuais da VATSIM. Não é afiliado à VATSIM, AeroNav, Navigraph ou EuroScope. Gratuito, sem fins lucrativos e aberto à comunidade.

### O que faz

Manter os Sector Files atualizados normalmente exige acessar o AeroNav, baixar o pacote certo de cada FIR, descompactar e copiar os arquivos com cuidado para não perder suas configurações. O aplicativo faz tudo isso por você, de forma transacional e segura.

- **Instalação e atualização em um clique** para cada FIR da VATSIM Brasil (SBAO, SBAZ, SBBS, SBCW, SBRE).
- **Baixa apenas da origem oficial (AeroNav).** Este projeto não redistribui nenhum arquivo.
- **Preserva sua personalização.** Sua pasta `Settings` e arquivos personalizados são mantidos nas atualizações.
- **Transacional e reversível.** Cada mudança é preparada, um backup automático é feito, e a operação só é confirmada se der certo por completo. Se algo falhar, é revertida.
- **Mostra o que você tem.** O painel sempre exibe o AIRAC instalado por FIR e o que está disponível.
- **Temas claro e escuro**, português e inglês.
- **Avisa quando há uma nova versão.** Ao iniciar, o aplicativo consulta os Releases do projeto no GitHub e oferece atualização em um clique (baixa, verifica, instala silenciosamente e reinicia). Pode ser desativado em Configurações.

### Privacidade em primeiro lugar

O aplicativo foi construído para que **não seja capaz** de coletar suas credenciais ou dados pessoais.

- **Sem telemetria, sem analytics, sem publicidade, sem backend.** Nada é enviado a nenhum servidor do Vectors ATC Group.
- **O aplicativo nunca vê a sua senha.** A autenticação acontece exclusivamente nas páginas oficiais do AeroNav, VATSIM e Navigraph, dentro de um perfil de navegador **isolado**, separado do seu navegador. O app nunca digita, lê, intercepta ou armazena a sua senha.
- **Apenas configurações técnicas ficam salvas localmente** (caminhos, versões instaladas, tema, idioma). Os cookies da sessão do AeroNav ficam somente nesse perfil isolado, apenas para evitar novo login a cada abertura.
- **A verificação de atualizações é anônima e opcional.** O aplicativo lê os metadados públicos de Releases deste repositório no GitHub (`api.github.com`) para saber se existe versão mais nova; a consulta leva apenas o nome e a versão do aplicativo. Os instaladores são baixados somente deste repositório e verificados (SHA-256) antes de executar. A verificação pode ser desativada em Configurações.

Os termos completos de uso e privacidade estão em [TERMS.txt](TERMS.txt).

### Download

Baixe o instalador mais recente na página de [**Releases**](https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/latest): `VectorsEuroScopeSectorFileManager-Setup.exe`. A instalação é **por usuário** (não precisa de administrador) e instala o runtime WebView2 da Microsoft caso ainda não exista. Requer Windows 10 ou superior (x64), EuroScope e uma conta ativa no AeroNav.

### Como contribuir

Contribuições são bem-vindas. Leia o [CONTRIBUTING.md](CONTRIBUTING.md) e o [Código de Conduta](CODE_OF_CONDUCT.md). Uma boa forma de começar é abrir uma issue para discutir a mudança, ou pegar uma issue marcada como `good first issue`.

O código-fonte é licenciado sob a [Apache License 2.0](LICENSE). VATSIM, AeroNav, Navigraph e EuroScope são marcas de seus respectivos donos.
