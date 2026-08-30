param(
    [string]$Configuration = "Release-Public",
    [string]$PublishDirectory = "artifacts\portable\public\win-x64",
    [string]$InternalPublishDirectory = "artifacts\portable\internal\win-x64",
    [switch]$PositiveOnly,
    [switch]$ProbeOnly
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\BatchRenamer.App\BatchRenamer.App.csproj"
$inspectorProject = Join-Path $repo "tools\BatchRenamer.PublicPurityInspector\BatchRenamer.PublicPurityInspector.csproj"
$reportPath = Join-Path $repo "artifacts\gates\public_build_purity.json"
$expectedProduct = "easy" + [char]0x91CD + [char]0x547D + [char]0x540D + " / BatchRenamer"
$expectedInternalProduct = "BatchRenamer Internal Test"
$results = [ordered]@{
    BuildFlavor = "NOT_RUN"
    CompileIsolation = "NOT_RUN"
    InternalTypesAbsent = "NOT_RUN"
    InternalCommandsAbsent = "NOT_RUN"
    InternalResourcesAbsent = "NOT_RUN"
    InternalDependenciesAbsent = "NOT_RUN"
    PublicIdentity = "NOT_RUN"
    InternalIdentity = "NOT_RUN"
    PublishDirectory = "NOT_RUN"
    NegativeControl = "NOT_RUN"
}
$evidence = [ordered]@{}

function Fail-Gate([string]$Code, [string]$Message) {
    throw [System.InvalidOperationException]::new("[$Code] $Message")
}

function Resolve-RepoPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path $repo $Path))
}

function Get-MsBuildEvidence([string]$BuildConfiguration) {
    $output = & dotnet msbuild $project "-p:Configuration=$BuildConfiguration" -getProperty:BatchRenamerBuildFlavor -getProperty:AssemblyName -getProperty:RootNamespace -getProperty:Product -getProperty:AssemblyTitle -getProperty:Version -getProperty:AssemblyVersion -getProperty:FileVersion -getProperty:InformationalVersion -getItem:Compile -getItem:Resource -getItem:EmbeddedResource -getItem:Page -getItem:Content -getItem:None -getItem:ProjectReference 2>&1
    if ($LASTEXITCODE -ne 0) { Fail-Gate "MSBUILD_INSPECTION" ($output -join [Environment]::NewLine) }
    try { return (($output -join [Environment]::NewLine) | ConvertFrom-Json) }
    catch { Fail-Gate "MSBUILD_INSPECTION" "MSBuild evidence was not valid JSON: $($_.Exception.Message)" }
}

function Assert-PublicArtifact([string]$BuildConfiguration, [string]$PublishPath) {
    if (-not (Test-Path -LiteralPath $PublishPath -PathType Container)) {
        Fail-Gate "PUBLISH_DIRECTORY" "Publish directory does not exist: $PublishPath"
    }
    $files = @(Get-ChildItem -LiteralPath $PublishPath -Recurse -File)
    if ($files.Count -ne 1 -or $files[0].Name -cne "BatchRenamer.exe") {
        Fail-Gate "PUBLISH_DIRECTORY" "Single-file contract requires exactly BatchRenamer.exe; found: $($files.FullName -join ', ')"
    }
    $results.PublishDirectory = "PASS"
    $evidence.PublishFiles = @($files.FullName)

    $msbuild = Get-MsBuildEvidence $BuildConfiguration
    $flavor = [string]$msbuild.Properties.BatchRenamerBuildFlavor
    $evidence.Configuration = $BuildConfiguration
    $evidence.BatchRenamerBuildFlavor = $flavor
    if ($BuildConfiguration -cne "Release-Public" -or $flavor -cne "Public") {
        Fail-Gate "BUILD_FLAVOR" "Expected Release-Public / Public; actual $BuildConfiguration / $flavor."
    }
    $results.BuildFlavor = "PASS"

    $publicMetadata = [ordered]@{
        AssemblyName = "BatchRenamer"
        RootNamespace = "BatchRenamer.App"
        Product = $expectedProduct
        AssemblyTitle = $expectedProduct
        Version = "1.0.0"
        AssemblyVersion = "1.0.0.0"
        FileVersion = "1.0.0.0"
        InformationalVersion = "1.0.0"
    }
    foreach ($entry in $publicMetadata.GetEnumerator()) {
        $actual = [string]$msbuild.Properties.($entry.Key)
        if ($actual -cne $entry.Value) {
            Fail-Gate "PUBLIC_IDENTITY" "MSBuild $($entry.Key) expected '$($entry.Value)'; actual '$actual'."
        }
    }

    $itemNames = @("Compile", "Resource", "EmbeddedResource", "Page", "Content", "None", "ProjectReference")
    foreach ($itemName in $itemNames) {
        $items = @($msbuild.Items.$itemName)
        $internalItems = @($items | Where-Object { ([string]$_.Identity) -match "InternalTools" -or ([string]$_.FullPath) -match "InternalTools" })
        if ($internalItems.Count -ne 0) {
            Fail-Gate "COMPILE_ISOLATION" "$itemName contains InternalTools: $($internalItems.Identity -join ', ')"
        }
        if ($itemName -eq "ProjectReference") {
            $internalProjects = @($items | Where-Object { ([string]$_.Identity) -match "Internal|QA|Test" })
            if ($internalProjects.Count -ne 0) {
                Fail-Gate "COMPILE_ISOLATION" "ProjectReference contains an internal/test dependency: $($internalProjects.Identity -join ', ')"
            }
        }
    }
    $results.CompileIsolation = "PASS"

    $assembly = Join-Path $repo "src\BatchRenamer.App\bin\$BuildConfiguration\net10.0-windows\win-x64\BatchRenamer.dll"
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        Fail-Gate "ASSEMBLY_INSPECTION" "Expected build assembly does not exist: $assembly"
    }
    $inspectorOutput = & dotnet run --project $inspectorProject -c Release -- $assembly public 2>&1
    $inspectorText = $inspectorOutput -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $inspectorText -notmatch "RELEASE_ASSEMBLY_METADATA = PASS") {
        Fail-Gate "ASSEMBLY_INSPECTION" $inspectorText
    }
    $results.InternalTypesAbsent = "PASS"
    $results.InternalCommandsAbsent = "PASS"
    $results.InternalResourcesAbsent = "PASS"
    $results.InternalDependenciesAbsent = "PASS"
    $evidence.Assembly = $assembly

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($files[0].FullName)
    if ($files[0].Name -cne "BatchRenamer.exe" -or
        $versionInfo.ProductName -cne $expectedProduct -or
        $versionInfo.FileDescription -cne $expectedProduct -or
        $versionInfo.FileVersion -cne "1.0.0.0" -or
        $versionInfo.ProductVersion -cne "1.0.0") {
        Fail-Gate "PUBLIC_IDENTITY" "Public file identity mismatch: '$($files[0].Name)' / '$($versionInfo.ProductName)' / '$($versionInfo.FileDescription)' / '$($versionInfo.FileVersion)' / '$($versionInfo.ProductVersion)'."
    }
    if ($versionInfo.ProductName -match "Internal|INTERNAL TEST" -or $versionInfo.ProductVersion -match "internal") {
        Fail-Gate "PUBLIC_IDENTITY" "Internal identity marker detected."
    }
    $results.PublicIdentity = "PASS"
    $evidence.ProductName = $versionInfo.ProductName
    $evidence.FileDescription = $versionInfo.FileDescription
    $evidence.FileVersion = $versionInfo.FileVersion
    $evidence.ProductVersion = $versionInfo.ProductVersion
}

function Write-GateReport([string]$Status, [string]$Failure) {
    if ($ProbeOnly) { return }
    $reportDirectory = Split-Path -Parent $reportPath
    New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
    $report = [ordered]@{
        GeneratedAtUtc = [DateTime]::UtcNow.ToString("o")
        PUBLIC_BUILD_PURITY = $Status
        Results = $results
        Evidence = $evidence
        Failure = $Failure
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}

try {
    $publish = Resolve-RepoPath $PublishDirectory
    Assert-PublicArtifact $Configuration $publish

    if ($PositiveOnly) {
        $results.NegativeControl = "NOT_RUN_POSITIVE_ONLY"
    }
    else {
        $internalPublish = Resolve-RepoPath $InternalPublishDirectory
        $internalExe = Join-Path $internalPublish "BatchRenamer.exe"
        if (-not (Test-Path -LiteralPath $internalExe -PathType Leaf)) {
            Fail-Gate "NEGATIVE_CONTROL_SETUP" "Internal control artifact does not exist: $internalExe"
        }
        $internalFiles = @(Get-ChildItem -LiteralPath $internalPublish -Recurse -File)
        if ($internalFiles.Count -ne 1 -or $internalFiles[0].Name -cne "BatchRenamer.exe") {
            Fail-Gate "NEGATIVE_CONTROL_SETUP" "Internal single-file contract is invalid: $($internalFiles.FullName -join ', ')"
        }
        $internalMsbuild = Get-MsBuildEvidence "Release-Internal"
        $internalMetadata = [ordered]@{
            BatchRenamerBuildFlavor = "Internal"
            AssemblyName = "BatchRenamer"
            RootNamespace = "BatchRenamer.App"
            Product = $expectedInternalProduct
            AssemblyTitle = $expectedInternalProduct
            Version = "1.0.0"
            AssemblyVersion = "1.0.0.0"
            FileVersion = "1.0.0.0"
            InformationalVersion = "1.0.0-internal"
        }
        foreach ($entry in $internalMetadata.GetEnumerator()) {
            $actual = [string]$internalMsbuild.Properties.($entry.Key)
            if ($actual -cne $entry.Value) {
                Fail-Gate "NEGATIVE_CONTROL_SETUP" "Internal MSBuild $($entry.Key) expected '$($entry.Value)'; actual '$actual'."
            }
        }
        $internalIdentity = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($internalExe)
        if ($internalIdentity.ProductName -cne $expectedInternalProduct -or
            $internalIdentity.FileDescription -cne $expectedInternalProduct -or
            $internalIdentity.FileVersion -cne "1.0.0.0" -or
            $internalIdentity.ProductVersion -cne "1.0.0-internal") {
            Fail-Gate "NEGATIVE_CONTROL_SETUP" "Internal control identity is invalid: '$($internalIdentity.ProductName)' / '$($internalIdentity.FileDescription)' / '$($internalIdentity.FileVersion)' / '$($internalIdentity.ProductVersion)'."
        }

        $internalAssembly = Join-Path $repo "src\BatchRenamer.App\bin\Release-Internal\net10.0-windows\win-x64\BatchRenamer.dll"
        if (-not (Test-Path -LiteralPath $internalAssembly -PathType Leaf)) {
            Fail-Gate "NEGATIVE_CONTROL_SETUP" "Internal build assembly does not exist: $internalAssembly"
        }
        $internalInspectorOutput = & dotnet run --project $inspectorProject -c Release -- $internalAssembly internal 2>&1
        $internalInspectorText = $internalInspectorOutput -join [Environment]::NewLine
        if ($LASTEXITCODE -ne 0 -or $internalInspectorText -notmatch "RELEASE_ASSEMBLY_METADATA = PASS") {
            Fail-Gate "NEGATIVE_CONTROL_SETUP" $internalInspectorText
        }
        $results.InternalIdentity = "PASS"
        $evidence.InternalProductName = $internalIdentity.ProductName
        $evidence.InternalFileDescription = $internalIdentity.FileDescription
        $evidence.InternalFileVersion = $internalIdentity.FileVersion
        $evidence.InternalProductVersion = $internalIdentity.ProductVersion
        $evidence.InternalQaRegression = "InternalQaCenterWindow and InternalTools_PreviewKeyDown present."

        $engine = (Get-Process -Id $PID).Path
        $negativeOutput = & $engine -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -Configuration "Release-Internal" -PublishDirectory $internalPublish -PositiveOnly -ProbeOnly 2>&1
        $negativeExitCode = $LASTEXITCODE
        $negativeText = $negativeOutput -join [Environment]::NewLine
        if ($negativeExitCode -eq 0 -or $negativeText -notmatch "GATE_FAILURE_CODE=BUILD_FLAVOR") {
            Fail-Gate "NEGATIVE_CONTROL" "Internal artifact was not rejected specifically by build flavor. Exit=$negativeExitCode Output=$negativeText"
        }
        $results.NegativeControl = "PASS"
        $evidence.NegativeControlExitCode = $negativeExitCode
        $evidence.NegativeControlReason = "Internal artifact was correctly rejected by BUILD_FLAVOR."
        Write-Output "NEGATIVE_CONTROL = PASS (Internal artifact rejected; exit code $negativeExitCode)"
    }

    Write-GateReport "PASS" ""
    Write-Output "PUBLIC_BUILD_PURITY = PASS"
    exit 0
}
catch {
    Write-GateReport "FAIL" $_.Exception.Message
    $failureCode = "UNCLASSIFIED"
    if ($_.Exception.Message -match "^\[([A-Z_]+)\]") { $failureCode = $Matches[1] }
    Write-Output "PUBLIC_BUILD_PURITY = FAIL"
    Write-Output "GATE_FAILURE_CODE=$failureCode"
    if ($ProbeOnly) { Write-Output "GATE_FAILURE_MESSAGE=$($_.Exception.Message)" }
    else { Write-Error $_.Exception.Message }
    exit 1
}
