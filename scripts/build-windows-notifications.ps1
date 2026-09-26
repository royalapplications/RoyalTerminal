# Build the reflection-free desktop notification host bridge, using the Windows
# SDK's C++/WinRT projection. No NuGet runtime or Windows App SDK install needed.
param(
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [switch]$Test
)
$ErrorActionPreference = 'Stop'
$notificationsRoot = Split-Path -Parent $PSScriptRoot
$notificationsOut = Join-Path $notificationsRoot "src/RoyalTerminal.Avalonia.App/runtimes/win-$Arch/native"
$notificationsBuild = Join-Path $notificationsRoot "native/windows-notifications/obj/$Arch"
New-Item -ItemType Directory -Force $notificationsOut, $notificationsBuild | Out-Null
$notificationsVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$notificationsVs = & $notificationsVswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$notificationsVs) { throw 'Visual C++ build tools and the Windows SDK are required.' }
$notificationsDevShell = Join-Path $notificationsVs 'Common7/Tools/Launch-VsDevShell.ps1'
$notificationsTargetArch = if ($Arch -eq 'x64') { 'amd64' } else { 'arm64' }
& $notificationsDevShell -Arch $notificationsTargetArch -HostArch amd64 -SkipAutomaticLocation | Out-Null
$notificationsSource = Join-Path $notificationsRoot 'native/windows-notifications/notifications.cpp'
$notificationsBinary = Join-Path $notificationsOut 'royalterminal-notifications.dll'
$notificationsOptions = @('/nologo', '/std:c++20', '/EHsc', '/permissive-', '/W4', '/WX', '/O2', '/MT', '/utf-8', '/DWIN32_LEAN_AND_MEAN', '/DWINVER=0x0A00', '/D_WIN32_WINNT=0x0A00')
if ($Test) {
    if ($Arch -ne 'x64') { throw 'The native test harness runs on an x64 Windows host.' }
    $notificationsSource = Join-Path $notificationsRoot 'native/windows-notifications/tests.cpp'
    $notificationsBinary = Join-Path $notificationsBuild 'notification-tests.exe'
} else { $notificationsOptions += '/LD' }
Push-Location $notificationsBuild
try {
    & cl @notificationsOptions $notificationsSource "/Fe:$notificationsBinary" /link windowsapp.lib runtimeobject.lib ole32.lib shell32.lib propsys.lib bcrypt.lib crypt32.lib shlwapi.lib windowscodecs.lib advapi32.lib user32.lib gdi32.lib
    if ($LASTEXITCODE -ne 0) { throw "Notification bridge compilation failed: $LASTEXITCODE" }
    if ($Test) {
        & $notificationsBinary
        if ($LASTEXITCODE -ne 0) { throw "Notification bridge tests failed: $LASTEXITCODE" }
    }
} finally { Pop-Location }
