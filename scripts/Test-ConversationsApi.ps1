#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDirectory 'Mono.Cecil.dll')
$client = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory 'Content.Client.dll'))
$engine = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory 'Robust.Client.dll'))
$shared = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory 'Robust.Shared.dll'))
function Assert-Api([bool]$Condition, [string]$Name) { if (-not $Condition) { throw "Unsupported Conversations API: $Name" } }
function Assert-Members($Type, [string[]]$Methods, [string[]]$Properties = @(), [string[]]$Fields = @(), [string[]]$Events = @()) {
    Assert-Api ($null -ne $Type) 'missing type'
    foreach ($name in $Methods) { Assert-Api ($name -in @($Type.Methods.Name)) "$($Type.FullName).$name" }
    foreach ($name in $Properties) { Assert-Api ($name -in @($Type.Properties.Name)) "$($Type.FullName).$name" }
    foreach ($name in $Fields) { Assert-Api ($name -in @($Type.Fields.Name)) "$($Type.FullName).$name" }
    foreach ($name in $Events) { Assert-Api ($name -in @($Type.Events.Name)) "$($Type.FullName).$name" }
}
try {
    $chat = $client.MainModule.GetType('Content.Client.UserInterface.Systems.Chat.Widgets.ChatBox')
    Assert-Members $chat @('.ctor', 'OnMessageAdded', 'AddLine', 'Repopulate') @('Contents') @('_controller')
    Assert-Api (@($chat.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.Parameters.Count -eq 0 }).Count -eq 1) 'ChatBox()'
    $line = @($chat.Methods | Where-Object Name -eq 'AddLine')[0]
    Assert-Api ($line.Parameters.Count -eq 2 -and $line.Parameters[0].ParameterType.FullName -eq 'System.String') 'ChatBox.AddLine(string, Color)'
    Assert-Members ($client.MainModule.GetType('Content.Client.UserInterface.Systems.Chat.ChatUIController')) @() @() @('_timing', 'History')
    Assert-Members ($engine.MainModule.GetType('Robust.Client.UserInterface.Control')) @('AddChild', 'Orphan', 'Dispose', 'DisposeAllChildren', 'SetPositionInParent', 'AddStyleClass') @('Parent', 'Visible', 'HorizontalExpand', 'SetHeight', 'ToolTip')
    Assert-Members ($engine.MainModule.GetType('Robust.Client.UserInterface.Controls.BoxContainer')) @('.ctor') @('Orientation', 'SeparationOverride')
    Assert-Members ($engine.MainModule.GetType('Robust.Client.UserInterface.Controls.BaseButton')) @() @('Pressed', 'ToggleMode') @() @('OnPressed', 'OnToggled')
    Assert-Members ($engine.MainModule.GetType('Robust.Client.UserInterface.Controls.Button')) @('.ctor') @('Text', 'ClipText')
    Assert-Members ($engine.MainModule.GetType('Robust.Client.UserInterface.Controls.Label')) @('.ctor') @('Text', 'ClipText')
    Assert-Members ($engine.MainModule.GetType('Robust.Client.UserInterface.Controls.LineEdit')) @('.ctor') @('Text', 'PlaceHolder') @() @('OnTextChanged')
    Assert-Members ($engine.MainModule.GetType('Robust.Client.UserInterface.Controls.OptionButton')) @('.ctor', 'AddItem', 'SelectId') @('SelectedId') @() @('OnItemSelected')
    Assert-Members ($engine.MainModule.GetType('Robust.Client.UserInterface.Controls.ScrollContainer')) @('.ctor') @('HScrollEnabled')
    Assert-Members ($shared.MainModule.GetType('Robust.Shared.Timing.GameTiming')) @() @('TimeBase', 'TickPeriod')
    Write-Host "Conversations native API contracts passed. Robust.Client $($engine.Name.Version)."
} finally { $client.Dispose(); $engine.Dispose(); $shared.Dispose() }
