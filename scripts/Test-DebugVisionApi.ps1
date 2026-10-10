#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDirectory 'Mono.Cecil.dll')
$assemblies = @{}
$checks = 0
function Assert-Api([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "Unsupported Debug Vision API: $Name" }
    $script:checks++
}
function Find-Type([string]$Assembly, [string]$Name) {
    $type = $assemblies[$Assembly].MainModule.GetType($Name)
    Assert-Api ($null -ne $type) $Name
    return $type
}
try {
    foreach ($name in @('Robust.Shared', 'Robust.Client', 'Content.Client', 'Content.Shared')) {
        $assemblies[$name] = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory "$name.dll"))
    }
    $eyeSystem = Find-Type 'Content.Shared' 'Content.Shared.Movement.Systems.SharedContentEyeSystem'
    foreach ($name in @('ZoomIn', 'ZoomOut', 'ResetZoom')) {
        $matching = @($eyeSystem.Methods | Where-Object { $_.Name -eq $name -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'Robust.Shared.Player.ICommonSession' })
        Assert-Api ($matching.Count -eq 1) "Native $name session command"
    }
    Assert-Api ('ZoomMod' -in $eyeSystem.Fields.Name) 'Native zoom step'
    [void](Find-Type 'Content.Client' 'Content.Client.Movement.Systems.ContentEyeSystem')
    [void](Find-Type 'Robust.Client' 'Robust.Client.Player.IPlayerManager')
    $timing = Find-Type 'Robust.Shared' 'Robust.Shared.Timing.IGameTiming'
    Assert-Api ('get_IsFirstTimePredicted' -in $timing.Methods.Name) 'First input prediction guard'
    $lerping = Find-Type 'Content.Client' 'Content.Client.Eye.EyeLerpingSystem'
    Assert-Api (@($lerping.Methods | Where-Object { $_.Name -eq 'FrameUpdate' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'System.Single' }).Count -eq 1) 'Native eye lerper frame update'
    foreach ($name in @('Content.Client.Flash.FlashOverlay', 'Content.Client.Eye.Blinding.BlindOverlay',
        'Content.Client.Eye.Blinding.BlurryVisionOverlay', 'Content.Client.Drunk.DrunkOverlay',
        'Content.Client.Drowsiness.DrowsinessOverlay', 'Content.Client.Drugs.RainbowOverlay',
        'Content.Client.UserInterface.Systems.DamageOverlays.Overlays.DamageOverlay')) {
        $overlay = Find-Type 'Content.Client' $name
        Assert-Api (@($overlay.Methods | Where-Object { $_.Name -eq 'Draw' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -like 'Robust.Client.Graphics.OverlayDrawArgs&*' }).Count -eq 1) "$name.Draw byref-like arguments"
    }
    $blind = $assemblies['Content.Client'].MainModule.GetType('Content.Client.Eye.Blinding.BlindOverlay')
    Assert-Api ('_blindableComponent' -in $blind.Fields.Name -and '_lightManager' -in $blind.Fields.Name) 'Blind overlay render cleanup dependencies'
    $blindable = Find-Type 'Content.Shared' 'Content.Shared.Eye.Blinding.Components.BlindableComponent'
    $hud = Find-Type 'Content.Client' 'Content.Client.Overlays.EquipmentHudSystem`1'
    Assert-Api (@($hud.Fields | Where-Object { $_.Name -eq '<IsActive>k__BackingField' -and $_.FieldType.FullName -eq 'System.Boolean' }).Count -eq 1) 'Native equipment HUD activation backing field'
    $health = Find-Type 'Content.Client' 'Content.Client.Overlays.ShowHealthIconsSystem'
    Assert-Api (@($health.Fields | Where-Object { $_.Name -eq 'DamageContainers' -and $_.FieldType.FullName -eq 'System.Collections.Generic.HashSet`1<System.String>' }).Count -eq 1) 'Health HUD damage containers'
    $job = Find-Type 'Content.Client' 'Content.Client.Overlays.ShowJobIconsSystem'
    Assert-Api ($health.BaseType.ElementType.FullName -eq $hud.FullName -and $job.BaseType.ElementType.FullName -eq $hud.FullName) 'Health/job HUD inheritance'
    $jobStatus = Find-Type 'Content.Client' 'Content.Client.Access.Systems.JobStatusSystem'
    Assert-Api (@($jobStatus.Fields | Where-Object { $_.Name -eq '_showJobIcons' -and $_.FieldType.FullName -eq $job.FullName }).Count -eq 1) 'Job status HUD dependency'
    foreach ($system in @($health, $jobStatus)) {
        Assert-Api (@($system.Methods | Where-Object { $_.Name -eq 'OnGetStatusIconsEvent' -and $_.Parameters.Count -eq 2 -and $_.Parameters[1].ParameterType.FullName -eq 'Content.Shared.StatusIcon.Components.GetStatusIconsEvent&' }).Count -eq 1) "$($system.FullName) native icon collection"
    }
    Assert-Api (@($blindable.Fields | Where-Object { $_.Name -in @('LightSetup', 'GraceFrame') -and $_.FieldType.FullName -eq 'System.Boolean' }).Count -eq 2) 'Blindness render bookkeeping'
    foreach ($pair in @(
        @('Robust.Shared', 'Robust.Shared.Graphics.Eye', @('get_DrawFov','get_Zoom','set_Zoom','set_Scale')),
        @('Robust.Shared', 'Robust.Shared.IoC.IoCManager', @('ResolveType')),
        @('Robust.Client', 'Robust.Client.Graphics.EyeManager', @('get_CurrentEye','set_CurrentEye')),
        @('Robust.Client', 'Robust.Client.Graphics.LightManager', @('get_DrawLighting','get_DrawShadows','set_Enabled')),
        @('Robust.Client', 'Robust.Client.Input.InputManager', @('KeyDown','KeyUp')),
        @('Content.Client', 'Content.Client.Gameplay.GameplayState', @('Startup','Shutdown')),
        @('Robust.Client', 'Robust.Client.UserInterface.CustomControls.DefaultWindow', @('.ctor')),
        @('Robust.Client', 'Robust.Client.UserInterface.Controls.BoxContainer', @('set_Orientation','set_SeparationOverride')),
        @('Robust.Client', 'Robust.Client.UserInterface.Controls.Label', @('set_Text','set_ClipText')),
        @('Robust.Client', 'Robust.Client.UserInterface.Controls.Button', @('set_Text')),
        @('Robust.Client', 'Robust.Client.UserInterface.Controls.BaseButton', @('set_Pressed','set_ToggleMode','add_OnToggled','add_OnPressed')),
        @('Robust.Client', 'Robust.Client.UserInterface.Control', @('AddChild','Dispose','set_SetSize','get_Disposed','set_HorizontalExpand','AddStyleClass'))
    )) {
        $type = Find-Type $pair[0] $pair[1]
        foreach ($method in $pair[2]) { Assert-Api ($method -in $type.Methods.Name) "$($type.FullName).$method" }
    }
    Write-Output "Debug Vision API: $checks checks passed. $($assemblies['Robust.Shared'].Name.FullName)"
}
finally { foreach ($assembly in $assemblies.Values) { $assembly.Dispose() } }
