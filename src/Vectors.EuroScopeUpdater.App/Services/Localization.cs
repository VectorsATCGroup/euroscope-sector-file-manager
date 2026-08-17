using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectors.EuroScopeUpdater.App.Services;

public enum AppLanguage { Pt, En }

/// <summary>
/// Runtime PT/EN localization. All translations live in one table. XAML binds through the
/// <c>{loc:Loc Key}</c> markup extension (to this class's indexer); code calls <see cref="T"/>.
/// Switching language raises <c>Item[]</c> so every binding refreshes instantly.
/// </summary>
public sealed partial class Localization : ObservableObject
{
    public static Localization Instance { get; } = new();

    [ObservableProperty] private AppLanguage _language = AppLanguage.Pt;

    public string this[string key] => T(key);

    public string T(string key) =>
        _table.TryGetValue(key, out var v) ? (Language == AppLanguage.Pt ? v.Pt : v.En) : key;

    public void SetLanguage(AppLanguage lang)
    {
        if (Language == lang) return;
        Language = lang;
        OnPropertyChanged("Item[]"); // refresh all {loc:Loc} bindings
    }

    private static readonly Dictionary<string, (string Pt, string En)> _table = new()
    {
        // ── Common ──────────────────────────────────────────────────────────────
        ["Common_ThemeTooltip"] = ("Alternar tema claro/escuro", "Toggle light/dark theme"),
        ["Common_Cancel"] = ("Cancelar", "Cancel"),
        ["Common_Back"] = ("Voltar", "Back"),
        ["Common_Continue"] = ("Continuar", "Continue"),
        ["Common_Settings"] = ("Configurações", "Settings"),
        ["Common_Authenticate"] = ("Autenticar", "Authenticate"),
        ["Auth_Checking"] = ("Verificando sessão…", "Checking session…"),
        ["Brand_ProductName"] = ("EuroScope Sector File Manager", "EuroScope Sector File Manager"),

        // ── Wizard ──────────────────────────────────────────────────────────────
        ["Wizard_Header"] = ("VECTORS ATC GROUP · CONFIGURAÇÃO", "VECTORS ATC GROUP · SETUP"),
        ["Wizard_Welcome_Title"] = ("Ferramenta comunitária gratuita", "Free community tool"),
        ["Wizard_Welcome_1"] = (
            "O Vectors ATC Group EuroScope Sector File Manager é um utilitário gratuito e sem fins lucrativos, feito para simplificar a instalação e a atualização dos Sector Files do EuroScope.",
            "The Vectors ATC Group EuroScope Sector File Manager is a free, non-commercial utility designed to simplify installing and updating EuroScope Sector Files."),
        ["Wizard_Welcome_2"] = (
            "A autenticação é feita diretamente pelos serviços oficiais utilizados pelo AeroNav. Este aplicativo não solicita nem armazena sua senha da VATSIM ou da Navigraph.",
            "Authentication is performed directly through the official services used by AeroNav. This application never requests or stores your VATSIM or Navigraph password."),
        ["Wizard_Welcome_3"] = (
            "Somente as configurações técnicas necessárias para o funcionamento do aplicativo são armazenadas localmente no seu computador, como os caminhos de instalação e as versões dos pacotes instalados.",
            "Only the technical settings the app needs to work are stored locally on your computer, such as installation paths and installed package versions."),
        ["Wizard_Welcome_4"] = (
            "Os Sector Files continuam hospedados e distribuídos pelo seu provedor oficial.",
            "Sector Files remain hosted and distributed by their official provider."),
        ["Wizard_ES_Title"] = ("Localizar o EuroScope", "Locate EuroScope"),
        ["Wizard_ES_Desc"] = (
            "Procuramos o EuroScope na sua pasta AppData. Se ele estiver em outro local, selecione-o manualmente.",
            "We look for EuroScope in your AppData folder. If it is elsewhere, select it manually."),
        ["Wizard_ES_FolderLabel"] = ("PASTA DO EUROSCOPE", "EUROSCOPE FOLDER"),
        ["Wizard_ES_DetectAgain"] = ("Detectar novamente", "Detect again"),
        ["Wizard_ES_Locate"] = ("Localizar EuroScope…", "Locate EuroScope…"),
        ["Wizard_Loc_Title"] = ("Local dos Sector Files", "Sector Files location"),
        ["Wizard_Loc_Recommended"] = ("Recomendado, dentro do EuroScope", "Recommended, inside EuroScope"),
        ["Wizard_Loc_Custom"] = ("Local personalizado", "Custom location"),
        ["Wizard_Loc_Choose"] = ("Escolher…", "Choose…"),
        ["Wizard_Auth_Title"] = ("Autenticação", "Authentication"),
        ["Wizard_Auth_Desc"] = (
            "Faça login nas páginas oficiais do AeroNav em uma janela segura. Suas credenciais nunca são vistas nem armazenadas por este aplicativo. Você também pode concluir agora e autenticar depois.",
            "Sign in on the official AeroNav pages in a secure window. Your credentials are never seen or stored by this app. You can also finish now and authenticate later."),
        ["Wizard_Auth_Now"] = ("Autenticar agora", "Authenticate now"),
        ["Wizard_Auth_Done"] = ("✓ Autenticado", "✓ Authenticated"),
        ["Wizard_Ready_Title"] = ("Tudo pronto", "All set"),
        ["Wizard_Ready_Desc"] = (
            "A configuração foi concluída. Agora você pode instalar e atualizar seus Sector Files em um só lugar.",
            "Setup is complete. You can now install and update your Sector Files in one place."),

        // ── Dashboard ───────────────────────────────────────────────────────────
        ["Dash_Refresh"] = ("Recarregar", "Refresh"),
        ["Dash_UpdateAll"] = ("Atualizar tudo", "Update all"),
        ["Dash_Division"] = ("DIVISÃO", "DIVISION"),
        ["Dash_SectorFiles"] = ("SECTOR FILES", "SECTOR FILES"),
        ["Dash_ESDetected"] = ("EuroScope detectado", "EuroScope detected"),
        ["Dash_ESNotSet"] = ("EuroScope não definido", "EuroScope not set"),
        ["Dash_Installed"] = ("INSTALADO", "INSTALLED"),
        ["Dash_Available"] = ("DISPONÍVEL", "AVAILABLE"),
        ["Dash_Gate_Title"] = ("Autenticação necessária", "Authentication required"),
        ["Dash_Gate_Body"] = (
            "Para instalar ou atualizar os Sector Files, entre nos serviços oficiais do AeroNav. Suas credenciais nunca são vistas nem armazenadas por este aplicativo.",
            "To install or update Sector Files, sign in to the official AeroNav services. Your credentials are never seen or stored by this app."),

        // ── FIR status / actions ────────────────────────────────────────────────
        ["Fir_NotInstalled"] = ("Não instalado", "Not installed"),
        ["Fir_UpToDate"] = ("Atualizado", "Up to date"),
        ["Fir_UpdateAvailable"] = ("Atualização disponível", "Update available"),
        ["Fir_LocallyModified"] = ("Modificado localmente", "Locally modified"),
        ["Fir_Incomplete"] = ("Instalação incompleta", "Installation incomplete"),
        ["Fir_InstalledAirac"] = ("Instalado, AIRAC {0}", "Installed, AIRAC {0}"),
        ["Fir_VersionUnknown"] = ("Instalado, versão desconhecida", "Installed, version unknown"),
        ["Fir_Install"] = ("Instalar", "Install"),
        ["Fir_Update"] = ("Atualizar", "Update"),

        // ── Operation phases / modal ────────────────────────────────────────────
        ["Op_Installing"] = ("Instalando {0}", "Installing {0}"),
        ["Op_Updating"] = ("Atualizando {0}", "Updating {0}"),
        ["Op_Prepare"] = ("Preparando…", "Preparing…"),
        ["Op_Download"] = ("Baixando pacote…", "Downloading package…"),
        ["Op_ValidateArchive"] = ("Validando arquivo…", "Validating archive…"),
        ["Op_Stage"] = ("Extraindo arquivos…", "Extracting files…"),
        ["Op_ValidateStaging"] = ("Validando arquivos extraídos…", "Validating extracted files…"),
        ["Op_Backup"] = ("Fazendo backup da instalação atual…", "Backing up current installation…"),
        ["Op_Commit"] = ("Instalando…", "Installing…"),
        ["Op_Verify"] = ("Validando…", "Validating…"),
        ["Op_RollingBack"] = ("Revertendo…", "Rolling back…"),
        ["Op_Cancelling"] = ("Cancelando…", "Cancelling…"),

        // ── Settings ────────────────────────────────────────────────────────────
        ["Set_Locations"] = ("LOCAIS", "LOCATIONS"),
        ["Set_ESFolder"] = ("Pasta do EuroScope", "EuroScope folder"),
        ["Set_Change"] = ("Alterar…", "Change…"),
        ["Set_SFLocation"] = ("Local dos Sector Files", "Sector Files location"),
        ["Set_BackupsKeep"] = ("Backups a manter", "Backups to keep"),
        ["Set_Save"] = ("Salvar", "Save"),
        ["Set_Language"] = ("IDIOMA", "LANGUAGE"),
        ["Set_AuthData"] = ("AUTENTICAÇÃO E DADOS", "AUTHENTICATION & DATA"),
        ["Set_LogoutDesc"] = (
            "Sair limpa a sessão do AeroNav do perfil de navegador isolado deste aplicativo. Nenhuma senha é armazenada.",
            "Signing out clears the AeroNav session from this app's isolated browser profile. No passwords are stored."),
        ["Set_Logout"] = ("Sair do AeroNav", "Log out of AeroNav"),
        ["Set_OpenAppData"] = ("Abrir dados do app", "Open app data"),
        ["Set_OpenLogs"] = ("Abrir logs", "Open logs"),
        ["Set_OpenBackups"] = ("Abrir backups", "Open backups"),
        ["Set_About"] = ("SOBRE", "ABOUT"),
        ["Set_AboutDesc"] = (
            "Ferramenta comunitária gratuita e sem fins lucrativos. Sem telemetria, sem analytics, sem anúncios.",
            "Free, non-commercial community tool. No telemetry, no analytics, no advertising."),
        ["Set_Back"] = ("Voltar ao painel", "Back to dashboard"),
        ["Set_Version"] = ("VERSÃO {0}", "VERSION {0}"),
        ["Set_About_Independent"] = (
            "O Vectors ATC Group é um grupo independente de controladores virtuais da VATSIM. Este é um projeto sem fins lucrativos, oferecido gratuitamente à comunidade.",
            "Vectors ATC Group is an independent group of VATSIM virtual controllers. This is a non-profit project, offered free to the community."),
        ["Set_About_Link"] = ("Visite vectorsatcgroup.com", "Visit vectorsatcgroup.com"),

        // ── How this software works ──────────────────────────────────────────────
        ["Set_How_Title"] = ("COMO ESTE SOFTWARE FUNCIONA", "HOW THIS SOFTWARE WORKS"),
        ["Set_How_Login_H"] = ("Autenticação", "Authentication"),
        ["Set_How_Login"] = (
            "A autenticação é feita exclusivamente nas páginas oficiais do AeroNav, VATSIM e Navigraph, exibidas em uma janela de navegador isolada dentro do aplicativo. Você digita suas credenciais apenas nessas páginas oficiais, o aplicativo nunca vê, nunca digita e nunca recebe a sua senha. Ele apenas detecta quando você chega à lista de pacotes para saber que o login foi concluído.",
            "Authentication happens exclusively on the official AeroNav, VATSIM and Navigraph pages, shown in an isolated browser window inside the app. You enter your credentials only on those official pages, the app never sees, never types and never receives your password. It only detects when you reach the package listing to know that sign-in succeeded."),
        ["Set_How_Storage_H"] = ("O que é armazenado", "What is stored"),
        ["Set_How_Storage"] = (
            "Somente configurações técnicas são armazenadas localmente no seu computador: caminhos de instalação, versões dos pacotes instalados, tema e idioma. A sessão do AeroNav (cookies) é mantida em um perfil de navegador isolado, separado do seu navegador, apenas para evitar que você precise entrar toda vez. Nada disso é enviado para servidores do Vectors.",
            "Only technical settings are stored locally on your computer: installation paths, installed package versions, theme and language. The AeroNav session (cookies) is kept in an isolated browser profile, separate from your own browser, only so you don't have to sign in every time. None of this is ever sent to any Vectors server."),
        ["Set_How_NoCollect"] = (
            "Este aplicativo NÃO coleta credenciais nem dados pessoais. Sem telemetria, sem analytics, sem anúncios. Os Sector Files permanecem hospedados e distribuídos pelo provedor oficial (AeroNav).",
            "This application does NOT collect credentials or personal data. No telemetry, no analytics, no advertising. Sector Files remain hosted and distributed by their official provider (AeroNav)."),
    };
}
