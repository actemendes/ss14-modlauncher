#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDirectory 'Mono.Cecil.dll')
$assemblies = @{}
$checks = 0
function Assert-Api([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "Unsupported ChemMaster API: $Name" }
    $script:checks++
}
function Find-Type([string]$Assembly, [string]$Name) {
    $type = $assemblies[$Assembly].MainModule.GetType($Name)
    Assert-Api ($null -ne $type) $Name
    return $type
}
function Member($Type, [string]$Name) {
    Assert-Api ($Name -in $Type.Fields.Name -or $Name -in $Type.Properties.Name -or $Name -in $Type.Methods.Name -or $Name -in $Type.Events.Name) "$($Type.FullName).$Name"
}
try {
    foreach ($name in @('Content.Client', 'Content.Shared', 'Robust.Client', 'Robust.Shared')) {
        $assemblies[$name] = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory "$name.dll"))
    }
    $bui = Find-Type 'Content.Client' 'Content.Client.Chemistry.UI.ChemMasterBoundUserInterface'
    foreach ($name in @('Open','UpdateState','_window')) { Member $bui $name }
    Member (Find-Type 'Content.Client' 'Content.Client.Chemistry.UI.ChemMasterWindow') 'Tabs'
    $bound = Find-Type 'Robust.Shared' 'Robust.Shared.GameObjects.BoundUserInterface'
    foreach ($name in @('State','IsOpened','EntMan','Owner','UiKey','PlayerManager','SendMessage','Close','Dispose','OnProtoReload')) { Member $bound $name }
    $state = Find-Type 'Content.Shared' 'Content.Shared.Chemistry.ChemMasterBoundUserInterfaceState'
    foreach ($name in @('BufferReagents','InputContainerInfo','Mode')) { Member $state $name }
    $container = Find-Type 'Content.Shared' 'Content.Shared.Chemistry.ContainerInfo'
    foreach ($name in @('Reagents','MaxVolume','CurrentVolume')) { Member $container $name }
    foreach ($pair in @(
        @('Content.Shared.Chemistry.Reagent.ReagentQuantity', @('Quantity','Reagent')),
        @('Content.Shared.Chemistry.Reagent.ReagentId', @('Prototype','Data')),
        @('Content.Shared.FixedPoint.FixedPoint2', @('Value')),
        @('Content.Shared.Chemistry.Reagent.ReagentPrototype', @('ID','LocalizedName','SpecificHeat')),
        @('Content.Shared.Chemistry.Reaction.ReactionPrototype', @('ID','Reactants','Products','Priority','MinimumTemperature','MaximumTemperature','Quantized','ConserveEnergy','Effects','MixingCategories')),
        @('Content.Shared.Chemistry.Reaction.ReactantPrototype', @('Amount','Catalyst'))
    )) { $type = Find-Type 'Content.Shared' $pair[0]; foreach ($name in $pair[1]) { Member $type $name } }
    $message = Find-Type 'Content.Shared' 'Content.Shared.Chemistry.ChemMasterReagentAmountButtonMessage'
    Assert-Api (@($message.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.Parameters.Count -eq 3 }).Count -eq 1) 'Transfer constructor'
    $amount = Find-Type 'Content.Shared' 'Content.Shared.Chemistry.ChemMasterReagentAmount'
    foreach ($name in @('U1','All')) { Member $amount $name }
    $slots = Find-Type 'Content.Shared' 'Content.Shared.Containers.ItemSlots.ItemSlotsSystem'
    Assert-Api (@($slots.Methods | Where-Object { $_.Name -eq 'GetItemOrNull' -and $_.Parameters.Count -eq 3 }).Count -eq 1) 'Beaker identity'
    $prototype = Find-Type 'Robust.Shared' 'Robust.Shared.Prototypes.IPrototypeManager'
    Assert-Api (@($prototype.Methods | Where-Object { $_.Name -eq 'EnumeratePrototypes' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'System.Type' }).Count -eq 1) 'Runtime prototype enumeration'
    $control = Find-Type 'Robust.Client' 'Robust.Client.UserInterface.Control'
    foreach ($name in @('DoFrameUpdateRecursive','AddChild','Dispose','HorizontalExpand','VerticalExpand','SetHeight','MinHeight','SetWidth','Children','ToolTip','Visible','MouseFilter','AddStyleClass')) { Member $control $name }
    foreach ($pair in @(
        @('BoxContainer', @('Orientation','SeparationOverride')),
        @('Label', @('Text','ClipText','Align','FontColorOverride')),
        @('Button', @('Text','TextAlign','ClipText')),
        @('ProgressBar', @('ForegroundStyleBoxOverride')),
        @('ScrollContainer', @()),
        @('OptionButton', @('Clear','AddItem','SelectId','SelectedId','OnItemSelected')),
        @('LineEdit', @('Text','PlaceHolder','OnTextChanged','OnTextEntered','OnFocusExit','Editable')),
        @('TabContainer', @('SetTabTitle')),
        @('BaseButton', @('OnPressed','OnToggled','Pressed','Group','ToggleMode','Disabled')),
        @('CheckBox', @('Text')),
        @('Range', @('MinValue','MaxValue','Rounded','RoundingDecimals','Value','SetValueWithoutEvent','OnValueChanged')),
        @('Slider', @('OnReleased'))
    )) { $type = Find-Type 'Robust.Client' ('Robust.Client.UserInterface.Controls.' + $pair[0]); foreach ($name in $pair[1]) { Member $type $name } }
    $group = Find-Type 'Robust.Client' 'Robust.Client.UserInterface.Controls.ButtonGroup'
    Assert-Api (@($group.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.Parameters.Count -eq 1 }).Count -eq 1) 'Exclusive beaker phase group'
    Member (Find-Type 'Robust.Client' 'Robust.Client.Graphics.StyleBoxFlat') 'BackgroundColor'
    $assemblies['Robust.Shared.Maths'] = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameDirectory 'Robust.Shared.Maths.dll'))
    $color = Find-Type 'Robust.Shared.Maths' 'Robust.Shared.Maths.Color'
    Assert-Api (@($color.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.Parameters.Count -eq 4 -and $_.Parameters[0].ParameterType.FullName -eq 'System.Single' }).Count -eq 1) 'Status colour constructor'
    $slotMessage = Find-Type 'Content.Shared' 'Content.Shared.Containers.ItemSlots.ItemSlotButtonPressedEvent'
    Assert-Api (@($slotMessage.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.Parameters.Count -eq 3 }).Count -eq 1) 'Input beaker insert/eject message'
    Write-Output "ChemMaster API: $checks checks passed. $($assemblies['Robust.Shared'].Name.FullName)"
}
finally { foreach ($assembly in $assemblies.Values) { $assembly.Dispose() } }
