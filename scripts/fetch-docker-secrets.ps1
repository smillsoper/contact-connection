<#
.SYNOPSIS
    Fetches Docker build-time secrets from Azure Key Vault into the repo-root .env file.

.DESCRIPTION
    Some secrets (currently just the SignalWire PAT used to build the FreeSWITCH image)
    are consumed as Docker build args, not by .NET IConfiguration, so they can't flow
    through the AddAzureKeyVault configuration provider used by the Api/Worker projects.
    Key Vault is still the source of truth for the value — this script pulls it down into
    the local, gitignored .env file that docker-compose already reads.

    Requires the Azure CLI (az) and an active `az login` session with read access to the
    vault's secrets.

.PARAMETER VaultName
    The Key Vault name (not the full URI) to read from, e.g. "contactconnection-kv".

.EXAMPLE
    ./scripts/fetch-docker-secrets.ps1 -VaultName contactconnection-kv
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$VaultName
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $repoRoot '.env'

$pat = az keyvault secret show --vault-name $VaultName --name SignalWirePAT --query value -o tsv
if ([string]::IsNullOrWhiteSpace($pat)) {
    throw "Could not read secret 'SignalWirePAT' from vault '$VaultName' — check `az login` session and vault access."
}

$content = "# Docker build secrets — never commit this file (.gitignore covers .env*)`nSIGNALWIRE_PAT=$pat`n"
Set-Content -Path $envPath -Value $content -NoNewline -Encoding utf8

Write-Host "Wrote SIGNALWIRE_PAT to $envPath from Key Vault '$VaultName'."
