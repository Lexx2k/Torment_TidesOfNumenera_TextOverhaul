param(
 [Parameter(Mandatory=$true)][string]$GameManaged,
 [Parameter(Mandatory=$true)][string]$OriginalAssembly,
 [Parameter(Mandatory=$true)][string]$CecilPath,
 [string]$OutputDirectory=''
)
$ErrorActionPreference='Stop'
if(-not $OutputDirectory){$OutputDirectory=Join-Path $PSScriptRoot 'build\TidesOfNumenera_Data\Managed'}
$expected='677CB9884B3ADC18A65005D37A55784164821F484F970339CD15297105686664'
if((Get-FileHash -LiteralPath $OriginalAssembly).Hash -ne $expected){throw 'Use the pristine supported Windows Steam Assembly-CSharp.dll, not an already patched DLL.'}
$CecilPath=(Resolve-Path -LiteralPath $CecilPath).Path
$GameManaged=(Resolve-Path -LiteralPath $GameManaged).Path
$OriginalAssembly=(Resolve-Path -LiteralPath $OriginalAssembly).Path
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory=(Resolve-Path -LiteralPath $OutputDirectory).Path
$sdk=(& dotnet --list-sdks | Where-Object {$_ -match '^10\.'} | Select-Object -Last 1)
if(-not $sdk){throw '.NET 10 SDK required'}
if($sdk -notmatch '^(\S+)\s+\[(.+)\]'){throw 'Cannot resolve .NET SDK path'}
$compiler=Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
$helper=Join-Path $OutputDirectory 'Torment.VerticalDialogue.dll'
$argsCsc=@($compiler,'/nologo','/target:library','/langversion:5','/nostdlib+','/noconfig','/optimize+','/debug-',('/out:'+$helper))
foreach($name in @('mscorlib.dll','System.dll','System.Core.dll','UnityEngine.dll','UnityEngine.UI.dll')){$argsCsc+=('/r:'+(Join-Path $GameManaged $name))}
$argsCsc+=@((Join-Path $PSScriptRoot 'src\VerticalDialogue.cs'),(Join-Path $PSScriptRoot 'src\HangingIndent.cs'))
& dotnet @argsCsc
if($LASTEXITCODE -ne 0){throw 'Helper compilation failed'}
& dotnet build (Join-Path $PSScriptRoot 'patcher\Patcher.csproj') --configfile (Join-Path $PSScriptRoot 'NuGet.Config') "-p:CecilPath=$CecilPath" --nologo -v quiet
if($LASTEXITCODE -ne 0){throw 'Patcher compilation failed'}
& dotnet (Join-Path $PSScriptRoot 'patcher\bin\Debug\net10.0\Patcher.dll') $OriginalAssembly $helper (Join-Path $OutputDirectory 'Assembly-CSharp.dll') $GameManaged | Set-Content (Join-Path $OutputDirectory 'assembly-patch-evidence.json') -Encoding UTF8
if($LASTEXITCODE -ne 0){throw 'Assembly patch failed'}
Write-Host "Built both mod DLLs in $OutputDirectory. No game files were installed."

