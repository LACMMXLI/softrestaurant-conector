#ifndef BuildVersion
  #define BuildVersion "1.1.1"
#endif
#ifndef BuildApiUrl
  #define BuildApiUrl "https://restaurant-agent-api.fatboymexicali.com"
#endif
#ifndef BuildOutputName
  #define BuildOutputName "RestaurantAgent-Sync-Agent-Setup"
#endif
#ifndef BuildSqlUser
  #define BuildSqlUser ""
#endif
#ifndef BuildSqlPassword
  #define BuildSqlPassword ""
#endif

#define AppName "RestaurantAgent Sync Agent"
#define AppPublisher "Fatboy"
#define ServiceName "RestaurantAgentSyncAgent"
#define AgentExe "RestaurantAgent.Extractor.exe"
#define AgentUiExe "RestaurantAgent.Extractor.Ui.exe"

[Setup]
AppId={{2D9E79BA-B15A-4B4E-88D4-E5719AD0E3D7}
AppName={#AppName}
AppVersion={#BuildVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf64}\Fatboy\RestaurantAgent Sync Agent
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=dist
OutputBaseFilename={#BuildOutputName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
SetupIconFile=..\extractor-ui\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AgentUiExe}
CloseApplications=no
RestartApplications=no
UsePreviousAppDir=no
UsePreviousLanguage=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: ".build\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\RestaurantAgentSyncAgent"
Name: "{commonappdata}\RestaurantAgentSyncAgent\out"

[Icons]
Name: "{group}\Estado del servicio"; Filename: "{sys}\services.msc"
Name: "{group}\Panel del agente"; Filename: "{app}\{#AgentUiExe}"; IconFilename: "{app}\{#AgentUiExe}"
; Autoarranque para CUALQUIER usuario que inicie sesión en este equipo (no requiere admin en
; tiempo de ejecución): un acceso directo en el Startup común, no una clave HKCU (que solo
; aplicaría al usuario que corre el instalador, normalmente un administrador, no al personal
; que usa la caja). Es solo un cliente HTTP local: si el servicio está detenido, el panel lo
; indica y no pasa nada más.
Name: "{commonstartup}\RestaurantAgent Sync Agent - Panel"; Filename: "{app}\{#AgentUiExe}"

[Run]
Filename: "{app}\{#AgentUiExe}"; Description: "Abrir el panel del agente"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  AgentServiceName = '{#ServiceName}';
  AgentApiUrl = '{#BuildApiUrl}';
  DefaultSqlUser = '{#BuildSqlUser}';
  DefaultSqlPassword = '{#BuildSqlPassword}';

var
  DatabasePage: TInputQueryWizardPage;
  CredentialsPage: TInputQueryWizardPage;
  DetectedIniPath: string;
  DataRootPath: string;

function StripQuotes(Value: string): string;
begin
  Result := Trim(Value);
  if (Length(Result) >= 2) and
     (((Result[1] = '"') and (Result[Length(Result)] = '"')) or
      ((Result[1] = '''') and (Result[Length(Result)] = ''''))) then
    Result := Copy(Result, 2, Length(Result) - 2);
end;

function ReadLooseIniValue(const FileName, WantedKey: string): string;
var
  Lines: TArrayOfString;
  I, SeparatorPos: Integer;
  Line, KeyName: string;
begin
  Result := '';
  if not LoadStringsFromFile(FileName, Lines) then
    Exit;

  for I := 0 to GetArrayLength(Lines) - 1 do
  begin
    Line := Trim(Lines[I]);
    if Line = '' then
      Continue;
    if (Line[1] = ';') or (Line[1] = '#') or (Line[1] = '[') then
      Continue;

    SeparatorPos := Pos('=', Line);
    if SeparatorPos = 0 then
      SeparatorPos := Pos(':', Line);
    if SeparatorPos = 0 then
      Continue;

    KeyName := Trim(Copy(Line, 1, SeparatorPos - 1));
    if CompareText(KeyName, WantedKey) = 0 then
    begin
      Result := StripQuotes(Copy(Line, SeparatorPos + 1, MaxInt));
      Exit;
    end;
  end;
end;

function JsonEscape(Value: string): string;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
  StringChangeEx(Result, #13, '\r', True);
  StringChangeEx(Result, #10, '\n', True);
  StringChangeEx(Result, #9, '\t', True);
end;

procedure DetectRestaurantAgent(out SqlServer, SqlDatabase: string);
var
  CandidatePaths: array[0..2] of string;
  I: Integer;
begin
  SqlServer := '.\SQLEXPRESS';
  SqlDatabase := 'restaurant11';
  DetectedIniPath := '';

  CandidatePaths[0] := 'C:\nationalsoft\Softrestaurant11.0\restaurant.ini';
  CandidatePaths[1] := 'C:\nationalsoft\Softrestaurant10.0\restaurant.ini';
  CandidatePaths[2] := 'C:\nationalsoft\Softrestaurant9.5.0Pro\restaurant.ini';

  for I := 0 to 2 do
  begin
    if FileExists(CandidatePaths[I]) then
    begin
      DetectedIniPath := CandidatePaths[I];
      if ReadLooseIniValue(DetectedIniPath, 'DataSource') <> '' then
        SqlServer := ReadLooseIniValue(DetectedIniPath, 'DataSource');
      if ReadLooseIniValue(DetectedIniPath, 'Catalog') <> '' then
        SqlDatabase := ReadLooseIniValue(DetectedIniPath, 'Catalog');
      Exit;
    end;
  end;
end;

procedure InitializeWizard;
var
  SqlServer, SqlDatabase, DetectionText: string;
begin
  DataRootPath := ExpandConstant('{commonappdata}\RestaurantAgentSyncAgent');

  DetectRestaurantAgent(SqlServer, SqlDatabase);
  if DetectedIniPath <> '' then
    DetectionText := 'Se detectó RestaurantAgent en:' + #13#10 + DetectedIniPath
  else
    DetectionText := 'No se encontró restaurant.ini. Confirma manualmente el servidor y la base.';

  DatabasePage := CreateInputQueryPage(
    wpSelectDir,
    'Conexión local de RestaurantAgent',
    'Confirma la instancia y la base detectadas',
    DetectionText);
  DatabasePage.Add('Servidor o instancia SQL:', False);
  DatabasePage.Add('Base de datos:', False);
  DatabasePage.Values[0] := SqlServer;
  DatabasePage.Values[1] := SqlDatabase;

  CredentialsPage := CreateInputQueryPage(
    DatabasePage.ID,
    'Credenciales de RestaurantAgent',
    'Usa el mismo usuario SQL configurado en RestaurantAgent',
    'El agente ejecuta únicamente consultas SELECT. La contraseña no se mostrará en el resumen.');
  CredentialsPage.Add('Usuario SQL:', False);
  CredentialsPage.Add('Contraseña SQL:', True);
  CredentialsPage.Values[0] := DefaultSqlUser;
  CredentialsPage.Values[1] := DefaultSqlPassword;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = CredentialsPage.ID) and
    (DefaultSqlUser <> '') and (DefaultSqlPassword <> '');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = DatabasePage.ID then
  begin
    if Trim(DatabasePage.Values[0]) = '' then
    begin
      MsgBox('Indica el servidor o instancia SQL.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(DatabasePage.Values[1]) = '' then
    begin
      MsgBox('Indica el nombre de la base de datos.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;

  if CurPageID = CredentialsPage.ID then
  begin
    if Trim(CredentialsPage.Values[0]) = '' then
    begin
      MsgBox('Indica el usuario SQL que utiliza RestaurantAgent.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if CredentialsPage.Values[1] = '' then
    begin
      MsgBox('Indica la contraseña SQL de RestaurantAgent.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
end;

// Ejecuta Exe+Params via cmd.exe redirigiendo stdout/stderr a un archivo temporal, para
// poder mostrar el motivo real cuando un comando externo falla (por ejemplo, icacls)
// en vez de solo el codigo de salida. Devuelve False si ni siquiera se pudo lanzar cmd.exe.
function RunCaptured(const Exe, Params: string; out ResultCode: Integer;
  out OutputText: string): Boolean;
var
  LogPath, FullCommand, CmdLine: string;
  Lines: TArrayOfString;
  I: Integer;
begin
  LogPath := ExpandConstant('{tmp}') + '\srx-cmd-output.log';
  DeleteFile(LogPath);

  FullCommand := Exe + ' ' + Params + ' > "' + LogPath + '" 2>&1';
  CmdLine := '/C "' + FullCommand + '"';

  Result := Exec(ExpandConstant('{cmd}'), CmdLine, '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);

  OutputText := '';
  if LoadStringsFromFile(LogPath, Lines) then
  begin
    for I := 0 to GetArrayLength(Lines) - 1 do
      OutputText := OutputText + Lines[I] + #13#10;
  end;
  DeleteFile(LogPath);
end;

function RunSc(const Operation, Parameters: string; IgnoreFailure: Boolean): Boolean;
var
  ResultCode: Integer;
begin
  Log('Ejecutando sc.exe para ' + Operation + ': ' + Parameters);
  Result := Exec(ExpandConstant('{sys}\sc.exe'), Parameters, '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  if Result and (ResultCode <> 0) and not IgnoreFailure then
  begin
    MsgBox('Windows no pudo ' + Operation + '. Código: ' + IntToStr(ResultCode),
      mbError, MB_OK);
    Result := False;
  end;
end;

procedure StopAndDeleteService;
begin
  RunSc('detener el servicio', 'stop "' + AgentServiceName + '"', True);
  Sleep(1200);
  RunSc('eliminar el servicio anterior', 'delete "' + AgentServiceName + '"', True);
  Sleep(500);
end;

function ProtectConfiguration(const ExePath, DataRoot: string): string;
var
  TempPath, ProtectedPath, SettingsJson: string;
  ResultCode: Integer;
  Executed: Boolean;
begin
  TempPath := DataRoot + '\agent-settings.tmp.json';
  ProtectedPath := DataRoot + '\agent-settings.dpapi';
  { Ya no se pide ninguna credencial de dispositivo en el instalador (ni clave de
    activación, ni token): el equipo se vincula DESPUÉS de instalar, desde la GUI, con la
    sesión del usuario del SaaS. El nombre de máquina se toma automáticamente. }
  SettingsJson :=
    '{' +
    '"SRX_API_URL":"' + JsonEscape(AgentApiUrl) + '",' +
    '"SRX_MACHINE_NAME":"' + JsonEscape(GetComputerNameString) + '",' +
    '"SRX_SQL_SERVER":"' + JsonEscape(Trim(DatabasePage.Values[0])) + '",' +
    '"SRX_SQL_DATABASE":"' + JsonEscape(Trim(DatabasePage.Values[1])) + '",' +
    '"SRX_SQL_USER":"' + JsonEscape(Trim(CredentialsPage.Values[0])) + '",' +
    '"SRX_SQL_PASSWORD":"' + JsonEscape(CredentialsPage.Values[1]) + '",' +
    '"SRX_QUEUE_PATH":"' + JsonEscape(DataRoot + '\sync-queue.db') + '",' +
    '"SRX_OUTPUT_PATH":"' + JsonEscape(DataRoot + '\out') + '"' +
    '}';

  if not SaveStringToFile(TempPath, SettingsJson, False) then
  begin
    MsgBox('No se pudo preparar la configuración del agente.', mbError, MB_OK);
    Abort;
  end;

  try
    Executed := Exec(ExePath,
      '--protect-config "' + TempPath + '" "' + ProtectedPath + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  finally
    DeleteFile(TempPath);
  end;

  if (not Executed) or (ResultCode <> 0) then
  begin
    MsgBox('Windows no pudo cifrar la configuración del agente. Código: ' +
      IntToStr(ResultCode), mbError, MB_OK);
    Abort;
  end;
  Result := ProtectedPath;
end;

procedure ConfigureAndStartService;
var
  ExePath, DataRoot, ProtectedPath, EnvironmentBlock, RegistryPath,
    ExpectedImagePath, RegisteredImagePath, IcaclsParams, IcaclsOutput: string;
  ResultCode: Integer;
begin
  ExePath := ExpandConstant('{app}\{#AgentExe}');
  DataRoot := DataRootPath;
  RegistryPath := 'SYSTEM\CurrentControlSet\Services\' + AgentServiceName;

  // Instalación limpia: crea el directorio de datos y aplica ACL nuevas.
  ForceDirectories(DataRoot);

  IcaclsParams := '"' + DataRoot + '" /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F"';

  if not RunCaptured(ExpandConstant('{sys}\icacls.exe'), IcaclsParams, ResultCode, IcaclsOutput)
    or (ResultCode <> 0) then
  begin
    MsgBox('No se pudieron proteger los permisos de la configuración en:' + #13#10 +
      DataRoot + #13#10#13#10 +
      'Código: ' + IntToStr(ResultCode) + #13#10 +
      IcaclsOutput, mbError, MB_OK);
    Abort;
  end;

  ProtectedPath := ProtectConfiguration(ExePath, DataRoot);
  { sc.exe necesita que el valor completo de binPath sea un argumento entre comillas
    y que las comillas internas de la ruta del ejecutable lleguen escapadas. Sin este
    formato devuelve ERROR_INVALID_COMMAND_LINE (1639) cuando la ruta contiene espacios. }
  ExpectedImagePath := '"' + ExePath + '" --watch';
  if not RunSc('crear el servicio',
    'create "' + AgentServiceName + '" binPath= "\"' + ExePath +
    '\" --watch" start= auto DisplayName= "RestaurantAgent Sync Agent"', False) then
    Abort;

  RunSc('configurar la descripción', 'description "' + AgentServiceName +
    '" "Extrae reportes de RestaurantAgent en modo SELECT y los sincroniza con Fatboy."', True);
  RunSc('configurar la recuperación', 'failure "' + AgentServiceName +
    '" reset= 86400 actions= restart/60000/restart/60000/restart/60000', True);

  if (not RegQueryStringValue(HKLM, RegistryPath, 'ImagePath', RegisteredImagePath)) or
     (CompareText(RegisteredImagePath, ExpectedImagePath) <> 0) then
  begin
    MsgBox('El servicio se creó con una ruta ejecutable inválida. La instalación se cancelará.',
      mbError, MB_OK);
    Abort;
  end;

  EnvironmentBlock := 'SRX_PROTECTED_CONFIG=' + ProtectedPath + #0;

  if not RegWriteMultiStringValue(HKLM, RegistryPath, 'Environment', EnvironmentBlock) then
  begin
    MsgBox('No se pudo guardar la configuración del servicio.', mbError, MB_OK);
    Abort;
  end;

  if not RunSc('iniciar el servicio', 'start "' + AgentServiceName + '"', False) then
    Abort;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ConfigureAndStartService;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopAndDeleteService;
    DeleteFile(ExpandConstant('{commonappdata}\RestaurantAgentSyncAgent\agent-settings.dpapi'));
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  DetectionSummary: string;
begin
  if DetectedIniPath <> '' then
    DetectionSummary := 'Detectado desde: ' + DetectedIniPath + NewLine
  else
    DetectionSummary := '';

  Result :=
    'Instalación:' + NewLine +
    '  ' + ExpandConstant('{app}') + NewLine + NewLine +
    'Conector:' + NewLine +
    '  Equipo: ' + GetComputerNameString + NewLine +
    '  Sin vincular todavía — abre el panel del agente después de instalar para' + NewLine +
    '  iniciar sesión y vincularlo a tu sucursal.' + NewLine + NewLine +
    'Backend:' + NewLine +
    '  ' + AgentApiUrl + NewLine + NewLine +
    'RestaurantAgent:' + NewLine +
    DetectionSummary +
    '  Servidor: ' + DatabasePage.Values[0] + NewLine +
    '  Base: ' + DatabasePage.Values[1] + NewLine +
    '  Usuario SQL preconfigurado: ' + CredentialsPage.Values[0] + NewLine +
    '  La contraseña se cifrará con DPAPI y no se mostrará.' + NewLine + NewLine +
    'Se creará el servicio automático "RestaurantAgent Sync Agent".';
end;
