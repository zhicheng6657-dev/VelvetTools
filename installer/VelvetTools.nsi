Unicode True

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "nsDialogs.nsh"
!include "x64.nsh"

!ifndef APP_VERSION
  !define APP_VERSION "0.0.1-beta.1"
!endif
!ifndef APP_FILE_VERSION
  !define APP_FILE_VERSION "0.0.1.1"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\out\publish-win-x64"
!endif
!ifndef OUTPUT_DIR
  !define OUTPUT_DIR "..\out\release"
!endif
!ifndef APP_ESTIMATED_SIZE_KB
  !define APP_ESTIMATED_SIZE_KB 200000
!endif
!ifndef LICENSE_FILE
  !define LICENSE_FILE "LICENSE-AGREEMENT.txt"
!endif
!ifndef INSTALLER_EXECUTION_LEVEL
  !define INSTALLER_EXECUTION_LEVEL admin
!endif
!ifndef APP_DISPLAY_VERSION
  !define APP_DISPLAY_VERSION "Beta 0.01"
!endif

!define APP_NAME "Velvet Tools"
!define APP_PUBLISHER "Velvet"
!define APP_EXE "VelvetTools.exe"
!define APP_REG_KEY "Software\VelvetTools"
!define APP_UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\VelvetTools"
!define APP_URL "https://github.com/zhicheng6657-dev/VelvetTools-cess"

Name "${APP_NAME} ${APP_DISPLAY_VERSION}"
OutFile "${OUTPUT_DIR}\VelvetTools-Setup-${APP_VERSION}-win-x64.exe"
InstallDir "$PROGRAMFILES64\Velvet Tools"
InstallDirRegKey HKLM "${APP_REG_KEY}" "InstallDir"
RequestExecutionLevel ${INSTALLER_EXECUTION_LEVEL}
SetCompressor zlib
CRCCheck force
XPStyle on
BrandingText "Velvet Tools · GPL-3.0-or-later"
Icon "..\src\VelvetTools\Assets\app.ico"
UninstallIcon "..\src\VelvetTools\Assets\app.ico"
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${APP_FILE_VERSION}"
VIAddVersionKey /LANG=2052 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=2052 "FileVersion" "${APP_FILE_VERSION}"
VIAddVersionKey /LANG=2052 "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey /LANG=2052 "FileDescription" "Velvet Tools ${APP_DISPLAY_VERSION} 安装程序"
VIAddVersionKey /LANG=2052 "LegalCopyright" "Copyright (c) 2026 Velvet"

!define MUI_ABORTWARNING
!define MUI_ICON "..\src\VelvetTools\Assets\app.ico"
!define MUI_UNICON "..\src\VelvetTools\Assets\app.ico"
!define MUI_LICENSEPAGE_CHECKBOX
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "启动 Velvet Tools"
!define MUI_FINISHPAGE_LINK "查看项目主页"
!define MUI_FINISHPAGE_LINK_LOCATION "${APP_URL}"

; MUI2 的 CUSTOMFUNCTION_PRE 只作用于紧随其后的一个页面：覆盖升级时
; 欢迎/协议/目录页全部跳过，直接进入复制进度页，完成页保留启动入口。
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipOnUpgrade
!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipOnUpgrade
!insertmacro MUI_PAGE_LICENSE "${LICENSE_FILE}"
Page custom PrivilegePageCreate PrivilegePageLeave
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipOnUpgrade
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "English"

Var PrivilegePage
Var AdminModeCheckbox
Var AutoStartCheckbox
Var DesktopShortcutCheckbox
Var AdminModeState
Var AutoStartState
Var DesktopShortcutState
Var IsUpgrade

; 覆盖升级时跳过协议/权限/目录页：首次安装已确认过，升级只需覆盖文件。
Function SkipOnUpgrade
  ${If} $IsUpgrade == "1"
    Abort
  ${EndIf}
FunctionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP|MB_OK "Velvet Tools Beta 0.01 目前仅提供 Windows x64 版本。"
    Abort
  ${EndIf}

  ; 已有安装记录即视为覆盖升级：沿用原目录，不再重复询问。
  SetRegView 64
  ReadRegStr $0 HKLM "${APP_REG_KEY}" "InstallDir"
  ${If} $0 != ""
    StrCpy $IsUpgrade "1"
    StrCpy $INSTDIR $0
  ${Else}
    StrCpy $IsUpgrade "0"
  ${EndIf}

  StrCpy $AdminModeState ${BST_UNCHECKED}
  StrCpy $AutoStartState ${BST_UNCHECKED}
  StrCpy $DesktopShortcutState ${BST_CHECKED}

  ${GetParameters} $R0
  ${GetOptions} $R0 "/ADMINMODE=" $R1
  ${If} $R1 == "1"
    StrCpy $AdminModeState ${BST_CHECKED}
  ${EndIf}
  ${GetOptions} $R0 "/AUTOSTART=" $R1
  ${If} $R1 == "1"
    StrCpy $AutoStartState ${BST_CHECKED}
  ${EndIf}
  ${GetOptions} $R0 "/DESKTOPSHORTCUT=" $R1
  ${If} $R1 == "0"
    StrCpy $DesktopShortcutState ${BST_UNCHECKED}
  ${EndIf}
FunctionEnd

Function PrivilegePageCreate
  ; 覆盖升级时不再询问权限/自启/快捷方式，安装段也会整体跳过这些配置。
  ${If} $IsUpgrade == "1"
    Abort
  ${EndIf}

  nsDialogs::Create 1018
  Pop $PrivilegePage
  ${If} $PrivilegePage == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 24u "权限与快捷方式"
  Pop $0
  CreateFont $1 "$(^Font)" "13" "700"
  SendMessage $0 ${WM_SETFONT} $1 1

  ${NSD_CreateLabel} 0 31u 100% 34u \
    "安装程序已通过 Windows UAC 获取写入 Program Files 和注册卸载信息所需的管理员权限。下面的高权限运行模式由你单独决定。"
  Pop $0

  ${NSD_CreateCheckbox} 0 74u 100% 22u \
    "始终以最高权限运行 Velvet Tools（创建仅按需运行的 Windows 计划任务）"
  Pop $AdminModeCheckbox
  ${If} $AdminModeState == ${BST_CHECKED}
    ${NSD_Check} $AdminModeCheckbox
  ${EndIf}

  ${NSD_CreateLabel} 18u 98u 94% 30u \
    "选中后，今后从普通快捷方式启动时会转交给该计划任务，不再反复弹出 UAC。卸载时会删除此任务。"
  Pop $0

  ${NSD_CreateCheckbox} 0 135u 100% 20u "随 Windows 登录自动启动"
  Pop $AutoStartCheckbox
  ${If} $AutoStartState == ${BST_CHECKED}
    ${NSD_Check} $AutoStartCheckbox
  ${EndIf}

  ${NSD_CreateCheckbox} 0 162u 100% 20u "创建桌面快捷方式"
  Pop $DesktopShortcutCheckbox
  ${If} $DesktopShortcutState == ${BST_CHECKED}
    ${NSD_Check} $DesktopShortcutCheckbox
  ${EndIf}

  nsDialogs::Show
FunctionEnd

Function PrivilegePageLeave
  ${NSD_GetState} $AdminModeCheckbox $AdminModeState
  ${NSD_GetState} $AutoStartCheckbox $AutoStartState
  ${NSD_GetState} $DesktopShortcutCheckbox $DesktopShortcutState
FunctionEnd

Section "Velvet Tools" SEC_MAIN
  SetRegView 64
  SetShellVarContext all

  ; 避免升级时旧进程锁定文件。未运行时 taskkill 的非零退出码可忽略。
  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /IM "${APP_EXE}" /T /F'
  Sleep 400

  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*"

  SetOutPath "$INSTDIR\Installer"
  File "Configure-Privileges.ps1"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKLM "${APP_REG_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "${APP_UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "${APP_UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKLM "${APP_UNINSTALL_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKLM "${APP_UNINSTALL_KEY}" "URLInfoAbout" "${APP_URL}"
  WriteRegStr HKLM "${APP_UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKLM "${APP_UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${APP_UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKLM "${APP_UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKLM "${APP_UNINSTALL_KEY}" "EstimatedSize" ${APP_ESTIMATED_SIZE_KB}
  WriteRegDWORD HKLM "${APP_UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${APP_UNINSTALL_KEY}" "NoRepair" 1

  CreateDirectory "$SMPROGRAMS\Velvet Tools"
  CreateShortcut "$SMPROGRAMS\Velvet Tools\Velvet Tools.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortcut "$SMPROGRAMS\Velvet Tools\卸载 Velvet Tools.lnk" "$INSTDIR\Uninstall.exe"

  ; 覆盖升级：权限模式、开机自启、桌面快捷方式全部沿用现有配置，
  ; 不重跑 Configure-Privileges，避免把用户已配置的计划任务/自启项重置掉。
  ${If} $IsUpgrade == "1"
    Return
  ${EndIf}

  ${If} $DesktopShortcutState == ${BST_CHECKED}
    CreateShortcut "$DESKTOP\Velvet Tools.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  ${Else}
    Delete "$DESKTOP\Velvet Tools.lnk"
  ${EndIf}

  ${If} $AdminModeState == ${BST_CHECKED}
    DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "VelvetTools"
    ${If} $AutoStartState == ${BST_CHECKED}
      nsExec::ExecToStack \
        '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Installer\Configure-Privileges.ps1" -Mode Enable -Executable "$INSTDIR\${APP_EXE}" -AutoStart'
    ${Else}
      nsExec::ExecToStack \
        '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Installer\Configure-Privileges.ps1" -Mode Enable -Executable "$INSTDIR\${APP_EXE}"'
    ${EndIf}
    Pop $0
    Pop $1
    ${If} $0 != 0
      MessageBox MB_ICONEXCLAMATION|MB_OK \
        "软件已安装，但最高权限计划任务创建失败。你仍可在“设置 → 通用”中重新配置。$\r$\n$\r$\n$1"
    ${EndIf}
  ${Else}
    nsExec::ExecToLog \
      '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Installer\Configure-Privileges.ps1" -Mode Disable -Executable "$INSTDIR\${APP_EXE}"'
    ${If} $AutoStartState == ${BST_CHECKED}
      WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "VelvetTools" '$\"$INSTDIR\${APP_EXE}$\"'
    ${Else}
      DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "VelvetTools"
    ${EndIf}
  ${EndIf}
SectionEnd

Section "Uninstall"
  SetRegView 64
  SetShellVarContext all

  nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /IM "${APP_EXE}" /T /F'

  ${If} ${FileExists} "$INSTDIR\Installer\Configure-Privileges.ps1"
    nsExec::ExecToLog \
      '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$INSTDIR\Installer\Configure-Privileges.ps1" -Mode Disable -Executable "$INSTDIR\${APP_EXE}"'
  ${Else}
    nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Delete /F /TN "VelvetTools"'
  ${EndIf}

  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "VelvetTools"
  DeleteRegKey HKLM "${APP_UNINSTALL_KEY}"
  DeleteRegKey HKLM "${APP_REG_KEY}"

  Delete "$DESKTOP\Velvet Tools.lnk"
  RMDir /r "$SMPROGRAMS\Velvet Tools"
  RMDir /r "$INSTDIR"

  ; 对话、知识库、密钥和设置保留在 %AppData%\VelvetTools，避免误删用户数据。
SectionEnd
