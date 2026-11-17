; Instalador de Nexus Island (Inno Setup 6).
; Se compila con tools\build-installer.ps1, que primero publica la app con .NET incluido.
;
; - Instalación por usuario (%LocalAppData%\Programs\Nexus Island): no pide permisos de administrador.
; - Incluye .NET: funciona en cualquier PC con Windows 10 (2004) u 11 x64, sin instalar nada más.
; - Desinstalador en Configuración de Windows → Aplicaciones.
; - La configuración y los logs del usuario (%AppData% / %LocalAppData%\NexusIsland) se conservan al desinstalar.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "Nexus Island"
#define AppExe "NexusIsland.exe"
#define PublishDir "..\artifacts\publish"

[Setup]
; Identificador fijo del producto: NO cambiarlo, es lo que permite actualizar sobre una versión anterior.
AppId={{6F1C2B7E-4A3D-4E8B-9C21-7D5E3A9B1F40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Nexus Island
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\artifacts\installer
OutputBaseFilename=NexusIsland-Setup-{#AppVersion}
SetupIconFile=..\assets\NexusIsland.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
ShowLanguageDialog=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; "Iniciar con Windows" lo maneja la app. Al desinstalar se borra esa entrada (si existe) para no dejar basura.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "NexusIsland"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// La isla corre en segundo plano (bandeja), así que se cierra antes de reemplazar o borrar sus archivos.
procedure CloseRunningIsland();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningIsland();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseRunningIsland();
  Result := True;
end;
