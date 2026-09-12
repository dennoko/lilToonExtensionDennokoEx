param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe',
    [string]$TestProject = (Join-Path ([IO.Path]::GetTempPath()) ('DennokoExMaskBakeTests-' + [Guid]::NewGuid().ToString('N'))),
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$repoPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$TestProject = [IO.Path]::GetFullPath($TestProject)
if (Test-Path -LiteralPath $TestProject) { throw "Use a new isolated test directory: $TestProject" }
New-Item -ItemType Directory -Path "$TestProject/Assets/Editor", "$TestProject/Packages", "$TestProject/ProjectSettings" -Force | Out-Null
foreach ($name in @('DennokoExMaskPacker.cs', 'DennokoExMaskSync.cs', 'DennokoExMaskBuild.cs')) {
    Copy-Item -LiteralPath "$repoPath/Editor/$name" -Destination "$TestProject/Assets/Editor/$name"
}
Copy-Item -LiteralPath "$repoPath/Shaders/DennokoEx_MaskPacker.shader" -Destination "$TestProject/Assets/MaskPacker.shader"
Copy-Item -LiteralPath "$PSScriptRoot/MaskBakeTests.cs" -Destination "$TestProject/Assets/Editor/MaskBakeTests.cs"
Copy-Item -LiteralPath "$PSScriptRoot/MaskFixture.shader" -Destination "$TestProject/Assets/MaskFixture.shader"
$fixtureText = Get-Content -LiteralPath "$PSScriptRoot/MaskFixture.shader" -Raw
$legacyText = $fixtureText.Replace('CompatibilityFixture', 'LegacyFixture') -replace '(?m)^.*_CustomMaskPacked.*\r?\n', ''
Set-Content -LiteralPath "$TestProject/Assets/LegacyFixture.shader" -Value $legacyText -Encoding utf8
$dependencies = @{}
foreach ($module in @('animation', 'imageconversion', 'imgui', 'jsonserialize', 'physics', 'ui', 'uielements')) {
    $dependencies["com.unity.modules.$module"] = '1.0.0'
}
@{ dependencies = $dependencies } | ConvertTo-Json | Set-Content -LiteralPath "$TestProject/Packages/manifest.json" -Encoding utf8
"m_EditorVersion: 2022.3.22f1`nm_EditorVersionWithRevision: 2022.3.22f1 (887be4894c44)" | Set-Content -LiteralPath "$TestProject/ProjectSettings/ProjectVersion.txt"
Write-Output "Test project: $TestProject"
if ($PrepareOnly) { return }
foreach ($phase in @('PrepareLegacy', 'Run', 'VerifyRestart')) {
    if ($phase -eq 'Run') {
        $fixtureText.Replace('CompatibilityFixture', 'LegacyFixture') | Set-Content -LiteralPath "$TestProject/Assets/LegacyFixture.shader" -Encoding utf8
    }
    $arguments = @('-batchmode', '-quit', '-projectPath', ('"' + $TestProject + '"'), '-executeMethod', "MaskBakeTests.$phase", '-logFile', ('"' + "$TestProject/$phase.log" + '"'))
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Unity test phase $phase failed ($($process.ExitCode)); see $TestProject/$phase.log" }
}
foreach ($result in @('prepare-passed.txt', 'tests-passed.txt', 'restart-passed.txt')) {
    Get-Content -LiteralPath "$TestProject/$result"
}
