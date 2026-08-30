#ifndef BuildVersion
  #define BuildVersion "1.1.1"
#endif
#ifndef BuildApiUrl
  #define BuildApiUrl "https://softrestaurant-api.fatboymexicali.com"
#endif
#ifndef BuildOutputName
  #define BuildOutputName "SoftRestaurant-Sync-Agent-Setup"
#endif

#define AppName "SoftRestaurant Sync Agent"
#define AppPublisher "Fatboy"
#define ServiceName "SoftRestaurantSyncAgent"
#define AgentExe "SoftRestaurant.Extractor.exe"

[Setup]
AppId={{2D9E79BA-B15A-4B4E-88D4-E5719AD0E3D7}
AppName={#AppName}
AppVersion={#BuildVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf64}\Fatboy\SoftRestaurant Sync Agent
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
UninstallDisplayIcon={app}\{#AgentExe}
CloseApplications=no
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousLanguage=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: ".build\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{commonappdata}\SoftRestaurantSyncAgent"
Name: "{commonappdata}\SoftRestaurantSyncAgent\out"

[Icons]
Name: "{group}\Estado del servicio"; Filename: "{sys}\services.msc"

[Code]
const
  AgentServiceName = '{#ServiceName}';
  AgentApiUrl = '{#BuildApiUrl}';

var
  DatabasePage: TInputQueryWizardPage;
  CredentialsPage: TInputQueryWizardPage;
  ActivationPage: TInputQueryWizardPage;
  DetectedIniPath: string;
  DataRootPath: string;
  ProtectedConfigPath: string;
  ExistingConfigValid: Boolean;

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

procedure DetectSoftRestaurant(out SqlServer, SqlDatabase: string);
var
  CandidatePaths: array[0..2] of string;
  I: Integer;
begin
  SqlServer := '.\SQLEXPRESS';
  SqlDatabase := 'softrestaurant11';
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

function CheckExistingConfig: Boolean;
var
  ExeForCheck: string;
  ResultCode: Integer;
begin
  Result := False;
  DataRootPath := ExpandConstant('{commonappdata}\SoftRestaurantSyncAgent');
  ProtectedConfigPath := DataRootPath + '\agent-settings.dpapi';
  ExeForCheck := ExpandConstant('{app}\{#AgentExe}');

  { Solo hay algo que reutilizar si el equipo ya tiene la config protegida Y el ejecutable
    de una instalación previa (para poder pedirle que la valide/descifre). En instalación
    nueva ninguno de los dos existe todavía y esto sigue el flujo normal del asistente. }
  if not FileExists(ProtectedConfigPath) then
    Exit;
  if not FileExists(ExeForCheck) then
    Exit;

  Log('Verificando configuración existente en: ' + ProtectedConfigPath);
  if Exec(ExeForCheck, '--config-status "' + ProtectedConfigPath + '"', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0);

  if Result then
    Log('Configuración existente válida detectada; se conservará sin pedir datos nuevos.')
  else
    Log('No se detectó configuración existente reutilizable; se pedirán los datos normalmente.');
end;

procedure InitializeWizard;
var
  SqlServer, SqlDatabase, DetectionText: string;
begin
  // OJO: la constante app todavia NO esta inicializada aqui (InitializeWizard
  // corre antes de mostrar la pagina wpSelectDir). CheckExistingConfig usa
  // ExpandConstant de esa constante, asi que se pospone hasta
  // NextButtonClick(wpSelectDir), justo cuando ya tiene el valor confirmado
  // por el usuario. Hacerlo aqui provoca el error de Inno Setup "An attempt
  // was made to expand the 'app' constant before it was initialized" en
  // equipos con instalacion previa (unica ruta que ejercita CheckExistingConfig).
  ExistingConfigValid := False;

  DetectSoftRestaurant(SqlServer, SqlDatabase);
  if DetectedIniPath <> '' then
    DetectionText := 'Se detectó SoftRestaurant en:' + #13#10 + DetectedIniPath
  else
    DetectionText := 'No se encontró restaurant.ini. Confirma manualmente el servidor y la base.';

  DatabasePage := CreateInputQueryPage(
    wpSelectDir,
    'Conexión local de SoftRestaurant',
    'Confirma la instancia y la base detectadas',
    DetectionText);
  DatabasePage.Add('Servidor o instancia SQL:', False);
  DatabasePage.Add('Base de datos:', False);
  DatabasePage.Values[0] := SqlServer;
  DatabasePage.Values[1] := SqlDatabase;

  CredentialsPage := CreateInputQueryPage(
    DatabasePage.ID,
    'Credenciales de SoftRestaurant',
    'Usa el mismo usuario SQL configurado en SoftRestaurant',
    'El agente ejecuta únicamente consultas SELECT. La contraseña no se mostrará en el resumen.');
  CredentialsPage.Add('Usuario SQL:', False);
  CredentialsPage.Add('Contraseña SQL:', True);

  ActivationPage := CreateInputQueryPage(
    CredentialsPage.ID,
    'Activación del conector',
    'Identifica esta instalación de forma independiente',
    'Pega la clave de activación de un solo uso generada por el backend.');
  ActivationPage.Add('Clave de activación:', True);
  ActivationPage.Add('Nombre de este equipo:', False);
  ActivationPage.Values[1] := GetComputerNameString;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { Actualización sobre un equipo ya configurado: no volver a pedir servidor, base,
    usuario, contraseña ni clave de activación. La configuración protegida existente
    se conserva intacta (ver ConfigureAndStartService). }
  Result := ExistingConfigValid and
    ((PageID = DatabasePage.ID) or (PageID = CredentialsPage.ID) or (PageID = ActivationPage.ID));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = wpSelectDir then
  begin
    // Primer punto donde la constante app ya esta inicializada con el valor
    // confirmado por el usuario; aqui es seguro llamar a CheckExistingConfig
    // (ver el comentario en InitializeWizard).
    ExistingConfigValid := CheckExistingConfig;
    Exit;
  end;

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
      MsgBox('Indica el usuario SQL que utiliza SoftRestaurant.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if CredentialsPage.Values[1] = '' then
    begin
      MsgBox('Indica la contraseña SQL de SoftRestaurant.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;


  if CurPageID = ActivationPage.ID then
  begin
    if Trim(ActivationPage.Values[0]) = '' then
    begin
      MsgBox('Indica la clave de activación generada para este conector.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if Trim(ActivationPage.Values[1]) = '' then
    begin
      MsgBox('Indica el nombre o identificador de este equipo.', mbError, MB_OK);
      Result := False;
      Exit;
    end;
  end;
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
  SettingsJson :=
    '{' +
    '"SRX_API_URL":"' + JsonEscape(AgentApiUrl) + '",' +
    '"SRX_ACTIVATION_KEY":"' + JsonEscape(Trim(ActivationPage.Values[0])) + '",' +
    '"SRX_MACHINE_NAME":"' + JsonEscape(Trim(ActivationPage.Values[1])) + '",' +
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
    ExpectedImagePath, RegisteredImagePath: string;
  ResultCode: Integer;
begin
  ExePath := ExpandConstant('{app}\{#AgentExe}');
  DataRoot := DataRootPath;
  RegistryPath := 'SYSTEM\CurrentControlSet\Services\' + AgentServiceName;

  if not Exec(ExpandConstant('{sys}\icacls.exe'),
    '"' + DataRoot + '" /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
  begin
    MsgBox('No se pudieron proteger los permisos de la configuración.', mbError, MB_OK);
    Abort;
  end;

  if ExistingConfigValid then
  begin
    { Actualización: NO se toca el archivo protegido existente (activación, SQL,
      tokens). Solo se reutiliza su ruta para apuntar el servicio nuevo a él. }
    ProtectedPath := ProtectedConfigPath;
    if not FileExists(ProtectedPath) then
    begin
      MsgBox('No se encontró la configuración existente esperada en ' + ProtectedPath +
        '. Cancela la instalación y contacta a soporte antes de continuar.', mbError, MB_OK);
      Abort;
    end;
    Log('Actualización: se conserva la configuración protegida existente sin modificarla: ' + ProtectedPath);
  end
  else
    ProtectedPath := ProtectConfiguration(ExePath, DataRoot);
  { sc.exe necesita que el valor completo de binPath sea un argumento entre comillas
    y que las comillas internas de la ruta del ejecutable lleguen escapadas. Sin este
    formato devuelve ERROR_INVALID_COMMAND_LINE (1639) cuando la ruta contiene espacios. }
  ExpectedImagePath := '"' + ExePath + '" --watch';
  if not RunSc('crear el servicio',
    'create "' + AgentServiceName + '" binPath= "\"' + ExePath +
    '\" --watch" start= auto DisplayName= "SoftRestaurant Sync Agent"', False) then
    Abort;

  RunSc('configurar la descripción', 'description "' + AgentServiceName +
    '" "Extrae reportes de SoftRestaurant en modo SELECT y los sincroniza con Fatboy."', True);
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
  if CurStep = ssInstall then
    StopAndDeleteService
  else if CurStep = ssPostInstall then
    ConfigureAndStartService;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopAndDeleteService;
    DeleteFile(ExpandConstant('{commonappdata}\SoftRestaurantSyncAgent\agent-settings.dpapi'));
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo,
  MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  DetectionSummary: string;
begin
  if ExistingConfigValid then
  begin
    Result :=
      'Instalación:' + NewLine +
      '  ' + ExpandConstant('{app}') + NewLine + NewLine +
      'Conector:' + NewLine +
      '  Se detectó una configuración existente en este equipo (' + ProtectedConfigPath + ').' + NewLine +
      '  Se conservará tal cual: activación, servidor, base, usuario, contraseña,' + NewLine +
      '  token del backend y la cola local de sincronización NO se modifican.' + NewLine + NewLine +
      'Backend:' + NewLine +
      '  ' + AgentApiUrl + NewLine + NewLine +
      'Se actualizará el servicio "SoftRestaurant Sync Agent" a esta versión sin pedir' + NewLine +
      'ni sobrescribir ningún dato de configuración.';
    Exit;
  end;

  if DetectedIniPath <> '' then
    DetectionSummary := 'Detectado desde: ' + DetectedIniPath + NewLine
  else
    DetectionSummary := '';

  Result :=
    'Instalación:' + NewLine +
    '  ' + ExpandConstant('{app}') + NewLine + NewLine +
    'Conector:' + NewLine +
    '  Equipo: ' + ActivationPage.Values[1] + NewLine +
    '  Se activará en la primera conexión' + NewLine + NewLine +
    'Backend:' + NewLine +
    '  ' + AgentApiUrl + NewLine + NewLine +
    'SoftRestaurant:' + NewLine +
    DetectionSummary +
    '  Servidor: ' + DatabasePage.Values[0] + NewLine +
    '  Base: ' + DatabasePage.Values[1] + NewLine +
    '  Usuario: ' + CredentialsPage.Values[0] + NewLine + NewLine +
    'Se creará el servicio automático "SoftRestaurant Sync Agent".';
end;
