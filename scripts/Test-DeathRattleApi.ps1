#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDirectory 'Mono.Cecil.dll')
$assemblies = @{}
function Assert-Api([bool]$Condition, [string]$Name) { if (-not $Condition) { throw "Unsupported Death Rattle API: $Name" } }
function Find-Type([string]$Assembly, [string]$Type) {
    $found = $assemblies[$Assembly].MainModule.GetType($Type)
    Assert-Api ($null -ne $found) $Type
    return $found
}
function Find-Method($Type, [string]$Name, [int]$ParameterCount) {
    $found = @($Type.Methods | Where-Object { $_.Name -eq $Name -and $_.Parameters.Count -eq $ParameterCount })
    Assert-Api ($found.Count -eq 1) "$($Type.FullName).$Name/$ParameterCount"
    return $found[0]
}
try {
    foreach ($name in @('Content.Client', 'Content.Shared', 'Robust.Client', 'Robust.Shared')) {
        $assemblies[$name] = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory "$name.dll"))
    }
    $controller = Find-Type 'Content.Client' 'Content.Client.UserInterface.Systems.Alerts.AlertsUIController'
    $sync = Find-Method $controller 'SystemOnSyncAlerts' 2
    Assert-Api ($sync.Parameters[1].ParameterType.FullName -eq 'System.Collections.Generic.IReadOnlyDictionary`2<Content.Shared.Alert.AlertKey,Content.Shared.Alert.AlertState>') 'Synced alert dictionary'
    $null = Find-Method $controller 'SystemOnClearAlerts' 2
    Assert-Api ('_player' -in $controller.Fields.Name) 'AlertsUIController._player'
    $alerts = Find-Type 'Content.Client' 'Content.Client.Alerts.ClientAlertsSystem'
    Assert-Api ('_playerManager' -in $alerts.Fields.Name -and 'ActiveAlerts' -in $alerts.Properties.Name) 'Local active alerts'
    $update = Find-Method (Find-Type 'Content.Shared' 'Content.Shared.Alert.AlertsSystem') 'Update' 1
    Assert-Api ($update.Parameters[0].ParameterType.FullName -eq 'System.Single') 'AlertsSystem.Update(float)'
    $state = Find-Type 'Content.Shared' 'Content.Shared.Alert.AlertState'
    Assert-Api ('Type' -in $state.Fields.Name -and @($state.Fields | Where-Object { $_.Name -eq 'Severity' -and $_.FieldType.FullName -eq 'System.Nullable`1<System.Int16>' }).Count -eq 1) 'Health severity'
    $send = Find-Method (Find-Type 'Content.Client' 'Content.Client.Chat.Managers.IChatManager') 'SendMessage' 2
    Assert-Api ($send.Parameters[0].ParameterType.FullName -eq 'System.String' -and $send.Parameters[1].ParameterType.FullName -eq 'Content.Shared.Chat.ChatSelectChannel') 'Native chat signature'
    Assert-Api ('Radio' -in (Find-Type 'Content.Shared' 'Content.Shared.Chat.ChatSelectChannel').Fields.Name) 'Radio channel'
    $null = Find-Method (Find-Type 'Robust.Shared' 'Robust.Shared.IoC.IoCManager') 'ResolveType' 1
    $null = Find-Type 'Content.Shared' 'Content.Shared.Humanoid.HumanoidProfileComponent'
    $changed = Find-Method (Find-Type 'Content.Shared' 'Content.Shared.Damage.Systems.DamageableSystem') 'OnEntityDamageChanged' 4
    Assert-Api ($changed.Parameters[0].ParameterType.FullName -eq 'Robust.Shared.GameObjects.Entity`1<Content.Shared.Damage.Components.DamageableComponent>' -and $changed.Parameters[3].ParameterType.FullName -eq 'System.Nullable`1<Robust.Shared.GameObjects.EntityUid>') 'Optional damage Origin'
    $null = Find-Method (Find-Type 'Content.Shared' 'Content.Shared.IdentityManagement.Identity') 'Name' 3
    $timing = Find-Type 'Robust.Shared' 'Robust.Shared.Timing.IGameTiming'
    Assert-Api ('IsFirstTimePredicted' -in $timing.Properties.Name -and 'ApplyingState' -in $timing.Properties.Name) 'Prediction replay vs authoritative state guard'
    $null = Find-Method (Find-Type 'Content.Shared' 'Content.Shared.Damage.DamageSpecifier') 'GetTotal' 0
    $null = Find-Method (Find-Type 'Content.Shared' 'Content.Shared.FixedPoint.FixedPoint2') 'Float' 0
    $damageable = Find-Type 'Content.Shared' 'Content.Shared.Damage.Components.DamageableComponent'
    Assert-Api (@($damageable.Fields | Where-Object { $_.Name -eq 'Damage' -and $_.FieldType.FullName -eq 'Content.Shared.Damage.DamageSpecifier' }).Count -eq 1) 'Current injury specifier (not cached TotalDamage)'
    $mob = Find-Type 'Content.Shared' 'Content.Shared.Mobs.Components.MobStateComponent'
    Assert-Api ('CurrentState' -in $mob.Properties.Name) 'Consciousness state'
    $entity = Find-Type 'Robust.Shared' 'Robust.Shared.GameObjects.Entity`1'
    Assert-Api ('Comp' -in $entity.Fields.Name -and 'Owner' -in $entity.Fields.Name) 'Boxed damage entity owner/component'
    $manager = Find-Type 'Robust.Shared' 'Robust.Shared.GameObjects.IEntityManager'
    Assert-Api (@($manager.Methods | Where-Object { $_.Name -eq 'TryGetComponent' -and -not $_.HasGenericParameters -and $_.Parameters.Count -eq 3 -and $_.Parameters[0].ParameterType.FullName -eq 'Robust.Shared.GameObjects.EntityUid' -and $_.Parameters[1].ParameterType.FullName -eq 'System.Type' }).Count -eq 1) 'Runtime component query'
    Assert-Api (@($manager.Methods | Where-Object { $_.Name -eq 'GetEntity' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'Robust.Shared.GameObjects.NetEntity' }).Count -eq 1) 'Network entity conversion'
    $lunge = Find-Method (Find-Type 'Content.Client' 'Content.Client.Weapons.Melee.MeleeWeaponSystem') 'OnMeleeLunge' 1
    Assert-Api ($lunge.Parameters[0].ParameterType.FullName -eq 'Content.Shared.Weapons.Melee.Events.MeleeLungeEvent') 'Server lunge handler'
    $ev = Find-Type 'Content.Shared' 'Content.Shared.Weapons.Melee.Events.MeleeLungeEvent'
    Assert-Api ('Entity' -in $ev.Fields.Name -and 'Weapon' -in $ev.Fields.Name -and @($ev.Fields | Where-Object { $_.Name -eq 'LocalPos' -and $_.FieldType.FullName -eq 'System.Numerics.Vector2' }).Count -eq 1) 'Lunge source/natural weapon/parent-frame offset'
    $transform = Find-Type 'Robust.Shared' 'Robust.Shared.GameObjects.TransformComponent'
    Assert-Api ('LocalPosition' -in $transform.Properties.Name -and 'ParentUid' -in $transform.Properties.Name) 'Same-parent geometry'
    Write-Host "Death Rattle native API contracts passed. Robust.Client $($assemblies['Robust.Client'].Name.Version)."
} finally { foreach ($assembly in $assemblies.Values) { $assembly.Dispose() } }
