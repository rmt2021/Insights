[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string[]]$ConfigNames,

    [Parameter(Mandatory = $true)]
    [string]$BuildVersion,

    [Parameter(Mandatory = $true)]
    [string]$WebsiteZipPath,

    [Parameter(Mandatory = $true)]
    [string]$WorkerZipPath,

    [Parameter(Mandatory = $false)]
    [string]$AzureFunctionsHostZipPath
)

Import-Module (Join-Path $PSScriptRoot "scripts/NuGet.Insights.psm1")

function New-ServiceModelFile($resourceSettings) {
    # Docs: https://ev2docs.azure.net/getting-started/authoring/service-model/servicemodel.html
    $definitionName = "Deploy.ServiceDefinition"
    $resourceName = "Deploy.ResourceDefinition"
    $serviceModel = [ordered]@{
        "`$schema"                      = "http://schema.express.azure.com/schemas/2015-01-01-alpha/ServiceModel.json";
        contentVersion                  = "0.0.0.1";
        serviceMetadata                 = [ordered]@{
            serviceGroup      = "NuGet.Insights";
            environment       = $resourceSettings.EnvironmentName;
            serviceIdentifier = $resourceSettings.ServiceTreeId;
        };
        serviceResourceGroupDefinitions = @(
            [ordered]@{
                name                       = $definitionName;
                serviceResourceDefinitions = @(
                    [ordered]@{
                        name       = $resourceName;
                        composedOf = [ordered]@{
                            arm = [ordered]@{
                                templatePath = Get-TemplatePath "main";
                            }
                        }
                    }
                )
            }
        );
        serviceResourceGroups           = @(
            [ordered]@{
                name                   = "Deploy.ServiceInstance";
                azureResourceGroupName = $resourceSettings.ResourceGroupName;
                location               = $resourceSettings.Location;
                instanceOf             = $definitionName;
                azureSubscriptionId    = $resourceSettings.SubscriptionId;
                serviceResources       = @(
                    [ordered]@{
                        name              = $serviceResourceName;
                        instanceOf        = $resourceName;
                        armParametersPath = Get-ParametersPath $resourceSettings.ConfigName;
                    }
                );
            }
        )
    }

    $serviceModelPath = Get-ServiceModelPath $resourceSettings.ConfigName
    $serviceModelPath = Join-Path $ev2 $serviceModelPath
    $dirPath = Split-Path $serviceModelPath
    if (!(Test-Path $dirPath)) {
        New-Item $dirPath -ItemType Directory | Out-Null
    }

    $serviceModel | ConvertTo-Json -Depth 100 | Out-File $serviceModelPath -Encoding UTF8
}

function New-RolloutSpecFile($resourceSettings) {
    # Docs: https://ev2docs.azure.net/getting-started/authoring/rollout-spec/rolloutspec.html
    $rolloutSpec = [ordered]@{
        "`$schema"        = "http://schema.express.azure.com/schemas/2015-01-01-alpha/RolloutSpec.json";
        contentVersion    = "1.0.0.0";
        rolloutMetadata   = [ordered]@{
            serviceModelPath = Get-ServiceModelPath $resourceSettings.ConfigName
            name             = "NuGet.Insights-$($resourceSettings.EnvironmentName)"
            rolloutType      = "Major";
            buildSource      = [ordered]@{
                parameters = [ordered]@{
                    versionFile = "BuildVer.txt"
                }
            }
        };
        orchestratedSteps = @(
            [ordered]@{
                name       = "Deploy.OrchestratedStep";
                targetType = "ServiceResource";
                targetName = $serviceResourceName;
                actions    = @( "deploy" );
                dependsOn  = @();
            }
        )
    }

    $rolloutSpecPath = Get-RolloutSpecPath $resourceSettings.ConfigName
    $rolloutSpecPath = Join-Path $ev2 $rolloutSpecPath
    $dirPath = Split-Path $rolloutSpecPath
    if (!(Test-Path $dirPath)) {
        New-Item $dirPath -ItemType Directory | Out-Null
    }

    $rolloutSpec | ConvertTo-Json -Depth 100 | Out-File $rolloutSpecPath -Encoding UTF8
}

function New-Bicep($name) {
    $bicepPath = Join-Path $PSScriptRoot "bicep/$name.bicep"
    $templatePath = Join-Path $ev2 (Get-TemplatePath $name)

    $templatesDir = Split-Path $templatePath
    if (!(Test-Path $templatesDir)) {
        New-Item $templatesDir -ItemType Directory | Out-Null
    }

    $bicepExe, $bicepArgs = Get-Bicep
    & $bicepExe @bicepArgs $bicepPath --outfile $templatePath
    if ($LASTEXITCODE -ne 0) {
        throw "Command 'bicep build' failed with exit code $LASTEXITCODE."
    }
}

function Get-ServiceModelPath($configName) {
    return "ServiceModels/$configName.ServiceModel.json"
}

function Get-RolloutSpecPath($configName) {
    return "$configName.RolloutSpec.json"
}

function Get-ParametersPath($configName) {
    return "Parameters/$configName.Parameters.json"
}

function Get-TemplatePath($name) {
    return "Templates/$name.Template.json"
}

# Declare shared variables
$artifacts = Join-Path $PSScriptRoot "../artifacts"
$ev2 = Join-Path $artifacts "ExpressV2"
$serviceResourceName = "Deploy.ResourceInstance"
$websiteBinPath = "bin/Website.zip"
$workerBinPath = "bin/Worker.zip"
$azureFunctionsHostBinPath = "bin/AzureFunctionsHost.zip"
$workerStandaloneEnvPathPattern = "bin/WorkerStandalone.{0}.env"
$installWorkerStandaloneSourcePath = Join-Path $PSScriptRoot "scripts\Install-WorkerStandalone.ps1"
$installWorkerStandalonePath = "bin/Install-WorkerStandalone.ps1"
$setDeploymentLabelSourcePath = Join-Path $PSScriptRoot "scripts\Set-DeploymentLabel.ps1"
$setDeploymentLabelPath = "Set-DeploymentLabel.ps1"

# Install Bicep, if needed.
if (!(Get-Command bicep -CommandType Application -ErrorAction Ignore)) {
    Write-Host "Installing Bicep..."
    # Source: https://github.com/Azure/bicep/blob/main/docs/installing.md#manual-with-powershell
    if ($IsLinux) {
        curl -Lo bicep.bin https://github.com/Azure/bicep/releases/latest/download/bicep-linux-x64
        chmod +x ./bicep.bin
        sudo mv ./bicep.bin /usr/local/bin/bicep
    }
    elseif ($IsMacOS) {
        curl -Lo bicep https://github.com/Azure/bicep/releases/latest/download/bicep-osx-x64
        chmod +x ./bicep
        sudo spctl --add ./bicep
        sudo mv ./bicep /usr/local/bin/bicep
    }
    else {
        $installPath = "$env:USERPROFILE\.bicep"
        $installDir = New-Item -ItemType Directory -Path $installPath -Force
        $installDir.Attributes += 'Hidden'
        (New-Object Net.WebClient).DownloadFile("https://github.com/Azure/bicep/releases/latest/download/bicep-win-x64.exe", "$installPath\bicep.exe")
        $currentPath = (Get-Item -path "HKCU:\Environment" ).GetValue('Path', '', 'DoNotExpandEnvironmentNames')
        if (-not $currentPath.Contains("%USERPROFILE%\.bicep")) { setx PATH ($currentPath + ";%USERPROFILE%\.bicep") }
        if (-not $env:path.Contains($installPath)) { $env:path += ";$installPath" }
    }
}

# Compile the Bicep templates to raw ARM JSON.
New-Bicep "main"
New-Bicep "storage-and-kv"

$bin = Join-Path $ev2 "bin"
if (!(Test-Path $bin)) {
    New-Item $bin -ItemType Directory | Out-Null
}

# Build the Ev2 artifacts
$anyUseSpotWorkers = $false
foreach ($configName in $ConfigNames) {
    $resourceSettings = Get-ResourceSettings $configName

    if ($resourceSettings.ConfigName -ne $resourceSettings.StampName) {
        throw "The config name must match the stamp name."
    }
    if (!$resourceSettings.SubscriptionId) {
        $configPath = Get-ConfigPath $resourceSettings.ConfigName
        throw "A subscription ID is required for generating Ev2 artifacts. Specify a value in file $configPath at JSON path $.Deployment.SubscriptionId."
    }
    if (!$resourceSettings.ServiceTreeId) {
        $configPath = Get-ConfigPath $resourceSettings.ConfigName
        throw "A ServiceTree ID is required for generating Ev2 artifacts. Specify a value in file $configPath at JSON path $.Deployment.ServiceTreeId."
    }
    if (!$resourceSettings.EnvironmentName) {
        $configPath = Get-ConfigPath $resourceSettings.ConfigName
        throw "A environment name is required for generating Ev2 artifacts. Specify a value in file $configPath at JSON path $.Deployment.EnvironmentName."
    }
    if (!$resourceSettings.WebsiteAadAppClientId) {
        $configPath = Get-ConfigPath $resourceSettings.ConfigName
        throw "A website AAD client ID is required for generating Ev2 artifacts. You can use the prepare.ps1 script to create the AAD app registration for the first time. Specify a value in file $configPath at JSON path $.deployment.WebsiteAadAppClientId."
    }

    $parameters = New-MainParameters $resourceSettings $websiteBinPath $workerBinPath "PLACEHOLDER"
    $parametersPath = Join-Path $ev2 (Get-ParametersPath $resourceSettings.ConfigName)
    New-ParameterFile $parameters @("websiteZipUrl", "workerZipUrl") $parametersPath
    New-ServiceModelFile $resourceSettings
    New-RolloutSpecFile $resourceSettings

    $standaloneEnv = New-WorkerStandaloneEnv $resourceSettings
    $standaloneEnvFileName = $workerStandaloneEnvPathPattern -f $resourceSettings.ConfigName
    $standaloneEnv | Out-EnvFile -FilePath (Join-Path $ev2 $standaloneEnvFileName)

    $anyUseSpotWorkers = $anyUseSpotWorkers -or $resourceSettings.UseSpotWorkers
}

$BuildVersion | Out-File (Join-Path $ev2 "BuildVer.txt") -NoNewline -Encoding UTF8

# Copy the runtime assets
Copy-Item $WebsiteZipPath -Destination (Join-Path $ev2 $websiteBinPath) -Verbose
Copy-Item $WorkerZipPath -Destination (Join-Path $ev2 $workerBinPath) -Verbose
if ($anyUseSpotWorkers) {
    if (!$AzureFunctionsHostZipPath) {
        throw "No AzureFunctionsHostZipPath parameter was provided but at least one of the configurations has UseSpotWorkers set to true."
    }
    Copy-Item $AzureFunctionsHostZipPath -Destination (Join-Path $ev2 $azureFunctionsHostBinPath) -Verbose
}
Copy-Item $installWorkerStandaloneSourcePath -Destination (Join-Path $ev2 $installWorkerStandalonePath) -Verbose
Write-Host "Wrote Ev2 files to: $ev2"

Copy-Item $setDeploymentLabelSourcePath -Destination (Join-Path $artifacts $setDeploymentLabelPath) -Verbose
