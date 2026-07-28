[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Enable', 'Disable')]
    [string]$Mode,

    [Parameter(Mandatory)]
    [string]$Executable,

    [switch]$AutoStart
)

$ErrorActionPreference = 'Stop'
$taskName = 'VelvetTools'

Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue

if ($Mode -eq 'Enable') {
    $resolvedExecutable = [System.IO.Path]::GetFullPath($Executable)
    if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
        throw "Velvet Tools executable was not found: $resolvedExecutable"
    }

    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action = New-ScheduledTaskAction -Execute $resolvedExecutable
    $principal = New-ScheduledTaskPrincipal `
        -UserId $identity `
        -LogonType Interactive `
        -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero)
    $taskParameters = @{
        Action = $action
        Principal = $principal
        Settings = $settings
    }
    if ($AutoStart) {
        $taskParameters['Trigger'] = New-ScheduledTaskTrigger -AtLogOn
    }
    $task = New-ScheduledTask @taskParameters

    Register-ScheduledTask -TaskName $taskName -InputObject $task -Force | Out-Null
}
