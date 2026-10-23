# PROTOTYPE - NOT FOR PRODUCTION
# Question: Run domain checks and build a placeholder Player in installed Unity 6.
# Date: 2026-10-23
param([Parameter(Mandatory=$true)][string]$EditorPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$prototypeRoot = Join-Path $repoRoot 'prototypes/NormalCampaign'
if (!(Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw 'Pass an existing absolute path to Unity.exe.' }
$pin = Get-Content -LiteralPath (Join-Path $prototypeRoot 'ProjectSettings/ProjectVersion.txt') -Raw
if ($pin -notmatch 'm_EditorVersion: 6000.6.5f1') { throw 'Unexpected project Editor version; expected 6000.6.5f1.' }
& py -3 (Join-Path $repoRoot 'tools/prepare_prototype.py')
if ($LASTEXITCODE -ne 0) { throw 'Canonical data conversion failed.' }
$evidenceDir = Join-Path $repoRoot 'production/qa/evidence/local'
New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
$logPath = Join-Path $evidenceDir ('unity-build-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
$editorArgs = @('-batchmode','-nographics','-quit','-projectPath',('"' + $prototypeRoot + '"'),'-buildTarget','StandaloneWindows64','-executeMethod','NormalCampaignPrototype.EditorTools.PrototypeBuild.Build','-logFile',('"' + $logPath + '"'))
$buildProcess = Start-Process -FilePath $EditorPath -ArgumentList $editorArgs -WindowStyle Hidden -Wait -PassThru
Write-Output "Unity exit=$($buildProcess.ExitCode); log=$logPath"
if ($buildProcess.ExitCode -ne 0) { throw "Unity build failed; see $logPath" }
